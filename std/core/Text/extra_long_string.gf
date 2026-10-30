using std::Text;
namespace std::Text;

// Optional alongside ExtraLongFormatting.gf; no float/libc formatting dependency.
public String ToString(extralong value)
=> value.ToString();

extension extralong
{
    public String ToString()
    {
        String result = new();
        std::Format((*this), &result);
        return move result;
    }
}
