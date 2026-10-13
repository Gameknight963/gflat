namespace std;

public class BadAllocException : Exception
{
	public BadAllocException(readonly(char*) message = "Allocation failed") : base(message, 1) { }
}