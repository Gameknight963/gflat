namespace std;

namespace detail
{
    extern int snprintf(char* buffer, nuint size, readonly(char)* format, ...);

    char* FloatingText(double value, readonly(char)* format) throws
    {
        char* result = AllocateText(64);
        int written = snprintf(result, 64, format, value);
        if (written < 0 || written >= 64)
        {
            Allocator::Free((void*)result);
            throw new* Exception(c"Floating-point formatting failed", 4);
        }
        return result;
    }
}

// General format with enough significant digits for round trips. Uses the C locale
// currently selected by the application, just like snprintf itself.
char* ToString(float value) throws { return detail::FloatingText((double)value, c"%.9g"); }
char* ToString(double value) throws { return detail::FloatingText(value, c"%.17g"); }
