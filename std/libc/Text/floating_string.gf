using std::Text;
namespace std::Text;

// Uses the existing libc-backed formatting policy without adding it to core.
public String ToString(float value)
=> value.ToString();

extension float
{
    public String ToString()
    {
        String result = new();
        std::Format((*this), &result);
        return move result;
    }
}

public String ToString(double value)
=> value.ToString();

extension double
{
    public String ToString()
    {
        String result = new();
        std::Format((*this), &result);
        return move result;
    }
}
