// Integer byte addresses/offsets only; these helpers neither allocate nor validate storage.
namespace Memory;

public bool IsAligned(nuint value, nuint alignment) nothrow
{
    return alignment != 0 && (alignment & (alignment - 1)) == 0
        && (value & (alignment - 1)) == 0;
}

public nuint AlignDown(nuint value, nuint alignment)
{
    if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        throw new std::Exception("Alignment must be a positive power of two");
    return value - (value & (alignment - 1));
}

public nuint AlignUp(nuint value, nuint alignment)
{
    if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        throw new std::Exception("Alignment must be a positive power of two");
    nuint remainder = value & (alignment - 1);
    if (remainder == 0) return value;
    nuint padding = alignment - remainder;
    nuint maximum = (nuint)0 - (nuint)1;
    if (value > maximum - padding) throw new std::Exception("Alignment overflow");
    return value + padding;
}
