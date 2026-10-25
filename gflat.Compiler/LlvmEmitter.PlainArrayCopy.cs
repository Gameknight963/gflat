using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private const string PlainArrayCopyName = "llvm.memcpy.inline.p0.p0.i64";
    private string PlainArrayCopyDeclaration =>
        _usesPlainArrayCopy && !llvmExternalDeclarations.ContainsKey(PlainArrayCopyName)
            ? "declare void @" + PlainArrayCopyName + "(i8*, i8*, i64 immarg, i1 immarg) nounwind\n"
            : "";
    private bool _usesPlainArrayCopy;

    private bool TryEmitPlainArrayLocalInitializer(AstNode initializer,
        TypeExpression targetType, string destination)
    {
        if (initializer is not (IdentifierExpression or MemberAccessExpression or
            IndexExpression or NamespaceAccessExpression)) return false;
        if (_typeChecker.IsFreshValue(initializer)) return false;
        TypeExpression sourceType = _typeChecker.ResolveAlias(_typeChecker.GetType(initializer));
        targetType = _typeChecker.ResolveAlias(targetType);
        if (sourceType is not ArrayTypeExpression { Size: not null } ||
            targetType is not ArrayTypeExpression { Size: not null }) return false;
        // Protection qualifiers may differ; the exact LLVM representation must not.
        if (EmitType(sourceType) != EmitType(targetType)) return false;
        if (_typeChecker.RequiresCopy(sourceType) || _typeChecker.RequiresCopy(targetType) ||
            _typeChecker.HasDestructor(sourceType) || _typeChecker.HasDestructor(targetType) ||
            HasManagedReferences(sourceType) || HasManagedReferences(targetType)) return false;

        int size = _typeChecker.GetTypeSize(targetType);
        // Evaluate exactly once, even for zero size. Retains static startup and
        // existing address/null/index checks; no member/property reinference.
        EmitArrayReadAddress(initializer);
        string source = Pop();
        if (size == 0) return true;
        _usesPlainArrayCopy = true;
        string llvm = EmitType(targetType);
        string sourceBytes = NewTemp(), destinationBytes = NewTemp();
        Emit($"    {sourceBytes} = bitcast {llvm}* {source} to i8*");
        Emit($"    {destinationBytes} = bitcast {llvm}* {destination} to i8*");
        EmitCallInstruction("void", "@" + PlainArrayCopyName,
            $"i8* align 1 {destinationBytes}, i8* align 1 {sourceBytes}, i64 {size}, i1 false",
            handleUnwind: false);
        return true;
    }
}
