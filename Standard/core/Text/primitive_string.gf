using Standard::Text;
namespace Standard::Text;

public String ToString(byte value)
=> value.ToString();

extension byte
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(sbyte value)
=> value.ToString();

extension sbyte
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(short value)
=> value.ToString();

extension short
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(ushort value)
=> value.ToString();

extension ushort
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(int value)
=> value.ToString();

extension int
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(uint value)
=> value.ToString();

extension uint
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(long value)
=> value.ToString();

extension long
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(ulong value)
=> value.ToString();

extension ulong
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(nint value)
=> value.ToString();

extension nint
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(nuint value)
=> value.ToString();

extension nuint
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(bool value)
=> value.ToString();

extension bool
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(char value)
=> value.ToString();

extension char
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}
