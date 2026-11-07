using Standard::Memory;

namespace Standard::Encoding;

public bool TryGetPercentEncodedLength(Span<readonly(char)> data, ref nuint length) nothrow
{
    length = 0;
    if (data.IsEmpty) return true;
    return PercentEncoding::TryGetEncodedLength(data.Data!, data.Length, ref length);
}

public bool TryGetPercentEncodedLength(Span<readonly(byte)> data, ref nuint length) nothrow
{
    length = 0;
    if (data.IsEmpty) return true;
    return PercentEncoding::TryGetEncodedLength(data.Data!, data.Length, ref length);
}

public bool TryEncodePercent(Span<readonly(char)> data, Span<char> destination,
    ref nuint written) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return PercentEncoding::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written);
}

public bool TryEncodePercent(Span<readonly(byte)> data, Span<char> destination,
    ref nuint written) nothrow
{
    written = 0;
    if (data.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return PercentEncoding::TryEncode(data.Data!, data.Length, destination.Data!, destination.Length, ref written);
}

public bool TryGetPercentDecodedLength(Span<readonly(char)> text, ref nuint length) nothrow
{
    length = 0;
    if (text.IsEmpty) return true;
    return PercentEncoding::TryGetDecodedLength(text.Data!, text.Length, ref length);
}

public bool TryDecodePercent(Span<readonly(char)> text, Span<char> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return PercentEncoding::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

public bool TryDecodePercent(Span<readonly(char)> text, Span<byte> destination, ref nuint written) nothrow
{
    written = 0;
    if (text.IsEmpty) return true;
    if (destination.IsEmpty) return false;
    return PercentEncoding::TryDecode(text.Data!, text.Length, destination.Data!, destination.Length, ref written);
}

extension Span<readonly(char)>
{
    public bool TryGetPercentEncodedLength(ref nuint length) nothrow
        => Standard::Encoding::TryGetPercentEncodedLength(*this, ref length);

    public bool TryEncodePercent(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryEncodePercent(*this, destination, ref written);

    public bool TryGetPercentDecodedLength(ref nuint length) nothrow
        => Standard::Encoding::TryGetPercentDecodedLength(*this, ref length);

    public bool TryDecodePercent(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodePercent(*this, destination, ref written);

    public bool TryDecodePercent(Span<byte> destination, ref nuint written) nothrow
        => Standard::Encoding::TryDecodePercent(*this, destination, ref written);
}

extension Span<readonly(byte)>
{
    public bool TryGetPercentEncodedLength(ref nuint length) nothrow
        => Standard::Encoding::TryGetPercentEncodedLength(*this, ref length);

    public bool TryEncodePercent(Span<char> destination, ref nuint written) nothrow
        => Standard::Encoding::TryEncodePercent(*this, destination, ref written);
}
