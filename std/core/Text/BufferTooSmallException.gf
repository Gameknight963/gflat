namespace std;

public class BufferTooSmallException : Exception
{
    public BufferTooSmallException() : base("The buffer is too small") { }
}
