using gflat.ast;
using gflat.diagnostics;

namespace gflat;

public partial class TypeChecker
{
    // Matching types alone do not establish an operator: aggregates need a declared
    // overload, and booleans support bitwise/logical operations rather than arithmetic.
    private bool CheckBuiltinOperatorOperands(AstNode site, TokenKind operation, TypeExpression left, TypeExpression right)
    {
        left = ResolveAlias(left);
        right = ResolveAlias(right);
        bool Numeric(TypeExpression type) => IsInteger(type) || type is NamedTypeExpression { Name: "float" or "double" };
        bool Address(TypeExpression type) => type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression or NamedTypeExpression { Name: "null" };
        bool InterfaceAddress(TypeExpression type) => (type switch
        {
            PointerTypeExpression pointer => ResolveAlias(pointer.Inner),
            ManagedTypeExpression managed => ResolveAlias(managed.Inner),
            _ => null
        }) is NamedTypeExpression named && IsInterface(named);
        bool numeric = Numeric(left) && Numeric(right);
        bool integers = IsInteger(left) && IsInteger(right);
        bool booleans = TypesMatch(left, Bool) && TypesMatch(right, Bool);
        // Preserve the existing, more specific diagnostics for these address kinds.
        if (operation is TokenKind.Plus or TokenKind.Minus &&
            (left is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression ||
             right is PointerTypeExpression or FunctionPointerTypeExpression or ManagedTypeExpression)) return true;
        bool valid = operation switch
        {
            TokenKind.Plus => numeric || left is PointerTypeExpression && IsInteger(right) || IsInteger(left) && right is PointerTypeExpression,
            TokenKind.Minus => numeric || left is PointerTypeExpression && (IsInteger(right) || right is PointerTypeExpression),
            TokenKind.Star or TokenKind.Slash or TokenKind.Percent => numeric,
            TokenKind.Ampersand or TokenKind.Pipe or TokenKind.Caret => integers || booleans,
            TokenKind.EqualsEquals or TokenKind.NotEquals or TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals
                => numeric || booleans || Address(left) && Address(right),
            _ => true // Logical and shift operators retain their more specific checks.
        };
        if (operation is TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals &&
            (InterfaceAddress(left) || InterfaceAddress(right))) valid = false;
        if (valid) return true;
        ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, site.Line, 0,
            OperatorText(operation), DisplayTypeName(left), DisplayTypeName(right));
        RecordType(site, Error);
        return false;
    }
}
