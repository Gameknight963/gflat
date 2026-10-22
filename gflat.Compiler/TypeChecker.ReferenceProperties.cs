using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<ReturnStatement, AstNode> _referenceReturns = new();
    public AstNode? GetReferenceReturn(ReturnStatement node) => _referenceReturns.GetValueOrDefault(node);
    private readonly HashSet<AstNode> _referenceReads = new();
    public bool IsReferenceRead(AstNode node) => _referenceReads.Contains(node);

    private void ExpandReferenceProperty(List<AstNode> members, AstNode owner, PropertyDeclaration property)
    {
        var getter = property.Getter;
        if (getter == null || property.Setter != null || property.Initializer != null || getter.IsStatic)
            throw new TypeCheckException("A ref property has an instance getter and no setter", property.Line);
        if (getter.Body == null && owner is not InterfaceDeclaration && !getter.IsAbstract)
            throw new TypeCheckException("A ref property requires a getter body", property.Line);
        var clones = new AstCloner(new());
        bool explicitShallow = getter.IsReadOnly && !getter.IsImmutable;
        foreach (int protection in getter.IsImmutable ? new[] { 0, 1, 2 } : new[] { 0, 1 })
        {
            bool readOnly = protection != 0;
            bool immutable = protection == 2;
            // An explicit shallow getter promises not to mutate the owner; it may
            // still borrow mutable storage reached through a pointer field.
            TypeExpression result = immutable ? TypeQualifiers.Immutable(property.Type) : readOnly && !explicitShallow ? TypeQualifiers.ReadOnly(property.Type) : property.Type;
            var method = new MethodDeclaration((immutable ? "$get$immutable$" : readOnly ? "$get$readonly$" : "$get$") + property.Name,
                new PointerTypeExpression(result, false, property.Line),
                getter.Parameters, getter.Body == null ? null : (BlockStatement)clones.CloneNode(getter.Body),
                owner is InterfaceDeclaration ? TokenKind.Public : getter.Accessibility,
                false, getter.IsVirtual, getter.IsOverride, getter.IsAbstract || owner is InterfaceDeclaration,
                getter.Line, readOnly, isNoThrow: getter.IsNoThrow, isImmutable: immutable)
            { IsInline = getter.IsInline, IsRefReturn = true, PropertyName = property.Name, Span = getter.Span };
            members.Insert(members.IndexOf(property) + 1, method);
        }
        property.Expanded = true;
    }
}
