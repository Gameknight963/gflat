using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private bool TryBindConstantStorageAddress(UnaryExpression expression)
    {
        if (_constValues.GetValueOrDefault(expression) is ConstValue.StaticAddress) return true;
        if (expression.Operand is NamespaceAccessExpression access && StaticOwnerType(access.Left) is { } ownerType)
        {
            bool bound = false;
            ResolveAlias(ownerType, published =>
            {
                if (published is not NamedTypeExpression owner) return;
                var cls = GetClassByIdentity(owner.Name);
                var str = GetStructByIdentity(owner.Name);
                var field = cls?.FieldDeclarationsByName.GetValueOrDefault(access.Member) ??
                    str?.FieldDeclarationsByName.GetValueOrDefault(access.Member);
                if (field is not { IsConst: true }) return;

                // A generic owner publishes its declaration before checking its
                // initializers. Its address can already close a constant graph.
                staticStorage.TryAdd(field, new(field, owner.Name, cls?.Namespace ?? str?.Namespace ?? "",
                    "gflat$storage$" + owner.Name + "$" + field.Name));
                bound = BindConstantStorageAddress(expression);
            });
            if (bound) return true;
        }
        return BindConstantStorageAddress(expression);
    }

    private bool BindConstantStorageAddress(UnaryExpression expression)
    {
        if (!TryBindStaticStorage(expression.Operand, evaluateConstant: false) ||
            GetStaticStorage(expression.Operand) is not { IsConst: true } field)
            return false;

        TypeExpression storageType = GetType(expression.Operand);
        ValidateValueType(storageType, expression.Line);
        RecordType(expression, new PointerTypeExpression(storageType, false, expression.Line, isReadOnly: true));
        _constValues[expression] = new ConstValue.StaticAddress(field);
        return true;
    }

    public ConstValue ReadConstantStorage(FieldDeclaration declaration)
    {
        if (EvaluateDefaultConstant(declaration, out ConstValue? value, out string? failureReason) && value != null) return value;
        throw new ConstEvalException(failureReason ?? $"Cannot evaluate constant '{declaration.Name}'", declaration.Line);
    }
}
