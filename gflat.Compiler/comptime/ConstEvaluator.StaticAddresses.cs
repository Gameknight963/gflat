using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private bool TryEvaluateStaticAddress(UnaryExpression expression)
    {
        if (expression.Operator != TokenKind.Ampersand) return false;
        if (_context.GetConstantDeclaration(expression.Operand) is FieldDeclaration { IsConst: true } declaration)
        {
            _currentValue = new ConstValue.StaticAddress(declaration);
            return true;
        }
        if (expression.Operand is UnaryExpression { Operator: TokenKind.Star } dereference)
        {
            dereference.Operand.Accept(this);
            if (_currentValue is ConstValue.StaticAddress or ConstValue.Pointer or ConstValue.ArrayPointer or ConstValue.String or ConstValue.BorrowedReference)
                return true;
            throw new ConstEvalException("Cannot take the address of this dereferenced constant", expression.Line);
        }
        return false;
    }

    private ConstValue ReadStaticAddress(ConstValue.StaticAddress address, int line, TypeExpression? accessType = null)
    {
        TypeExpression storedType = _context.ResolveAlias(address.Declaration.Type);
        if (address.ElementIndex != null && storedType is ArrayTypeExpression storageArray)
            storedType = storageArray.ElementType;
        if (accessType != null && !MatchesStaticStorageView(storedType, accessType))
            throw new ConstEvalException("Reading a reinterpreted constant address requires runtime evaluation", line);
        ConstValue value = _context.ReadConstantStorage(address.Declaration);
        if (address.ElementIndex is not { } index) return value;
        if (value is not ConstValue.Array array || index < 0 || index >= array.Elements.Count)
            throw new ConstEvalException("Array address is outside readable constant storage", line);
        return ReadInitializedElement(array, index, line);
    }

    private ConstValue DecayConstantArray(AstNode expression, ConstValue value, TypeExpression target)
    {
        if (value is not ConstValue.Array array || _context.ResolveAlias(target) is not PointerTypeExpression ||
            _context.ResolveAlias(_context.GetType(expression)) is not ArrayTypeExpression source)
            return value;
        if (_context.GetConstantDeclaration(expression) is FieldDeclaration { IsConst: true } declaration)
            return new ConstValue.StaticAddress(declaration, 0);
        return new ConstValue.ArrayPointer(source.ElementType, array);
    }

    private ConstValue IndexStaticAddress(ConstValue.StaticAddress address, ConstValue index, TypeExpression accessType, int line)
    {
        if (!ConstValue.TryIntegerValue(index, out var offset))
            throw new ConstEvalException("Pointer index must be an integer", line);
        if (address.ElementIndex is { } first)
        {
            var absolute = first + offset;
            if (absolute < 0 || absolute > int.MaxValue)
                throw new ConstEvalException("Array address is outside readable constant storage", line);
            return ReadStaticAddress(address with { ElementIndex = (int)absolute }, line, accessType);
        }
        if (offset != 0)
            throw new ConstEvalException("Pointer index is outside readable constant storage", line);
        return ReadStaticAddress(address, line, accessType);
    }

    private bool MatchesStaticStorageView(TypeExpression storage, TypeExpression view)
    {
        storage = _context.ResolveAlias(storage);
        view = _context.ResolveAlias(view);
        return (storage, view) switch
        {
            (NamedTypeExpression a, NamedTypeExpression b) => a.Name == b.Name || IsBaseStorageView(a.Name, b.Name),
            (PointerTypeExpression a, PointerTypeExpression b) => MatchesStaticStorageView(a.Inner, b.Inner),
            (ReferenceTypeExpression a, ReferenceTypeExpression b) => MatchesStaticStorageView(a.Inner, b.Inner),
            (ArrayTypeExpression a, ArrayTypeExpression b) => a.Size == b.Size && MatchesStaticStorageView(a.ElementType, b.ElementType),
            (ManagedTypeExpression a, ManagedTypeExpression b) => MatchesStaticStorageView(a.Inner, b.Inner) &&
                (a.Collector == null && b.Collector == null || a.Collector != null && b.Collector != null && MatchesStaticStorageView(a.Collector, b.Collector)),
            (FunctionPointerTypeExpression a, FunctionPointerTypeExpression b) =>
                a.IsSignature == b.IsSignature && a.IsManaged == b.IsManaged &&
                MatchesStaticStorageView(a.ReturnType, b.ReturnType) && a.ParameterTypes.Count == b.ParameterTypes.Count &&
                a.ParameterTypes.Zip(b.ParameterTypes).All(pair => MatchesStaticStorageView(pair.First, pair.Second)),
            _ => false
        };
    }

    private bool IsBaseStorageView(string storage, string view)
    {
        var seen = new HashSet<string>();
        for (string? owner = _context.GetClass(storage)?.BaseClass; owner != null && seen.Add(owner); owner = _context.GetClass(owner)?.BaseClass)
            if (owner == view) return true;
        return false;
    }
}
