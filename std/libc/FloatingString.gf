namespace std;

// Uses the existing libc-backed formatting policy without adding it to core.
public String ToString(this float value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}

public String ToString(this double value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}
