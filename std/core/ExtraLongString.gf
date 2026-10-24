namespace std;

// Optional alongside ExtraLongFormatting.gf; no float/libc formatting dependency.
public String ToString(this extralong value)
{
    String result = new();
    std::Format(value, &result);
    return move result;
}
