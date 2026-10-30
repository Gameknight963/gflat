using Standard::Text;
using Standard::Time;
using Standard;

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        Console::Error.WriteLine("Usage: time-convert <mode> <value>");
        return 2;
    }
    readonly(char)* mode = argv[1];
    int operation = -1;
    if (CString::Equals(mode, "utc")) operation = 0;
    else if (CString::Equals(mode, "seconds")) operation = 1;
    else if (CString::Equals(mode, "milliseconds")) operation = 2;
    else if (CString::Equals(mode, "from-seconds")) operation = 3;
    else if (CString::Equals(mode, "from-milliseconds")) operation = 4;
    if (operation < 0)
    {
        Console::Error.WriteLine("time-convert: expected utc, seconds, milliseconds, from-seconds, or from-milliseconds");
        return 2;
    }
    try
    {
        DateTimeOffset value = new();
        if (operation < 3)
        {
            if (!DateTimeOffset::TryParse(argv[2], &value))
            {
                Console::Error.WriteLine("time-convert: expected an explicit-offset timestamp");
                return 2;
            }
            if (operation == 0)
            {
                DateTimeOffset utc = value.ToOffset(TimeSpan::FromTicks(0l));
                utc.Format(Console::Out);
            }
            else if (operation == 1) Standard::Format(value.ToUnixTimeSeconds(), Console::Out);
            else Standard::Format(value.ToUnixTimeMilliseconds(), Console::Out);
        }
        else
        {
            long number = 0l;
            if (!TryParseLong(argv[2], &number))
            {
                Console::Error.WriteLine("time-convert: expected a signed decimal long");
                return 2;
            }
            if (operation == 3) value = DateTimeOffset::FromUnixTimeSeconds(number);
            else value = DateTimeOffset::FromUnixTimeMilliseconds(number);
            value.Format(Console::Out);
        }
        Console::WriteLine();
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.Write("time-convert: ", 14);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
}
