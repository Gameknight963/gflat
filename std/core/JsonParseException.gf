namespace std;

public class JsonParseException : Exception
{
    public readonly(nuint) Offset;
    public JsonParseException(readonly(char)* message, nuint offset) : base(message, 1)
    {
        Offset = offset;
    }
}
