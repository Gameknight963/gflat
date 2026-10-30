using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private sealed record AssignmentStorage(Func<ConstValue> Read, Action<ConstValue> Write);

    private AssignmentStorage CaptureAssignmentStorage(AstNode target)
    {
        if (_context.GetPropertyRead(target) != null || _context.GetPropertyWrite(target) != null)
            throw new ConstEvalException("Property access is not yet supported in constant evaluation", target.Line);
        if (target is IdentifierExpression identifier)
        {
            foreach (var scope in _scopes)
                if (scope.ContainsKey(identifier.Name))
                    return new(() => scope[identifier.Name], value => scope[identifier.Name] = OverwriteValueStorage(scope[identifier.Name], value, _context.GetType(target)));

            if (TryGetVariable("this", out ConstValue? receiver))
            {
                var fields = receiver switch
                {
                    ConstValue.Struct value => value.Fields,
                    ConstValue.ClassInstance value => value.Fields,
                    _ => null
                };
                bool exists = fields?.ContainsKey(identifier.Name) == true || receiver switch
                {
                    ConstValue.Struct value => _context.GetStruct(value.StructName)?.FieldIndex(identifier.Name) >= 0,
                    ConstValue.ClassInstance value => _context.GetClass(value.ClassName)?.FieldIndex(identifier.Name) >= 0,
                    _ => false
                };
                if (fields != null && exists)
                    return new(() => fields[identifier.Name], value => fields[identifier.Name] = OverwriteValueStorage(fields.GetValueOrDefault(identifier.Name), value, _context.GetType(target)));
            }
            throw new ConstEvalException($"Cannot modify non-local variable '{identifier.Name}' at compile time", target.Line);
        }

        if (target is MemberAccessExpression member)
        {
            Func<ConstValue> receiver;
            if (_context.ResolveAlias(_context.GetType(member.Object)) is NamedTypeExpression &&
                member.Object is IdentifierExpression or MemberAccessExpression)
            {
                // A field of inline storage remains at the same address when the
                // containing value is overwritten by the right-hand operand.
                var storage = CaptureAssignmentStorage(member.Object);
                receiver = storage.Read;
            }
            else
            {
                // Pointer values and temporary receivers are captured once.
                member.Object.Accept(this);
                ConstValue captured = _currentValue!;
                receiver = () => captured;
            }

            Dictionary<string, ConstValue> Fields()
            {
                ConstValue value = receiver();
                if (value is ConstValue.BorrowedReference borrowed) value = borrowed.Read();
                if (value is ConstValue.Pointer pointer) value = pointer.Target;
                return value switch
                {
                    ConstValue.Struct aggregate => aggregate.Fields,
                    ConstValue.ClassInstance aggregate => aggregate.Fields,
                    _ => throw new ConstEvalException("Invalid assignment target at compile time", target.Line)
                };
            }
            // Resolve the containing storage before evaluating the right operand.
            _ = Fields();
            return new(() => Fields()[member.Member], value => {
                var fields = Fields();
                fields[member.Member] = OverwriteValueStorage(fields.GetValueOrDefault(member.Member), value, _context.GetType(target));
            });
        }
        if (target is UnaryExpression { Operator: TokenKind.Star } dereference)
        {
            dereference.Operand.Accept(this);
            if (_currentValue is ConstValue.BorrowedReference borrowed)
                return new(borrowed.Read, borrowed.Write);
            if (_currentValue is ConstValue.Pointer pointer)
                return new(() => pointer.Target, value => OverwriteValueStorage(pointer.Target, value, _context.GetType(target)));
            throw new ConstEvalException("Cannot assign through this pointer at compile time", target.Line);
        }
        if (target is IndexExpression index)
        {
            index.Target.Accept(this);
            ConstValue array = _currentValue!;
            if (array is ConstValue.ArrayPointer pointer) array = pointer.Target;
            index.Index.Accept(this);
            if (array is not ConstValue.Array elements ||
                !ConstValue.TryIntegerValue(_currentValue!, out var position) || position < 0 || position >= elements.Elements.Count)
                throw new ConstEvalException("Array assignment index is outside constant storage", target.Line);
            int offset = (int)position;
            return new(() => elements.Elements[offset], value => elements.Elements[offset] =
                OverwriteValueStorage(elements.Elements[offset], value, _context.GetType(target)));
        }
        throw new ConstEvalException("Invalid assignment target at compile time", target.Line);
    }
}
