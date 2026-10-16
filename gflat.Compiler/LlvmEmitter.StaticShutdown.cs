namespace gflat;

public partial class LlvmEmitter
{
    private void ValidateStaticShutdownAbi()
    {
        if (_nativeExternDeclarations.TryGetValue("atexit", out string? declaration) &&
            declaration != "declare i32 @atexit(void ()*)")
        {
            var field = _typeChecker.GetStaticStorage().First(s => NeedsCleanup(s.Field.Type)).Field;
            throw new CompileExceptions.TypeCheckException(
                "Static cleanup requires the native atexit signature 'extern int atexit(void()* callback)'", field.Span);
        }
    }
}
