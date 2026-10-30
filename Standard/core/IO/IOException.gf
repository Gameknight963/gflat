namespace Standard::IO;

public class IOException : Exception
{
    public IOException(immutable(char*) message = "Input/output failed") : base(message, 1) { }
}

