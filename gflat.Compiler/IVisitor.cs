using gflat.ast;

namespace gflat
{
    public interface IVisitor
    {
        void Visit(CompilationUnit node);
        void Visit(UsingDirective node);
        void Visit(NamespaceDeclaration node);
        void Visit(ClassDeclaration node);
        void Visit(StructDeclaration node);
        void Visit(InterfaceDeclaration node);
        void Visit(FieldDeclaration node);
        void Visit(PropertyDeclaration node) { }
        void Visit(MethodDeclaration node);
        void Visit(Parameter node);
        void Visit(BlockStatement node);
        void Visit(ReturnStatement node);
        void Visit(IfStatement node);
        void Visit(WhileStatement node);
        void Visit(ForStatement node);
        void Visit(ForeachStatement node);
        void Visit(VariableDeclaration node);
        void Visit(DeferStatement node);
        void Visit(ExpressionStatement node);
        void Visit(BinaryExpression node);
        void Visit(UnaryExpression node);
        void Visit(LiteralExpression node);
        void Visit(IdentifierExpression node);
        void Visit(CallExpression node);
        void Visit(MemberAccessExpression node);
        void Visit(AssignmentExpression node);
        void Visit(InterpolatedStringExpression node);
        void Visit(NamedTypeExpression node);
        void Visit(NestedTypeExpression node);
        void Visit(PointerTypeExpression node);
        void Visit(ManagedTypeExpression node);
        void Visit(ArrayTypeExpression node);
        void Visit(IndexExpression node);
        void Visit(ArrayLiteralExpression node);
        void Visit(BreakStatement node);
        void Visit(ContinueStatement node);
        void Visit(NewExpression node);
        void Visit(DeleteStatement node);
        void Visit(ConstructorDeclaration node);
        void Visit(DestructorDeclaration node);
        void Visit(NamespaceAccessExpression node);
        void Visit(AttributeNode node);
        void Visit(ExternDeclaration node);
        void Visit(GlobalExpression node);
        void Visit(FunctionPointerTypeExpression node);
        void Visit(AliasDeclaration node);
        void Visit(EnumDeclaration node);
        void Visit(EnumMemberDeclaration node);
        void Visit(CastExpression node);
        void Visit(LambdaExpression node);
        void Visit(OperatorDeclaration node);
        void Visit(DefaultExpression node);
        void Visit(SizeofExpression node);
        void Visit(NameofExpression node);
        void Visit(PrefixedStringLiteralExpression node);
        void Visit(ThrowStatement node);
        void Visit(TryStatement node);
        void Visit(CatchClause node);
    }
}
