using Standard::Text;
namespace Standard::Text;

// Uses the existing libc-backed formatting policy without adding it to core.
public String ToString(float value)
=> value.ToString();

extension float
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}

public String ToString(double value)
=> value.ToString();

extension double
{
    public immutable String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}
