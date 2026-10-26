namespace std::IO;

public class EndOfStreamException : IOException
{
    public EndOfStreamException() : base("End of input") { }
}
