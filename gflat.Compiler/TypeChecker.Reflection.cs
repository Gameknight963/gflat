using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private TypeExpression? typeQueryOwner;

    public void Visit(TypeofExpression node)
    {
        using var sourceContext = SourceContext.Enter(node.Span);
        using var unevaluated = UnevaluatedOperand();
        bool hasReceiver = TryLookupVariable("this", out _);
        TypeExpression? owner = typeQueryOwner;
        owner ??= (_currentClass?.Name ?? _currentStruct?.Name) is { } name
            ? NamedTypeExpression.Resolved(name, node.Line) : null;
        bool hypotheticalReceiver = !hasReceiver && owner != null;
        if (hypotheticalReceiver)
        {
            // Declaration queries need only the receiver's type. Keep this
            // binding inside the unevaluated operand's temporary scope.
            PushScope();
            DeclareVariable("this", new PointerTypeExpression(owner!, false, node.Line), node.Line);
        }
        try
        {
            node.Operand.Accept(this);
            RecordType(node, ReflectionType(node.Line));
            _constValues[node] = ReflectExpressionType(node.Operand, node.Line);
        }
        finally
        {
            if (hypotheticalReceiver) PopScope();
        }
    }

    private TypeExpression? DeclarationTypeQueryOwner(AstNode declaration, string? owner)
    {
        if (owner != null) return NamedTypeExpression.Resolved(owner, declaration.Line);
        if (declaration is InterfaceDeclaration iface)
            return NamedTypeExpression.Resolved(iface.Name, declaration.Line);
        if (declaration is not MethodDeclaration method) return null;
        if (method.ExtensionReceiverType != null) return method.ExtensionReceiverType;
        if (primitiveMethodSources.TryGetValue(method, out var source)) method = source;
        foreach (var implementation in primitiveImplementationSources)
            if (implementation.Declaration.Members.Contains(method)) return implementation.Declaration.Target;
        foreach (var contract in _interfaces.Values)
            if (contract.Methods.Contains(method)) return NamedTypeExpression.Resolved(contract.Name, declaration.Line);
        return null;
    }

    public ConstValue.Reflection ReflectExpressionType(AstNode operand, int line)
    {
        // Constant storage is protected implicitly. The query reports the
        // declared type, retaining any qualifiers explicitly present there.
        var type = GetConstantDeclaration(operand) switch {
            VariableDeclaration variable => variable.Type,
            FieldDeclaration field => field.Type,
            _ => GetType(operand)
        };
        return ReflectType(type, line);
    }
}
