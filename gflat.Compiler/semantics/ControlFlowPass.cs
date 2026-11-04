using System;
using System.Collections.Generic;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;
using gflat.diagnostics;

namespace gflat.semantics
{
    public class ControlFlowPass
    {
        private readonly TypeChecker _typeChecker;
        private readonly ConstEvaluator _constEvaluator;
        private readonly DiagnosticBag _diagnostics;
        private readonly Dictionary<AstNode, ControlFlowGraph> _graphs = new();
        private readonly Dictionary<AstNode, bool?> _conditions = new();
        private readonly Dictionary<AstNode, Exits> _exits = new();
        private readonly HashSet<AstNode> _diagnosed = new();

        [Flags]
        private enum Exits { None = 0, Normal = 1, Return = 2, Throw = 4, Break = 8, Continue = 16 }

        public ControlFlowGraph Graph(AstNode statement)
        {
            if (!_graphs.TryGetValue(statement, out var graph))
                _graphs[statement] = graph = new ControlFlowGraph(statement, ConstantCondition);
            return graph;
        }

        public ControlFlowPass(TypeChecker typeChecker, ConstEvaluator constEvaluator, DiagnosticBag? diagnostics = null)
        {
            _typeChecker = typeChecker;
            _constEvaluator = constEvaluator;
            _diagnostics = diagnostics ?? new DiagnosticBag();
        }

        public void Execute(CompilationUnit cu)
        {
            for (int i = 0; i < cu.Members.Count; i++)
            {
                CheckMember(cu.Members[i]);
            }

            foreach (NamespaceDeclaration ns in cu.Namespaces)
            {
                CheckNamespace(ns);
            }
        }

        private void CheckNamespace(NamespaceDeclaration ns)
        {
            for (int i = 0; i < ns.Members.Count; i++)
            {
                CheckMember(ns.Members[i]);
            }
        }

        private void CheckMember(AstNode member)
        {
            using var context = SourceContext.Enter(member.Span);
            if (member is NamespaceDeclaration ns)
            {
                CheckNamespace(ns);
            }
            else if (member is MethodDeclaration method)
            {
                CheckMethod(method);
            }
            else if (member is ClassDeclaration classDecl)
            {
                if (classDecl.IsGeneric) return;
                for (int i = 0; i < classDecl.Members.Count; i++)
                {
                    CheckMember(classDecl.Members[i]);
                }
            }
            else if (member is StructDeclaration structDecl)
            {
                if (structDecl.IsGeneric) return;
                for (int i = 0; i < structDecl.Members.Count; i++)
                {
                    CheckMember(structDecl.Members[i]);
                }
            }
            else if (member is OperatorDeclaration op)
            {
                CheckOperator(op);
            }
        }

        public void CheckMethod(MethodDeclaration method)
        {
            if (!_typeChecker.WasBodyChecked(method)) return;
            if (method.IsGeneric || method.Body == null || method.IsAbstract || _typeChecker.IsDeferredReflectionBody(method))
            {
                return;
            }

            bool terminates = StatementTerminates(method.Body);

            TypeExpression resolvedRet = _typeChecker.ResolveAlias(method.ReturnType);
            if (!IsVoidType(resolvedRet) && !terminates)
            {
                string declaration = method.PropertyName switch
                {
                    null => $"function '{_typeChecker.DisplayName(method.Name)}'",
                    "$index" => "indexer getter",
                    var property => $"property getter '{property}'"
                };
                throw new TypeCheckException($"Not all code paths return a value in {declaration}", method.Line);
            }
        }

        public void CheckOperator(OperatorDeclaration op)
        {
            if (!_typeChecker.WasBodyChecked(op)) return;
            if (op.Body == null)
            {
                return;
            }

            bool terminates = StatementTerminates(op.Body);

            TypeExpression resolvedRet = _typeChecker.ResolveAlias(op.ReturnType);
            if (!IsVoidType(resolvedRet) && !terminates)
            {
                throw new TypeCheckException($"Not all code paths return a value in operator '{op.OperatorSymbol}'", op.Line);
            }
        }

        private bool IsVoidType(TypeExpression type)
        {
            TypeExpression resolved = _typeChecker.ResolveAlias(type);
            return resolved is NamedTypeExpression named && named.Name == "void";
        }

        public bool StatementTerminates(AstNode stmt)
        {
            if (_diagnosed.Add(stmt)) DiagnoseTermination(stmt);
            return (StatementExits(stmt) & Exits.Normal) == 0;
        }

        // Each summary describes the statement on its own. A parent consumes
        // break/continue exits at its loop boundary and composes normal exits.
        private Exits StatementExits(AstNode statement)
        {
            if (_exits.TryGetValue(statement, out var cached)) return cached;
            Exits result;
            switch (statement)
            {
                case ReturnStatement: result = Exits.Return; break;
                case ThrowStatement: result = Exits.Throw; break;
                case BreakStatement: result = Exits.Break; break;
                case ContinueStatement: result = Exits.Continue; break;
                case BlockStatement block:
                    result = Exits.Normal;
                    foreach (var child in block.Statements)
                    {
                        var childExits = StatementExits(child);
                        result = Sequence(result, childExits);
                    }
                    break;
                case IfStatement branch:
                    var then = StatementExits(branch.Then);
                    var otherwise = branch.Else == null ? Exits.Normal : StatementExits(branch.Else);
                    result = ConstantCondition(branch.Condition) switch { true => then, false => otherwise, _ => then | otherwise };
                    break;
                case WhileStatement loop:
                    result = LoopExits(loop.Body, ConstantCondition(loop.Condition));
                    break;
                case ForStatement loop:
                    result = LoopExits(loop.Body, loop.Condition == null ? true : ConstantCondition(loop.Condition), loop.Increment);
                    if (loop.Initializer != null) result = Sequence(StatementExits(loop.Initializer), result);
                    break;
                case ForeachStatement loop:
                    result = Exits.Normal | (StatementExits(loop.Body) & (Exits.Return | Exits.Throw));
                    break;
                case TryStatement guarded:
                    // Match the graph's conservative call-exception edge and
                    // the possibility that none of the typed catches matches.
                    result = Exits.Throw | (StatementExits(guarded.TryBlock) & ~Exits.Throw);
                    foreach (var clause in guarded.CatchClauses) result |= StatementExits(clause.Body);
                    break;
                default: result = Exits.Normal; break;
            }
            _exits[statement] = result;
            return result;
        }

        private static Exits Sequence(Exits first, Exits next) =>
            (first & ~Exits.Normal) | ((first & Exits.Normal) != 0 ? next : Exits.None);

        private Exits LoopExits(AstNode body, bool? condition, AstNode? increment = null)
        {
            var exits = StatementExits(body);
            if (condition == false) return Exits.Normal;
            var result = exits & (Exits.Return | Exits.Throw);
            if ((exits & Exits.Break) != 0 || condition == null) result |= Exits.Normal;
            if (increment != null && (exits & (Exits.Normal | Exits.Continue)) != 0)
            {
                var step = StatementExits(increment);
                result |= step & (Exits.Return | Exits.Throw);
                if ((step & Exits.Break) != 0) result |= Exits.Normal;
            }
            return result;
        }

        private bool DiagnoseTermination(AstNode stmt)
        {
            if (stmt is ReturnStatement)
            {
                return true;
            }

            if (stmt is ThrowStatement)
            {
                return true;
            }

            if (stmt is BlockStatement block)
            {
                for (int i = 0; i < block.Statements.Count; i++)
                {
                    AstNode current = block.Statements[i];
                    bool terminates = StatementTerminates(current);
                    if (terminates || current is BreakStatement || current is ContinueStatement)
                    {
                        if (i + 1 < block.Statements.Count)
                        {
                            _diagnostics.Report(DiagnosticRules.GF2001_UnreachableCode, block.Statements[i + 1].Line);
                        }
                        return terminates;
                    }
                }
                return false;
            }

            if (stmt is IfStatement ifStmt)
            {
                if (IsConstantTrue(ifStmt.Condition))
                {
                    if (ifStmt.Else != null)
                    {
                        _diagnostics.Report(DiagnosticRules.GF2001_UnreachableCode, ifStmt.Else.Line);
                    }
                    return StatementTerminates(ifStmt.Then);
                }

                if (IsConstantFalse(ifStmt.Condition))
                {
                    _diagnostics.Report(DiagnosticRules.GF2001_UnreachableCode, ifStmt.Then.Line);
                    if (ifStmt.Else != null)
                    {
                        return StatementTerminates(ifStmt.Else);
                    }
                    return false;
                }

                if (ifStmt.Else == null)
                {
                    StatementTerminates(ifStmt.Then);
                    return false;
                }

                bool thenTerminates = StatementTerminates(ifStmt.Then);
                bool elseTerminates = StatementTerminates(ifStmt.Else);
                return thenTerminates && elseTerminates;
            }

            if (stmt is WhileStatement whileStmt)
            {
                if (IsConstantFalse(whileStmt.Condition))
                {
                    _diagnostics.Report(DiagnosticRules.GF2001_UnreachableCode, whileStmt.Body.Line);
                    return false;
                }

                StatementTerminates(whileStmt.Body);
                if (IsConstantTrue(whileStmt.Condition))
                {
                    if (!HasBreakTargetingCurrentLoop(whileStmt.Body))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (stmt is ForStatement forStmt)
            {
                if (forStmt.Condition != null && IsConstantFalse(forStmt.Condition))
                {
                    _diagnostics.Report(DiagnosticRules.GF2001_UnreachableCode, forStmt.Body.Line);
                    return false;
                }

                StatementTerminates(forStmt.Body);
                if (forStmt.Condition == null || IsConstantTrue(forStmt.Condition))
                {
                    if (!HasBreakTargetingCurrentLoop(forStmt.Body))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (stmt is ForeachStatement foreachStmt)
            {
                StatementTerminates(foreachStmt.Body);
                return false;
            }

            if (stmt is TryStatement tryStmt)
            {
                if (!StatementTerminates(tryStmt.TryBlock))
                {
                    return false;
                }

                for (int i = 0; i < tryStmt.CatchClauses.Count; i++)
                {
                    if (!StatementTerminates(tryStmt.CatchClauses[i].Body))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        private bool HasBreakTargetingCurrentLoop(AstNode node)
        {
            if (node is BreakStatement)
            {
                return true;
            }

            // Inner loops absorb their own break statements
            if (node is WhileStatement or ForStatement or ForeachStatement)
            {
                return false;
            }

            if (node is BlockStatement block)
            {
                for (int i = 0; i < block.Statements.Count; i++)
                {
                    if (HasBreakTargetingCurrentLoop(block.Statements[i]))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (node is IfStatement ifStmt)
            {
                if (HasBreakTargetingCurrentLoop(ifStmt.Then))
                {
                    return true;
                }
                if (ifStmt.Else != null && HasBreakTargetingCurrentLoop(ifStmt.Else))
                {
                    return true;
                }
                return false;
            }

            if (node is TryStatement tryStmt)
            {
                if (HasBreakTargetingCurrentLoop(tryStmt.TryBlock))
                {
                    return true;
                }
                for (int i = 0; i < tryStmt.CatchClauses.Count; i++)
                {
                    if (HasBreakTargetingCurrentLoop(tryStmt.CatchClauses[i].Body))
                    {
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        private bool IsConstantTrue(AstNode expr)
            => ConstantCondition(expr) == true;

        private bool IsConstantFalse(AstNode expr)
            => ConstantCondition(expr) == false;

        private bool? ConstantCondition(AstNode expr)
        {
            if (_conditions.TryGetValue(expr, out var cached)) return cached;
            bool? result = expr is LiteralExpression literal && literal.Token.Kind is TokenKind.True or TokenKind.False
                ? literal.Token.Kind == TokenKind.True
                : _constEvaluator.TryEvaluateCondition(expr, out var value) ? value : null;
            _conditions.Add(expr, result);
            return result;
        }
    }
}
