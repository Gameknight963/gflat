using Standard::Text;
namespace Standard::Text;

// Optional alongside ExtraLongFormatting.gf; no float/libc formatting dependency.
public String ToString(extralong value)
=> value.ToString();

extension extralong
{
    public String ToString()
    {
        String result = new();
        Standard::Format((*this), &result);
        return move result;
    }
}
