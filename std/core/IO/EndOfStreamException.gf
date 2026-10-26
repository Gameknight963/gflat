namespace std;

public class EndOfStreamException : IOException
{
    public EndOfStreamException() : base("End of input") { }
}
