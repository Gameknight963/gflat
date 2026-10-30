namespace Standard::Json;

public class JsonWriteException : Exception
{
    public JsonWriteException(readonly(char)* message) : base(message, 1) { }
}
