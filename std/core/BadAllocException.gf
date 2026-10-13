namespace std;

public class BadAllocException : Exception
{
	public BadAllocException(readonly(char*) message = c"Allocation failed") : base(message, 1) { }
}