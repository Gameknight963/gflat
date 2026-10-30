// Automatically available formatting contract. No libc dependency.
interface ITextWriter
{
    void Write(readonly(char)* data, nuint length);
}
