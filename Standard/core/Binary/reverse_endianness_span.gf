using Standard::Memory;

namespace Standard::Binary;

public void ReverseEndianness(Span<readonly(byte)> source, Span<byte> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(sbyte)> source, Span<sbyte> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(ushort)> source, Span<ushort> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(short)> source, Span<short> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(uint)> source, Span<uint> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(int)> source, Span<int> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(ulong)> source, Span<ulong> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(long)> source, Span<long> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(nuint)> source, Span<nuint> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(nint)> source, Span<nint> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}

public void ReverseEndianness(Span<readonly(extralong)> source, Span<extralong> destination)
{
    if (destination.Length < source.Length) throw new Exception("Endian reversal destination is too short");
    if (source.IsEmpty) return;
    BinaryPrimitives::TryReverseEndianness(source.Data!, source.Length, destination.Data!, destination.Length);
}
