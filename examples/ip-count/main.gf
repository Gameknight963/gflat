using Standard::Text;
using Standard::Net;
using Standard::Memory;
using Standard::Collections;
using Standard;

// Keep owned address values as keys; the input buffer is reused for every line.
int main(int argc, char** argv)
{
    if (argc != 1 && argc != 3)
    {
        Console::Error.WriteLine("Usage: ip-count [--network <CIDR>] < addresses.txt");
        return 2;
    }
    bool filtered = argc == 3;
    if (filtered && !CString::Equals(argv[1], "--network"))
    {
        Console::Error.WriteLine("Usage: ip-count [--network <CIDR>] < addresses.txt");
        return 2;
    }
    IpNetwork network = new();
    if (filtered && !IpNetwork::TryParse(argv[2], &network))
    {
        Console::Error.WriteLine("ip-count: invalid IP network");
        return 2;
    }
    HashMap<IpAddress, ulong> counts = new();
    Vector<IpAddress> order = new();
    try
    {
        char[256] line;
        nuint length = 0;
        nuint lineNumber = 0;
        while (Console::TryReadLine(line, 256, ref length))
        {
            if (lineNumber == (nuint)0 - (nuint)1) throw new Exception("Line count overflow");
            lineNumber++;
            Span<readonly(char)> text = new(line, length);
            text = text.Trim();
            if (text.Length == 0) continue;

            IpAddress address = new();
            if (!IpAddress::TryParse(text.Data!, text.Length, &address))
            {
                Console::Error.Write("Line ", 5);
                lineNumber.Format(Console::Error);
                Console::Error.WriteLine(": invalid IP address");
                return 2;
            }
            if (filtered && !network.Contains(&address)) continue;
            ulong*? existing = counts.Find(&address);
            if (existing == null)
            {
                counts.Add(address, 1ul);
                order.Add(address);
            }
            else
            {
                if (*existing! == 0ul - 1ul) throw new Exception("Address count overflow");
                (*existing!)++;
            }
        }

        // Validate the complete input before writing any counts.
        for (nuint i = 0; i < order.Count; i++)
        {
            IpAddress address = order[i];
            address.Format(Console::Out);
            Console::Write("\t", 1);
            counts[&address].Format(Console::Out);
            Console::WriteLine();
        }
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.Write("ip-count: ", 10);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
}
