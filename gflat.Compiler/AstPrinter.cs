using gflat.ast;
using System;
using System.Collections.Generic;
using System.Text;

namespace gflat
{
    public class AstPrinter : IVisitor
    {
        public void Visit(ArrayLiteralExpression node)
        {
            Print("ArrayLiteral");
            Indented(() => { foreach (var element in node.Elements) element.Accept(this); });
        }
        private int _indent = 0;
        private string Indent => new string(' ', _indent * 2);

        private void Print(string text) => Console.WriteLine($"{Indent}{text}");

        private void Indented(Action action)
        {
            _indent++;
            action();
            _indent--;
        }

        public void Visit(CompilationUnit node)
        {
            Print("CompilationUnit");
            Indented(() =>
            {
                foreach (UsingDirective u in node.Usings) u.Accept(this);
                foreach (AstNode m in node.Members) m.Accept(this);
                foreach (NamespaceDeclaration n in node.Namespaces) n.Accept(this);
            });
        }

        public void Visit(UsingDirective node) => Print($"Using: {node.Name}");

        public void Visit(NamespaceDeclaration node)
        {
            Print($"Namespace: {node.Name}");
            Indented(() => { foreach (AstNode m in node.Members) m.Accept(this); });
        }

        public void Visit(ClassDeclaration node)
        {
            Print($"Class: {node.Accessibility} {(node.IsAbstract ? "abstract " : "")}{node.Name}");
            Indented(() => { foreach (AstNode m in node.Members) m.Accept(this); });
        }

        public void Visit(StructDeclaration node)
        {
            Print($"Struct: {node.Accessibility} {node.Name}");
            Indented(() => { foreach (AstNode m in node.Members) m.Accept(this); });
        }

        public void Visit(InterfaceDeclaration node)
        {
            Print($"Interface: {node.Accessibility} {node.Name}");
            Indented(() => { foreach (AstNode m in node.Members) m.Accept(this); });
        }

        public void Visit(FieldDeclaration node)
        {
            Print($"Field: {node.Accessibility} {node.Name}");
            Indented(() =>
            {
                node.Type.Accept(this);
                if (node.Initializer != null) node.Initializer.Accept(this);
            });
        }

        public void Visit(MethodDeclaration node)
        {
            Print($"Method: {node.Accessibility} {(node.IsConst ? "const " : "")}{(node.IsReadOnly ? "readonly " : "")}{(node.IsStatic ? "static " : "")}{(node.IsVirtual ? "virtual " : "")}{(node.IsOverride ? "override " : "")}{(node.IsAbstract ? "abstract " : "")}{(node.Throws ? "throws " : "")}{node.Name}");
            Indented(() =>
            {
                Print("ReturnType:");
                Indented(() => node.ReturnType.Accept(this));
                Print("Parameters:");
                Indented(() => { foreach (Parameter p in node.Parameters) p.Accept(this); });
                if (node.Body != null)
                {
                    Print("Body:");
                    Indented(() => node.Body.Accept(this));
                }
            });
        }

        public void Visit(Parameter node)
        {
            Print($"Parameter: {(node.IsConst ? "const " : "")}{node.Name}");
            Indented(() => node.Type.Accept(this));
        }

        public void Visit(BlockStatement node)
        {
            Print("Block");
            Indented(() => { foreach (AstNode s in node.Statements) s.Accept(this); });
        }

        public void Visit(ReturnStatement node)
        {
            Print("Return");
            Indented(() => node.Value?.Accept(this));
        }

        public void Visit(IfStatement node)
        {
            Print("If");
            Indented(() =>
            {
                Print("Condition:"); Indented(() => node.Condition.Accept(this));
                Print("Then:"); Indented(() => node.Then.Accept(this));
                if (node.Else != null) { Print("Else:"); Indented(() => node.Else.Accept(this)); }
            });
        }

        public void Visit(WhileStatement node)
        {
            Print("While");
            Indented(() =>
            {
                Print("Condition:"); Indented(() => node.Condition.Accept(this));
                Print("Body:"); Indented(() => node.Body.Accept(this));
            });
        }

        public void Visit(ForStatement node)
        {
            Print("For");
            Indented(() =>
            {
                if (node.Initializer != null) { Print("Init:"); Indented(() => node.Initializer.Accept(this)); }
                if (node.Condition != null) { Print("Condition:"); Indented(() => node.Condition.Accept(this)); }
                if (node.Increment != null) { Print("Increment:"); Indented(() => node.Increment.Accept(this)); }
                Print("Body:"); Indented(() => node.Body.Accept(this));
            });
        }

        public void Visit(ForeachStatement node)
        {
            Print($"Foreach: {node.VariableName}");
            Indented(() =>
            {
                Print("Type:");
                Indented(() => node.ElementType.Accept(this));
                Print("Collection:");
                Indented(() => node.Collection.Accept(this));
                Print("Body:");
                Indented(() => node.Body.Accept(this));
            });
        }

        public void Visit(VariableDeclaration node)
        {
            Print($"Variable: {(node.IsConst ? "const " : "")}{node.Name}");
            Indented(() =>
            {
                node.Type.Accept(this);
                node.Initializer?.Accept(this);
            });
        }

        public void Visit(DeferStatement node)
        {
            Print("Defer");
            Indented(() => node.Statement.Accept(this));
        }

        public void Visit(DeleteStatement node)
        {
            Print("Delete");
            Indented(() => node.Target.Accept(this));
        }

        public void Visit(ExpressionStatement node)
        {
            Print("ExpressionStatement");
            Indented(() => node.Expression.Accept(this));
        }

        public void Visit(BinaryExpression node)
        {
            Print($"Binary: {node.Operator}");
            Indented(() => { node.Left.Accept(this); node.Right.Accept(this); });
        }

        public void Visit(UnaryExpression node)
        {
            Print($"Unary: {(node.IsPrefix ? "prefix" : "postfix")} {node.Operator}");
            Indented(() => node.Operand.Accept(this));
        }

        public void Visit(LiteralExpression node) => Print($"Literal: {node.Token.Kind} '{node.Token.Text}'");

        public void Visit(IdentifierExpression node) => Print($"Identifier: {node.Name}");

        public void Visit(CallExpression node)
        {
            Print("Call");
            Indented(() =>
            {
                Print("Callee:"); Indented(() => node.Callee.Accept(this));
                Print("Args:");
                Indented(() => { foreach (AstNode a in node.Arguments) a.Accept(this); });
            });
        }

        public void Visit(MemberAccessExpression node)
        {
            Print($"MemberAccess: {(node.IsArrow ? "->" : ".")}{node.Member}");
            Indented(() => node.Object.Accept(this));
        }

        public void Visit(AssignmentExpression node)
        {
            Print($"Assignment: {node.Operator}");
            Indented(() => { node.Target.Accept(this); node.Value.Accept(this); });
        }

        public void Visit(InterpolatedStringExpression node)
        {
            Print("InterpolatedString");
            Indented(() => { foreach (AstNode p in node.Parts) p.Accept(this); });
        }

        public void Visit(NewExpression node)
        {
            Print($"New ({node.Kind})");
            Indented(() =>
            {
                node.Type.Accept(this);
                foreach (AstNode a in node.Arguments) a.Accept(this);
            });
        }

        public void Visit(ConstructorDeclaration node)
        {
            Print($"Constructor: {node.Accessibility} {node.Name}");
            Indented(() =>
            {
                Print("Parameters:");
                Indented(() => { foreach (Parameter p in node.Parameters) p.Accept(this); });
                Print("Body:");
                Indented(() => node.Body.Accept(this));
            });
        }

        public void Visit(NamespaceAccessExpression node)
        {
            Print($"NamespaceAccess: ::{node.Member}");
            Indented(() => node.Left.Accept(this));
        }

        public void Visit(NamedTypeExpression node) => Print($"Type: {(node.Namespace != null ? node.Namespace + "::" : "")}{node.Name}");
        public void Visit(NestedTypeExpression node)
        {
            Print($"NestedType: {node.Member}");
            Indented(() => node.Parent.Accept(this));
        }
        public void Visit(PointerTypeExpression node) { Print($"PointerType{(node.IsReadOnly ? " readonly" : "")}{(node.IsNullable ? "?" : "")}"); Indented(() => node.Inner.Accept(this)); }
        public void Visit(ManagedTypeExpression node) { Print($"ManagedType{(node.IsReadOnly ? " readonly" : "")}{(node.IsNullable ? "?" : "")}"); Indented(() => node.Inner.Accept(this)); }
        public void Visit(ArrayTypeExpression node) { Print($"ArrayType{(node.Size.HasValue ? $"[{node.Size}]" : "[]")}"); Indented(() => node.ElementType.Accept(this)); }
        public void Visit(IndexExpression node)
        {
            Print("IndexExpression");
            Indented(() =>
            {
                node.Target.Accept(this);
                node.Index.Accept(this);
            });
        }
        public void Visit(BreakStatement node) => Print("Break");
        public void Visit(ContinueStatement node) => Print("Continue");

        public void Visit(AttributeNode node)
        {
            Print($"Attribute {node.Name}");
            Indented(() => { foreach (AstNode argument in node.Arguments) argument.Accept(this); });
        }

        public void Visit(ExternDeclaration node) => Print($"ExternDecleration");
        public void Visit(GlobalExpression node) => Print($"GlobalExpression");

        public void Visit(FunctionPointerTypeExpression node)
        {
            Print($"FunctionPointerType{(node.IsManaged ? "^" : "*")}{(node.IsNullable ? "?" : "")}");
            Indented(() =>
            {
                Print("Return:"); Indented(() => node.ReturnType.Accept(this));
                Print("Params:"); Indented(() => { foreach (var p in node.ParameterTypes) p.Accept(this); });
            });
        }

        public void Visit(AliasDeclaration node)
        {
            Print($"Alias: {node.Accessibility} {node.Name}");
            Indented(() => node.TargetType.Accept(this));
        }

        public void Visit(EnumDeclaration node)
        {
            Print($"Enum: {node.Accessibility} {node.Name}");
            Indented(() =>
            {
                if (node.UnderlyingType != null)
                {
                    Print("UnderlyingType:");
                    Indented(() => node.UnderlyingType.Accept(this));
                }
                Print("Members:");
                Indented(() =>
                {
                    foreach (EnumMemberDeclaration m in node.Members)
                        m.Accept(this);
                });
            });
        }

        public void Visit(EnumMemberDeclaration node)
        {
            Print($"EnumMember: {node.Name}");
            if (node.Value != null)
            {
                Indented(() => node.Value.Accept(this));
            }
        }

        public void Visit(CastExpression node)
        {
            Print("CastExpression");
            Indented(() =>
            {
                Print("TargetType:");
                Indented(() => node.TargetType.Accept(this));
                Print("Operand:");
                Indented(() => node.Operand.Accept(this));
            });
        }

        public void Visit(LambdaExpression node)
        {
            Print($"LambdaExpression (static={node.IsStatic}, exprBody={node.IsExpressionBody})");
            Indented(() =>
            {
                Print("Params:");
                Indented(() =>
                {
                    foreach (Parameter p in node.Parameters)
                    {
                        p.Accept(this);
                    }
                });
                Print("Body:");
                Indented(() => node.Body.Accept(this));
            });
        }

        public void Visit(OperatorDeclaration node)
        {
            Print($"OperatorDeclaration: {node.Accessibility} {(node.IsStatic ? "static " : "")}operator {node.OperatorSymbol}");
            Indented(() =>
            {
                Print("Return:");
                Indented(() => node.ReturnType.Accept(this));
                Print("Params:");
                Indented(() =>
                {
                    foreach (Parameter p in node.Parameters)
                    {
                        p.Accept(this);
                    }
                });
                Print("Body:");
                Indented(() => node.Body.Accept(this));
            });
        }

        public void Visit(DestructorDeclaration node)
        {
            Print($"Destructor: ~{node.Name}{(node.IsVirtual ? " (virtual)" : "")}");
            Indented(() => node.Body.Accept(this));
        }

        public void Visit(DefaultExpression node)
        {
            Print($"DefaultExpression{(node.TargetType != null ? "" : " (inferred)")}");
            if (node.TargetType != null)
            {
                Indented(() => node.TargetType.Accept(this));
            }
        }

        public void Visit(SizeofExpression node)
        {
            Print("SizeofExpression");
            Indented(() => node.TargetType.Accept(this));
        }

        public void Visit(NameofExpression node)
        {
            Print("NameofExpression");
            Indented(() => node.Target.Accept(this));
        }

        public void Visit(PrefixedStringLiteralExpression node)
        {
            Print($"PrefixedStringLiteralExpression ({node.Prefix})");
            Indented(() =>
            {
                if (node.Scope != null)
                {
                    Print("Scope:");
                    Indented(() => node.Scope.Accept(this));
                }
                Print("Literal:");
                Indented(() => node.Literal.Accept(this));
            });
        }

        public void Visit(ThrowStatement node)
        {
            Print("Throw");
            Indented(() => node.Expression.Accept(this));
        }

        public void Visit(TryStatement node)
        {
            Print("Try");
            Indented(() =>
            {
                Print("Body:");
                Indented(() => node.TryBlock.Accept(this));
                Print("CatchClauses:");
                Indented(() =>
                {
                    foreach (CatchClause c in node.CatchClauses)
                        c.Accept(this);
                });
            });
        }

        public void Visit(CatchClause node)
        {
            Print($"Catch: {(node.ExceptionType != null ? "" : "all")}{(node.VariableName != null ? $" {node.VariableName}" : "")}");
            Indented(() =>
            {
                if (node.ExceptionType != null)
                {
                    Print("Type:");
                    Indented(() => node.ExceptionType.Accept(this));
                }
                Print("Body:");
                Indented(() => node.Body.Accept(this));
            });
        }
    }
}
