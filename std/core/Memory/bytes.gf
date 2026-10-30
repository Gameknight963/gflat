// Raw bytes only: these operations do not copy object ownership or GC metadata.
namespace Memory;

public void Copy(void* destination, readonly(void)* source, nuint count) nothrow
{
    byte* output = (byte*)destination;
    readonly(byte)* input = (readonly(byte)*)source;
    for (nuint i = 0; i < count; i++) output[i] = input[i];
}

public void Move(void* destination, readonly(void)* source, nuint count) nothrow
{
    byte* output = (byte*)destination;
    readonly(byte)* input = (readonly(byte)*)source;
    if ((output).reinterpret<nuint>() <= (input).reinterpret<nuint>())
    {
        for (nuint i = 0; i < count; i++) output[i] = input[i];
    }
    else
    {
        nuint remaining = count;
        while (remaining != 0) { remaining--; output[remaining] = input[remaining]; }
    }
}

public void Fill(void* destination, byte value, nuint count) nothrow
{
    byte* output = (byte*)destination;
    for (nuint i = 0; i < count; i++) output[i] = value;
}

public int Compare(readonly(void)* left, readonly(void)* right, nuint count) nothrow
{
    readonly(byte)* first = (readonly(byte)*)left;
    readonly(byte)* second = (readonly(byte)*)right;
    for (nuint i = 0; i < count; i++)
    {
        if (first[i] != second[i]) return (int)first[i] - (int)second[i];
    }
    return 0;
}

public bool Equal(readonly(void)* left, readonly(void)* right, nuint count) nothrow
{
    return Memory::Compare(left, right, count) == 0;
}
