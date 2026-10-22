namespace std;

// Borrow the source owners; only the returned String owns new storage.
public String Join(this Span<immutable(String)> values, readonly(char)* separator, nuint separatorCount)
{
    nuint count = values.Length;
    if (count == 0) return new String();
    nuint maximum = (nuint)0 - (nuint)2;
    nuint total = 0;
    if (count > 1)
    {
        if (separatorCount > maximum / (count - 1)) throw new Exception("Joined String length overflow");
        total = separatorCount * (count - 1);
    }
    for (nuint i = 0; i < count; i++)
    {
        if (values[i].Length > maximum - total) throw new Exception("Joined String length overflow");
        total += values[i].Length;
    }
    String result = new();
    result.Reserve(total);
    for (nuint i = 0; i < count; i++)
    {
        if (i != 0) result.Append(separator, separatorCount);
        result.Append(values[i].Data, values[i].Length);
    }
    return move result;
}

public String Join(this Span<immutable(String)> values, readonly(char)* separator)
{
    if (values.Length < 2) return std::Join(values, separator, 0);
    nuint length = 0;
    while (separator[length] != '\0') length++;
    return std::Join(values, separator, length);
}
