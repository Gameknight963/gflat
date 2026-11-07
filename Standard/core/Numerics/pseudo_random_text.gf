using Standard::Memory;
using Standard::Text;

namespace Standard::Numerics;

public String GetString(ref PseudoRandom random, Span<readonly(char)> choices, nuint length)
{
    if (choices.IsEmpty) throw new Exception("Random choices must not be empty");
    String result = new();
    result.Resize(length);
    for (nuint i = 0; i < length; i++)
        result[i] = choices[(nuint)random.NextULong((ulong)choices.Length)];
    return move result;
}

public String GetString(ref PseudoRandom random, readonly(char)* choices, nuint length)
    => GetString(ref random, new Span<readonly(char)>(choices, CString::Length(choices)), length);

public void GetHexString(ref PseudoRandom random, Span<char> destination, bool lowercase = false)
{
    readonly(char)* alphabet = "0123456789ABCDEF";
    if (lowercase) alphabet = "0123456789abcdef";
    for (nuint i = 0; i < destination.Length; i++)
        destination[i] = alphabet[(nuint)random.NextULong(16ul)];
}

public String GetHexString(ref PseudoRandom random, nuint length, bool lowercase = false)
{
    String result = new();
    result.Resize(length);
    if (length != 0)
        GetHexString(ref random, new Span<char>((char*)result.Data, length), lowercase);
    return move result;
}

extension PseudoRandom
{
    public String GetString(Span<readonly(char)> choices, nuint length)
        => Standard::Numerics::GetString(ref *this, choices, length);

    public String GetString(readonly(char)* choices, nuint length)
        => Standard::Numerics::GetString(ref *this, choices, length);

    public void GetHexString(Span<char> destination, bool lowercase = false)
        => Standard::Numerics::GetHexString(ref *this, destination, lowercase);

    public String GetHexString(nuint length, bool lowercase = false)
        => Standard::Numerics::GetHexString(ref *this, length, lowercase);
}
