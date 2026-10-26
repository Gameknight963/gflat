using std::Text;
namespace std::Text;

public String ToString(this byte value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this sbyte value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this short value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this ushort value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this int value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this uint value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this long value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this ulong value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this nint value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this nuint value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this bool value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this char value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}
