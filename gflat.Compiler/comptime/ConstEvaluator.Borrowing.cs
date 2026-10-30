using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private bool TryBorrowScalar(UnaryExpression expression)
    {
        if (expression.Operator != TokenKind.Ampersand ||
            NumericType(_context.GetType(expression.Operand)) is not NamedTypeExpression named ||
            NumericSemantics.IntegerType(named.Name) == null && named.Name is not ("float" or "double" or "bool" or "char"))
            return false;

        AstNode operand = expression.Operand;
        AssignmentStorage? storage = null;
        if (operand is IdentifierExpression identifier && HasLocalScalarStorage(identifier.Name) ||
            operand is UnaryExpression { Operator: TokenKind.Star } or IndexExpression ||
            operand is MemberAccessExpression && !_context.TryGetConstValue(operand, out _))
            storage = CaptureAssignmentStorage(operand);

        if (storage == null)
        {
            // Literal/temporary receivers have one cell for this evaluation.
            // Evaluate once, before evaluating the remaining call arguments.
            operand.Accept(this);
            ConstValue value = _currentValue!;
            storage = new(() => value, replacement => value = Normalize(replacement, _context.GetType(operand), operand.Line));
        }
        _currentValue = new ConstValue.BorrowedReference(storage.Read, storage.Write);
        return true;
    }

    private bool HasLocalScalarStorage(string name)
    {
        if (_scopes.Any(scope => scope.ContainsKey(name))) return true;
        if (!TryGetVariable("this", out ConstValue? receiver)) return false;
        return receiver switch
        {
            ConstValue.Struct aggregate => aggregate.Fields.ContainsKey(name),
            ConstValue.ClassInstance aggregate => aggregate.Fields.ContainsKey(name),
            _ => false
        };
    }

    private static bool ContainsTransientAddress(ConstValue value, HashSet<ConstValue>? visited = null)
    {
        if (value is ConstValue.BorrowedReference) return true;
        visited ??= new(ReferenceEqualityComparer.Instance);
        if (!visited.Add(value)) return false;
        return value switch
        {
            ConstValue.Struct aggregate => aggregate.Fields.Values.Any(field => ContainsTransientAddress(field, visited)),
            ConstValue.ClassInstance aggregate => aggregate.Fields.Values.Any(field => ContainsTransientAddress(field, visited)),
            ConstValue.Array array => array.Elements.Any(element => ContainsTransientAddress(element, visited)),
            ConstValue.Pointer pointer => ContainsTransientAddress(pointer.Target, visited),
            ConstValue.ArrayPointer pointer => ContainsTransientAddress(pointer.Target, visited),
            _ => false
        };
    }
}
