// Automatically available formatting contracts. No libc dependency.
interface ITextWriter
{
    void Write(readonly(char)* data, nuint length);
}

interface IFormattable
{
    readonly void Format(ITextWriter* output);
}
