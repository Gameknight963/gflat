using std::Memory;
namespace std::Text;

// Borrow the source owners; only the returned String owns new storage.
public String Join(Span<immutable(String)> values, readonly(char)* separator, nuint separatorCount)
=> values.Join(separator, separatorCount);

extension Span<immutable(String)>
{
    public String Join(readonly(char)* separator, nuint separatorCount)
    {
        nuint count = (*this).Length;
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
            if ((*this)[i].Length > maximum - total) throw new Exception("Joined String length overflow");
            total += (*this)[i].Length;
        }
        String result = new();
        result.Reserve(total);
        for (nuint i = 0; i < count; i++)
        {
            if (i != 0) result.Append(separator, separatorCount);
            result.Append((*this)[i].Data, (*this)[i].Length);
        }
        return move result;
    }
}

public String Join(Span<immutable(String)> values, readonly(char)* separator)
=> values.Join(separator);

extension Span<immutable(String)>
{
    public String Join(readonly(char)* separator)
    {
        if ((*this).Length < 2) return std::Text::Join((*this), separator, 0);
        nuint length = 0;
        while (separator[length] != '\0') length++;
        return std::Text::Join((*this), separator, length);
    }
}
