using Standard::Memory;

namespace Standard::Encoding;

public bool TryEncodeHex(Span<readonly(char)> data, Span<char> destination,
    ref nuint written, bool uppercase = true) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Hex::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written, uppercase);
}

public bool TryEncodeHex(Span<readonly(byte)> data, Span<char> destination,
    ref nuint written, bool uppercase = true) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Hex::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written, uppercase);
}

public bool TryGetHexDecodedLength(Span<readonly(char)> text, ref nuint length) nothrow
{
    length = 0;
    if (text.IsEmpty) return true;
    return Hex::TryGetDecodedLength(text.Data!, text.Length, ref length);
}

public bool TryDecodeHex(Span<readonly(char)> text, Span<char> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Hex::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

public bool TryDecodeHex(Span<readonly(char)> text, Span<byte> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Hex::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

extension Span<readonly(char)>
{
    public bool TryEncodeHex(Span<char> destination, ref nuint written, bool uppercase = true) nothrow
        => Standard::Encoding::TryEncodeHex(*this, destination, ref written, uppercase);

    public bool TryGetHexDecodedLength(ref nuint length) nothrow
        => Standard::Encoding::TryGetHexDecodedLength(*this, ref length);

    public bool TryDecodeHex(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodeHex(*this, destination, ref written);

    public bool TryDecodeHex(Span<byte> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodeHex(*this, destination, ref written);
}

extension Span<readonly(byte)>
{
    public bool TryEncodeHex(Span<char> destination, ref nuint written, bool uppercase = true) nothrow
        => Standard::Encoding::TryEncodeHex(*this, destination, ref written, uppercase);
}
