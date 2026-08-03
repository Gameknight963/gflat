namespace gflat.CompileExceptions
{
    public class TypeCheckException : Exception
    {
        public int Line { get; }
        public TypeCheckException(string message, int line) : base($"line {line}: {message}")
            => Line = line;
    }
}
