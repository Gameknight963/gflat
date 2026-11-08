using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private bool hasNativeOpaqueRecords;
    private readonly Dictionary<NewExpression, CallExpression> nativeCreations = new();
    private readonly Dictionary<DeleteStatement, CallExpression> nativeDeletions = new();
    private readonly HashSet<CallExpression> checkedNativeArguments = new();
    private readonly Dictionary<AstNode, string> nativeFactoryCallees = new();

    public CallExpression? GetNativeCreation(NewExpression node) => nativeCreations.GetValueOrDefault(node);
    public CallExpression? GetNativeDeletion(DeleteStatement node) => nativeDeletions.GetValueOrDefault(node);

    public IReadOnlyList<MethodDeclaration> GetNativeConstructorCandidates(TypeExpression type)
    {
        if (!hasNativeOpaqueRecords) return [];
        type = ResolveAlias(type);
        if (type is not NamedTypeExpression named ||
            GetStructByIdentity(named.Name)?.Declaration?.NativeConstructorFactory is not { } factory ||
            !_globalScope.Functions.TryGetValue(factory, out var method)) return [];
        return (overloadFamilies.GetValueOrDefault(method) ?? [method]).Where(candidate => candidate.IsForeignImport).ToArray();
    }

    private CallExpression NativeFactoryCall(string factory, List<AstNode> arguments, AstNode site)
    {
        var callee = new NamespaceAccessExpression(new GlobalExpression(site.Line) { Span = site.Span }, factory, site.Line) { Span = site.Span };
        nativeFactoryCallees[callee] = factory;
        return new(callee, arguments, site.Line) { Span = site.Span };
    }

    private bool TryCheckNativeCreation(NewExpression node, TypeExpression type)
    {
        if (NativeOpaqueLayout(type) == null || node.Kind != AllocationKind.Pointer || node.Destination != null) return false;
        var named = (NamedTypeExpression)ResolveAlias(type);
        var declaration = GetStructByIdentity(named.Name)!.Declaration!;
        if (declaration.NativeConstructorFactory is not { } factory)
            throw new TypeCheckException($"Native type '{DisplayTypeName(type)}' has no supported public constructor", node.Span);
        var call = NativeFactoryCall(factory, node.Arguments, node);
        try { call.Accept(this); }
        catch (TypeCheckException error) when (error.Description.Contains(factory, StringComparison.Ordinal))
        { throw new TypeCheckException(error.Description.Replace(factory, DisplayTypeName(type) + " constructor", StringComparison.Ordinal), error.Span); }
        nativeCreations[node] = call;
        RecordType(node, new PointerTypeExpression(type.WithoutValueProtection(), false, node.Line));
        return true;
    }

    private bool TryCheckNativeDeletion(DeleteStatement node, TypeExpression type)
    {
        if (NativeOpaqueLayout(type) == null) return false;
        var named = (NamedTypeExpression)ResolveAlias(type);
        var declaration = GetStructByIdentity(named.Name)!.Declaration!;
        if (declaration.NativeDeleteFactory is not { } factory)
            throw new TypeCheckException($"Native type '{DisplayTypeName(type)}' has no accessible native destructor; use its native destruction API", node.Span);
        var call = NativeFactoryCall(factory, [node.Target], node);
        checkedNativeArguments.Add(call);
        call.Accept(this);
        nativeDeletions[node] = call;
        return true;
    }

    private NativeRecordLayout? NativeOpaqueLayout(TypeExpression type)
    {
        if (!hasNativeOpaqueRecords) return null;
        type = ResolveAlias(type);
        return type is NamedTypeExpression named
            ? GetStructByIdentity(named.Name)?.Declaration?.NativeOpaqueLayout : null;
    }

    private void RejectNativeOpaqueValue(TypeExpression type, int line)
    {
        if (!hasNativeOpaqueRecords) return;
        type = ResolveAlias(type);
        while (type is ArrayTypeExpression array) type = ResolveAlias(array.ElementType);
        if (NativeOpaqueLayout(type) != null)
            throw new TypeCheckException($"Native type '{DisplayTypeName(type)}' is pointer-only. Construct, copy, and destroy it in native code", line);
    }
}
