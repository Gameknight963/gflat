using System;
using System.Collections.Generic;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat.semantics
{
    public class ControlFlowPass
    {
        private readonly TypeChecker _typeChecker;
        private readonly ConstEvaluator _constEvaluator;

        public ControlFlowPass(TypeChecker typeChecker, ConstEvaluator constEvaluator)
        {
            _typeChecker = typeChecker;
            _constEvaluator = constEvaluator;
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
            if (member is MethodDeclaration method)
            {
                CheckMethod(method);
            }
            else if (member is ClassDeclaration classDecl)
            {
                for (int i = 0; i < classDecl.Members.Count; i++)
                {
                    CheckMember(classDecl.Members[i]);
                }
            }
            else if (member is StructDeclaration structDecl)
            {
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
            if (method.Body == null || method.IsAbstract)
            {
                return;
            }

            TypeExpression resolvedRet = _typeChecker.ResolveAlias(method.ReturnType);
            if (IsVoidType(resolvedRet))
            {
                return;
            }

            if (!StatementTerminates(method.Body))
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

            TypeExpression resolvedRet = _typeChecker.ResolveAlias(op.ReturnType);
            if (IsVoidType(resolvedRet))
            {
                return;
            }

            if (!StatementTerminates(op.Body))
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
                    if (StatementTerminates(block.Statements[i]))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (stmt is IfStatement ifStmt)
            {
                if (IsConstantTrue(ifStmt.Condition))
                {
                    return StatementTerminates(ifStmt.Then);
                }

                if (IsConstantFalse(ifStmt.Condition))
                {
                    if (ifStmt.Else != null)
                    {
                        return StatementTerminates(ifStmt.Else);
                    }
                    return false;
                }

                if (ifStmt.Else == null)
                {
                    return false;
                }

                return StatementTerminates(ifStmt.Then) && StatementTerminates(ifStmt.Else);
            }

            if (stmt is WhileStatement whileStmt)
            {
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
                if (forStmt.Condition == null || IsConstantTrue(forStmt.Condition))
                {
                    if (!HasBreakTargetingCurrentLoop(forStmt.Body))
                    {
                        return true;
                    }
                }
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
