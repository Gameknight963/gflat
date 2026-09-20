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

        public ControlFlowGraph Graph(AstNode statement)
        {
            if (!_graphs.TryGetValue(statement, out var graph))
                _graphs[statement] = graph = new ControlFlowGraph(statement, expression =>
                    IsConstantTrue(expression) ? true : IsConstantFalse(expression) ? false : null);
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
            if (method.IsGeneric || method.Body == null || method.IsAbstract)
            {
                return;
            }

            bool terminates = StatementTerminates(method.Body);

            TypeExpression resolvedRet = _typeChecker.ResolveAlias(method.ReturnType);
            if (!IsVoidType(resolvedRet) && !terminates)
            {
                throw new TypeCheckException($"Not all code paths return a value in function '{method.Name}'", method.Line);
            }
        }

        public void CheckOperator(OperatorDeclaration op)
        {
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
            if (!_graphs.ContainsKey(stmt)) DiagnoseTermination(stmt);
            return !Graph(stmt).FallsThrough;
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
        {
            if (expr is LiteralExpression lit)
            {
                if (lit.Token.Kind == TokenKind.True)
                {
                    return true;
                }
            }

            if (_constEvaluator.TryEvaluate(expr, out ConstValue? val, out _))
            {
                if (val is ConstValue.Boolean bVal)
                {
                    return bVal.Value;
                }
            }

            return false;
        }

        private bool IsConstantFalse(AstNode expr)
        {
            if (expr is LiteralExpression lit)
            {
                if (lit.Token.Kind == TokenKind.False)
                {
                    return true;
                }
            }

            if (_constEvaluator.TryEvaluate(expr, out ConstValue? val, out _))
            {
                if (val is ConstValue.Boolean bVal)
                {
                    return !bVal.Value;
                }
            }

            return false;
        }
    }
}
