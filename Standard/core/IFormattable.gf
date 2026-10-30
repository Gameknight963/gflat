// Automatically available formatting contract. No libc dependency.
interface IFormattable
{
    immutable void Format(ITextWriter* output);
}
