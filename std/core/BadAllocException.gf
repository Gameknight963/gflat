namespace std;

public class BadAllocException : Exception
{
	public BadAllocException(immutable(char*) message = "Allocation failed") : base(message, 1) { }
}