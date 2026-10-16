namespace std;

public class IOException : Exception
{
    public IOException(readonly(char*) message = "Input/output failed") : base(message, 1) { }
}

