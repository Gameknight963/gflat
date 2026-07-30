using gssembler;

const string OutputExtension = ".bin";

if (args.Length < 1)
{
    Console.Error.WriteLine($"usage: gasm <input.gasm> [output{OutputExtension}]");
    Environment.Exit(1);
}

string inputPath = args[0];
string outputPath = args.Length >= 2 ? args[1] : Path.ChangeExtension(inputPath, OutputExtension);

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"{inputPath}: no such file");
    Environment.Exit(1);
}

string[] lines = File.ReadAllLines(inputPath);
List<byte> bytecode = new();

for (int i = 0; i < lines.Length; i++)
{
    string line = lines[i];

    // strip comments
    int commentIndex = line.IndexOf("//");
    if (commentIndex >= 0)
        line = line[..commentIndex];

    line = line.Trim();

    if (line.Length == 0)
        continue;

    string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    string mnemonic = parts[0].ToUpper();

    if (!Enum.TryParse(mnemonic, ignoreCase: true, out OpCode opCode))
    {
        Console.Error.WriteLine($"line {i + 1}: unknown instruction '{mnemonic}'");
        Environment.Exit(1);
    }

    // handle operands based on opcode
    if (opCode == OpCode.HALT)
    {
        byte code = parts.Length >= 2 ? ParseByte(parts[1], i + 1) : (byte)0;
        bytecode.Add((byte)opCode);
        bytecode.Add(code);
    }
    else if (opCode == OpCode.PUSH)
    {
        if (parts.Length < 2)
        {
            Console.Error.WriteLine($"line {i + 1}: PUSH requires an operand");
            Environment.Exit(1);
        }
        int value = ParseInt(parts[1], i + 1);
        bytecode.Add((byte)opCode);
        bytecode.Add((byte)(value & 0xFF));
        bytecode.Add((byte)((value >> 8) & 0xFF));
        bytecode.Add((byte)((value >> 16) & 0xFF));
        bytecode.Add((byte)((value >> 24) & 0xFF));
    }
    else
    {
        bytecode.Add((byte)opCode);
    }
}

File.WriteAllBytes(outputPath, bytecode.ToArray());
Console.WriteLine($"assembled {lines.Length} lines -> {outputPath} ({bytecode.Count} bytes)");

static byte ParseByte(string s, int line)
{
    if (byte.TryParse(s, out byte result))
        return result;
    Console.Error.WriteLine($"line {line}: invalid byte value '{s}'");
    Environment.Exit(1);
    return 0;
}

static int ParseInt(string s, int line)
{
    // support 'X' char literals
    if (s.Length == 3 && s[0] == '\'' && s[2] == '\'')
        return s[1];

    if (int.TryParse(s, out int result))
        return result;

    Console.Error.WriteLine($"line {line}: invalid integer value '{s}'");
    Environment.Exit(1);
    return 0;
}