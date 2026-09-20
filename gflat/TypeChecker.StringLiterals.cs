using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record LiteralOperator(MethodDeclaration Method, string Owner, NamespaceScope Scope);
    private readonly List<LiteralOperator> _literalOperators = new();

    public string GetStringLiteralOperatorName(MethodDeclaration method)
    {
        LiteralOperator op = _literalOperators.Single(o => ReferenceEquals(o.Method, method));
        string ns = GetFunctionNamespace(method);
        return $"gflat${(ns.Length == 0 ? "" : ns + "$")}{op.Owner}$literal${method.StringLiteralPrefix}";
    }

    private void RegisterStringLiteralOperators(CompilationUnit unit)
    {
        foreach (AstNode member in unit.Members) Collect(member, _globalScope, null, false);
        foreach (NamespaceDeclaration ns in unit.Namespaces) Collect(ns, _globalScope, null, false);

        void Collect(AstNode node, NamespaceScope scope, string? owner, bool generic)
        {
            switch (node)
            {
                case NamespaceDeclaration ns:
                    foreach (AstNode member in ns.Members) Collect(member, _namespaceScopes[ns], null, false);
                    break;
                case ClassDeclaration cls:
                    foreach (AstNode member in cls.Members) Collect(member, scope, cls.Name, generic || cls.IsGeneric);
                    break;
                case StructDeclaration str:
                    foreach (AstNode member in str.Members) Collect(member, scope, str.Name, generic || str.IsGeneric);
                    break;
                case InterfaceDeclaration iface:
                    foreach (AstNode member in iface.Members) Collect(member, scope, null, false);
                    break;
                case MethodDeclaration { StringLiteralPrefix: not null } method:
                    if (owner == null || generic)
                        throw new TypeCheckException("String literal operators require a non-generic class or struct", method.Line);
                    if (method.StringLiteralPrefix == "c")
                        throw new TypeCheckException("String prefix 'c' is reserved for built-in C strings", method.Line);
                    if (!method.IsStatic || method.Accessibility != TokenKind.Public || method.Body == null)
                        throw new TypeCheckException("String literal operators must be public static methods with a body", method.Line);
                    var previous = _currentNamespace;
                    _currentNamespace = scope;
                    try
                    {
                        method.ReturnType = ResolveAlias(method.ReturnType);
                        foreach (Parameter parameter in method.Parameters) parameter.Type = ResolveAlias(parameter.Type);
                        if (method.Parameters.Count != 2 || method.Parameters.Any(p => p.IsConst) ||
                            method.Parameters[0].Type is not PointerTypeExpression
                            { IsReadOnly: true, IsNullable: false, Inner: NamedTypeExpression { Name: "char" } } ||
                            method.Parameters[1].Type is not NamedTypeExpression { Name: "ulong" })
                            throw new TypeCheckException("String literal operators must take (readonly char* data, ulong length)", method.Line);
                        if (method.ReturnType is not NamedTypeExpression result ||
                            (result.Name != owner && result.Name != owner.Split('.').Last()))
                            throw new TypeCheckException("A string literal operator must return its containing class or struct", method.Line);
                        method.ReturnType = new NamedTypeExpression(owner, null, method.Line);
                    }
                    finally { _currentNamespace = previous; }
                    if (_literalOperators.Any(o => o.Scope == scope && o.Method.StringLiteralPrefix == method.StringLiteralPrefix))
                        throw new TypeCheckException($"Duplicate string prefix '{method.StringLiteralPrefix}' in the same namespace", method.Line);
                    _literalOperators.Add(new(method, owner, scope));
                    break;
            }
        }
    }

    private void CheckStringLiteralOperatorBody(MethodDeclaration method)
    {
        // Keep the owning type for private access and nested names, but do not declare this.
        Visit(method);
    }

    private void CheckStringLiteralCall(PrefixedStringLiteralExpression node)
    {
        var visible = new HashSet<NamespaceScope>();
        if (node.Scope != null)
        {
            NamespaceScope? scope = ResolveNamespace(node.Scope);
            if (scope == null) throw new TypeCheckException("Unknown namespace for string prefix", node.Line);
            visible.Add(scope);
        }
        else
        {
            for (NamespaceScope? scope = _currentNamespace; scope != null; scope = scope.Parent) visible.Add(scope);
            foreach (string name in _usingNamespaces)
                if (ResolveNamespaceByName(name) is NamespaceScope scope) visible.Add(scope);
        }
        var candidates = _literalOperators.Where(o => o.Method.StringLiteralPrefix == node.Prefix && visible.Contains(o.Scope)).ToList();
        if (candidates.Count == 0) throw new TypeCheckException($"Unknown string prefix '{node.Prefix}'", node.Line);
        if (candidates.Count > 1) throw new TypeCheckException($"Ambiguous string prefix '{node.Prefix}'; qualify it with its namespace", node.Line);
        MethodDeclaration method = candidates[0].Method;
        node.Literal.Accept(this);
        int length = StringLiteralEncoding.Bytes(node.Literal.Token.Text[1..^1], node.Line).Length;
        if (node.Arguments.Count == 1)
            node.Arguments.Add(new LiteralExpression(new Token(TokenKind.ULongLiteral, node.Line, 0, 0, length + "ul"), node.Line));
        node.Arguments[1].Accept(this);
        _resolvedCalls[node] = method;
        if (method.Throws) CheckThrowingCall(node, node.Prefix + "\"\"");
        RecordType(node, method.ReturnType);
    }
}
