namespace std;

public class IOException : Exception
{
    public IOException(readonly(char*) message = "Input/output failed") : base(message, 1) { }
}

public class EndOfStreamException : IOException
{
    public EndOfStreamException() : base("End of input") { }
}
