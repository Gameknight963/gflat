using System.Text;
using System.Text.RegularExpressions;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public sealed record LlvmCallInfo(string Ir, string? Entry, TypeExpression Result, IReadOnlyList<TypeExpression> Arguments, int SourceArguments);
    private readonly Dictionary<CallExpression, LlvmCallInfo> llvmCalls = new();
    public LlvmCallInfo? GetLlvmCall(CallExpression call) => llvmCalls.GetValueOrDefault(call);

    private bool TryCheckLlvmCall(CallExpression node)
    {
        if (node.Callee is not IdentifierExpression { Name: "__llvm" or "__llvm_module" } identifier) return false;
        int literalCount = identifier.Name == "__llvm_module" ? 2 : 1;
        if (node.TypeArguments.Count != 1 || node.Arguments.Count < literalCount)
            throw new TypeCheckException($"{identifier.Name} requires one explicit result type and {literalCount} leading string literal(s)", node.Span);
        string Text(int index)
        {
            if (node.Arguments[index] is not LiteralExpression { Token.Kind: TokenKind.StringLiteral } literal)
                throw new TypeCheckException("LLVM IR and entry names must be ordinary string literals", node.Arguments[index].Span);
            literal.Accept(this);
            string text = Encoding.UTF8.GetString(StringLiteralEncoding.Bytes(literal.Token.Text[1..^1], node.Line));
            if (text.Contains('\0')) throw new TypeCheckException("LLVM text cannot contain null characters", node.Arguments[index].Span);
            return text;
        }
        string ir = Text(0);
        string? entry = literalCount == 2 ? Text(1) : null;
        if (entry != null && !Regex.IsMatch(entry, @"\A[A-Za-z_$.-][A-Za-z0-9_$.-]*\z"))
            throw new TypeCheckException("LLVM entry must be an unquoted LLVM symbol name", node.Span);
        TypeExpression result = ResolveAlias(node.TypeArguments[0]);
        ValidateTypeUsage(result, node.Line);
        ValidateLlvmValueType(result, allowVoid: true, node);
        var arguments = new List<TypeExpression>();
        foreach (var expression in node.Arguments.Skip(literalCount))
        {
            CheckNullableArgument(expression);
            TypeExpression type = ResolveAlias(GetType(expression));
            ValidateLlvmValueType(type, allowVoid: false, node);
            arguments.Add(type);
        }
        llvmCalls[node] = new(ir, entry, result, arguments, literalCount);
        RecordType(node, result);
        return true;
    }

    private void ValidateLlvmValueType(TypeExpression type, bool allowVoid, AstNode node)
    {
        type = ResolveAlias(type);
        if (ContainsManagedReference(type) || type is ReferenceTypeExpression or ArrayTypeExpression ||
            type is PointerTypeExpression pointer && ResolveAlias(pointer.Inner) is NamedTypeExpression iface && IsInterface(iface) ||
            type is NamedTypeExpression named && !(IsPrimitive(named.Name) && (allowVoid || named.Name != "void") || ResolveEnum(named) != null))
            throw new TypeCheckException("LLVM calls support primitive values, enums, function pointers and raw pointers only; use raw pointers for aggregate storage, without managed references", node.Span);
    }
}
