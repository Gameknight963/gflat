using Standard::Memory;

namespace Standard::Encoding;

public bool TryEncodeBase64(Span<readonly(char)> data, Span<char> destination,
    ref nuint written) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written);
}

public bool TryEncodeBase64(Span<readonly(byte)> data, Span<char> destination,
    ref nuint written) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written);
}

public bool TryGetBase64DecodedLength(Span<readonly(char)> text, ref nuint length) nothrow
{
    length = 0;
    if (text.IsEmpty) return true;
    return Base64::TryGetDecodedLength(text.Data!, text.Length, ref length);
}

public bool TryDecodeBase64(Span<readonly(char)> text, Span<char> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

public bool TryDecodeBase64(Span<readonly(char)> text, Span<byte> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

extension Span<readonly(char)>
{
    public bool TryEncodeBase64(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryEncodeBase64(*this, destination, ref written);

    public bool TryGetBase64DecodedLength(ref nuint length) nothrow
        => Standard::Encoding::TryGetBase64DecodedLength(*this, ref length);

    public bool TryDecodeBase64(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodeBase64(*this, destination, ref written);

    public bool TryDecodeBase64(Span<byte> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodeBase64(*this, destination, ref written);
}

extension Span<readonly(byte)>
{
    public bool TryEncodeBase64(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryEncodeBase64(*this, destination, ref written);
}
