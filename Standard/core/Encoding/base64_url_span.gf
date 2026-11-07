using Standard::Memory;

namespace Standard::Encoding;

public bool TryEncodeBase64Url(Span<readonly(char)> data, Span<char> destination,
    ref nuint written) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64Url::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written);
}

public bool TryEncodeBase64Url(Span<readonly(byte)> data, Span<char> destination,
    ref nuint written) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64Url::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written);
}

public bool TryGetBase64UrlDecodedLength(Span<readonly(char)> text, ref nuint length) nothrow
{
    length = 0;
    if (text.IsEmpty) return true;
    return Base64Url::TryGetDecodedLength(text.Data!, text.Length, ref length);
}

public bool TryDecodeBase64Url(Span<readonly(char)> text, Span<char> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64Url::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

public bool TryDecodeBase64Url(Span<readonly(char)> text, Span<byte> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return Base64Url::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

extension Span<readonly(char)>
{
    public bool TryEncodeBase64Url(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryEncodeBase64Url(*this, destination, ref written);

    public bool TryGetBase64UrlDecodedLength(ref nuint length) nothrow
        => Standard::Encoding::TryGetBase64UrlDecodedLength(*this, ref length);

    public bool TryDecodeBase64Url(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodeBase64Url(*this, destination, ref written);

    public bool TryDecodeBase64Url(Span<byte> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodeBase64Url(*this, destination, ref written);
}

extension Span<readonly(byte)>
{
    public bool TryEncodeBase64Url(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryEncodeBase64Url(*this, destination, ref written);
}
