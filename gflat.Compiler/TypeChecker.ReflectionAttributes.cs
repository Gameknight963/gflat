using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<AstNode> queryingAttributes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FieldDeclaration> generatedFieldAttributes = new(ReferenceEqualityComparer.Instance);

    private void AddGeneratedFieldAttribute(AstNode declaration)
    {
        if (declaration is not FieldDeclaration { PropertyName: not null } field || generatedFieldAttributes.Contains(field)) return;
        if (!_globalScope.Classes.TryGetValue("CompilerGeneratedAttribute", out var type) || !IsSubclassOf(type.Name, "Attribute")) return;
        generatedFieldAttributes.Add(field);
        field.Attributes.Add(new AttributeNode("global::CompilerGeneratedAttribute", [], field.Line) { Span = field.Span });
    }

    private AstNode? ReflectedAttributeDeclaration(ConstValue.Reflection target)
    {
        if (target.Declaration != null) return target.Declaration;
        if (target.Kind != "Type" || target.Type == null || ResolveAlias(target.Type) is not NamedTypeExpression type) return null;
        return GetStructByIdentity(type.Name)?.Declaration ?? (AstNode?)GetClassByIdentity(type.Name)?.Declaration
            ?? ResolveInterface(type)?.Declaration ?? (AstNode?)ResolveEnum(type)?.Declaration;
    }

    private bool TryReflectAttributeMember(ConstValue.Reflection target, string member, int line, out ConstValue? result)
    {
        result = null;
        if (target.Kind == "Attribute" && member == "Value" && target.Value != null && target.Type != null)
        {
            result = new ConstValue.Reflection(target.Identity + ":value", "Value", target.Name, target.Type, Value: target.Value);
            return true;
        }
        if (member != "Attributes") return false;
        if (target.Overloads is { Count: > 1 })
            throw new TypeCheckException("Select a single overload before querying its attributes", line);
        var declaration = ReflectedAttributeDeclaration(target);
        if (declaration == null) { result = new ConstValue.Array([]); return true; }
        if (!queryingAttributes.Add(declaration)) throw new TypeCheckException("Circular attribute reflection", declaration.Span);
        try
        {
            AddGeneratedFieldAttribute(declaration);
            using var context = EnterSignatureContext(declaration,
                target.DeclaringType is NamedTypeExpression owner ? owner.Name : null,
                target.DeclaringType is NamedTypeExpression classOwner && GetClassByIdentity(classOwner.Name) != null);
            var attributes = new List<ConstValue>();
            foreach (var attribute in declaration.Attributes)
            {
                attribute.Accept(this);
                var value = _evaluatedAttributes[attribute];
                attributes.Add(new ConstValue.Reflection(target.Identity + ":attribute:" + attributes.Count, "Attribute", value.Type.Name,
                    NamedTypeExpression.Resolved(value.Type.Name, line), attribute, Value: value.Value));
            }
            result = new ConstValue.Array(attributes);
            return true;
        }
        finally { queryingAttributes.Remove(declaration); }
    }
}
