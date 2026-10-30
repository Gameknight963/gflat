namespace std;

namespace detail
{
    extern int snprintf(char* buffer, nuint size, readonly(char)* format, ...);
    void FloatingText(double value, readonly(char)* format, ITextWriter* output)
    {
        char[64] buffer;
        int written = snprintf(buffer, 64, format, value);
        if (written < 0 || written >= 64)
            throw new Exception("Floating-point formatting failed", 4);
        output.Write(buffer, (nuint)written);
    }
}

// Locale follows the application's C locale. Enough digits for round trips.
void Format(float value, ITextWriter* output) => value.Format(output);

extension float
{
    public void Format(ITextWriter* output)
    { detail::FloatingText((double)(*this), "%.9g", output); }
}
void Format(double value, ITextWriter* output) => value.Format(output);

extension double
{
    public void Format(ITextWriter* output)
    { detail::FloatingText((*this), "%.17g", output); }
}
