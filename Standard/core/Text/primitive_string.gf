using Standard::Text;
namespace Standard::Text;

public String ToString(byte value)
=> value.ToString();

extension byte
{
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
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
    public String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}
