using gflat.ast;
using Syntax = gflat.AnalysisSnapshot.Syntax;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private TypeExpression? SourceIndexerElementType(IndexExpression index, TypeExpression? receiver, string path, int offset)
    {
        string? identity = TypeOwner(receiver, path, offset);
        if (identity == null || SourceOwnerType(identity) is not { } owner) return null;
        var members = owner.Syntax.Node switch {
            ClassDeclaration type => type.Members, StructDeclaration type => type.Members,
            InterfaceDeclaration type => type.Members, _ => null
        };
        var properties = members?.OfType<PropertyDeclaration>().Where(candidate => candidate.Name == "$index").ToArray();
        if (properties == null || properties.Length != 1 || properties[0].Getter is not { } getter || getter.IsStatic ||
            getter.Parameters.Count != index.Indices.Count) return null;
        var property = properties[0];
        var syntax = new Syntax(property, property.Span, owner.Syntax);
        var source = new Symbol("$index", 10, "property", syntax, NameSpan(syntax, "this", property.Type),
            owner.Namespace, owner.Qualified, null, property.Type, false,
            owner.Syntax.Node is InterfaceDeclaration ? TokenKind.Public : getter.Accessibility);
        if (!Accessible(source, path, offset)) return null;
        var projected = ProjectSourceMemberType(source, receiver, path, offset);
        // A hidden generic application must not expose an unbound parameter as a caller type name.
        if (ReferenceEquals(projected, source) && Parents(syntax).Any(ancestor => ancestor.Node switch {
            ClassDeclaration type => type.GenericParameters.Count > 0, StructDeclaration type => type.GenericParameters.Count > 0,
            InterfaceDeclaration type => type.GenericParameters.Count > 0, _ => false
        })) return null;
        var result = projected.Type;
        if (result == null) return null;
        if (!property.IsRef) return ReceiverAllowsMember(source, receiver) ? result : null;
        var view = receiver;
        while (view is ReferenceTypeExpression reference) view = reference.Inner;
        view = view switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner, _ => view };
        // Ref accessors emit receiver-specific variants; the original getter alone is not the readonly variant.
        if (view?.IsImmutableValue == true) return getter.IsImmutable ? TypeQualifiers.Immutable(result) : null;
        bool explicitShallow = getter.IsReadOnly && !getter.IsImmutable;
        return view?.IsReadOnlyValue == true && !explicitShallow ? TypeQualifiers.ReadOnly(result) : result;
    }
}
