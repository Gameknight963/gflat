using std::Text;
using std::Time;
using std;

struct UtcPrefix : IFormattable
{
    public immutable void Format(ITextWriter* output)
    {
        // Read the clock before emitting any part of this prefix.
        DateTime utc = NativeClock::GetUtcNow();
        output.Write("[", 1);
        utc.Format(output);
        output.Write("Z] ", 3);
    }
}

int main(int argc, char** argv)
{
    try
    {
        if (argc != 1)
        {
            Console::Error.WriteLine("Usage: stamp-lines");
            Console::FlushError();
            return 2;
        }
        UtcPrefix prefix = new();
        LinePrefixWriter lines = new(Console::Out, &prefix);
        char[4096] buffer;
        nuint count = Console::Read(buffer, 4096);
        while (count != 0)
        {
            lines.Write(buffer, count);
            count = Console::Read(buffer, 4096);
        }
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine("Unable to stamp input");
        Console::FlushError();
        return 1;
    }
}
