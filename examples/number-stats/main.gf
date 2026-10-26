using std::Memory;
using std::Numerics;
using std;

void WriteField(readonly(char)* label, extralong value)
{
    Console::Write(label);
    value.Format(Console::Out);
    Console::WriteLine();
}

void ReportLine(nuint line, readonly(char)* message)
{
    Console::Error.Write("Line ", 5);
    line.Format(Console::Error);
    Console::Error.Write(": ", 2);
    Console::Error.WriteLine(message);
}

int main()
{
    char[128] buffer;
    nuint lineNumber = 0;
    nuint count = 0;
    extralong sum = (extralong)0;
    long minimum = 0l;
    long maximum = 0l;
    try
    {
        nuint length = 0;
        while (Console::TryReadLine(buffer, 128, ref length))
        {
            if (lineNumber == (nuint)0 - (nuint)1)
                throw new Exception("Input line count overflow");
            lineNumber++;
            Span<readonly(char)> text = new(buffer, length);
            Span<readonly(char)> token = text.Trim();
            if (token.IsEmpty) continue;
            long value = 0l;
            if (!TryParseLong(token.Data!, token.Length, &value))
            {
                ReportLine(lineNumber, "Expected a signed 64-bit decimal integer");
                return 2;
            }
            if (count == 0) { minimum = value; maximum = value; }
            else { minimum = Math::Min(minimum, value); maximum = Math::Max(maximum, value); }
            // count <= lineNumber <= maximum nuint; signed 128 bits hold this sum.
            count++;
            sum += (extralong)value;
        }
        WriteField("Count: ", (extralong)count);
        WriteField("Sum: ", sum);
        if (count != 0)
        {
            WriteField("Min: ", (extralong)minimum);
            WriteField("Max: ", (extralong)maximum);
        }
        Console::Flush();
    }
    catch (ref BufferTooSmallException error)
    {
        if (lineNumber == (nuint)0 - (nuint)1)
            Console::Error.WriteLine("Input line count overflow");
        else ReportLine(lineNumber + 1, "Line exceeds the 127-byte payload limit");
        return 2;
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
