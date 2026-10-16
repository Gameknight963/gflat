using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private bool HasIncompleteArrayValueShape(TypeExpression type)
    {
        type = ResolveAlias(type);
        return type switch {
            ReferenceTypeExpression reference => HasIncompleteArrayValueShape(reference.Inner),
            ArrayTypeExpression array => array.Size == null || HasIncompleteArrayValueShape(array.ElementType),
            _ => false
        };
    }

    private void ValidateArrayValueShape(TypeExpression type, int line)
    {
        type = ResolveAlias(type);
        if (type is ReferenceTypeExpression reference) type = ResolveAlias(reference.Inner);
        if (type is not ArrayTypeExpression array) return;
        if (array.Size == null)
            throw new TypeCheckException("Array values require a fixed size in every dimension; use an explicit pointer for a buffer of unknown length", line);
        ValidateArrayValueShape(array.ElementType, line);
    }

    private void ValidateReturnType(TypeExpression type, int line)
    {
        ValidateTypeUsage(type, line);
        ValidateArrayValueShape(type, line);
    }

    private TypeExpression InferArrayStorageType(TypeExpression type, AstNode initializer, TypeExpression? initializerType = null)
    {
        if (ResolveAlias(type) is not ArrayTypeExpression { Size: null } array) return type;
        int? size = initializer is LiteralExpression { Token.Kind: TokenKind.StringLiteral } text &&
            TypesMatch(ResolveAlias(array.ElementType), Char)
                ? GetStringLiteralLength(text.Token.Text[1..^1], text.Line)
                : initializer is ArrayLiteralExpression literal ? literal.Elements.Count
                : initializerType != null ? (ResolveAlias(initializerType) as ArrayTypeExpression)?.Size : null;
        return size is { } bound
            ? new ArrayTypeExpression(array.ElementType, bound, type.Line).WithReadOnlyValue(type.IsReadOnlyValue)
            : type;
    }

    private void PrepareArrayFieldShape(FieldDeclaration field)
    {
        TypeExpression type = ResolveAlias(field.Type);
        if (field.Initializer != null) type = InferArrayStorageType(type, field.Initializer);
        ValidateArrayValueShape(type, field.Line);
        if (type is not ArrayTypeExpression) return;
        field.Type = type;
        if (_currentClass != null) UpdateInferredClassField(_currentClass.Name, field, type);
        if (_currentStruct != null)
        {
            int index = _currentStruct.FieldIndex(field.Name);
            if (index >= 0) _currentStruct.Fields[index] = (field.Name, type);
        }
    }

    private void UpdateInferredClassField(string owner, FieldDeclaration field, TypeExpression type)
    {
        // Inherited field lists are copied during symbol collection, before initializers
        // are checked. Keep those copies in sync when an initializer supplies a bound.
        foreach (var cls in _classes.Values)
            for (int i = 0; i < cls.Fields.Count; i++)
                if (cls.Fields[i].Name == field.Name && cls.Fields[i].DeclaringClass == owner)
                    cls.Fields[i] = (field.Name, type, field.Accessibility, owner);
    }
}
