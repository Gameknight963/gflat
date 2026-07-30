using gasm;

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
Dictionary<string, int> labels = new();

// pass 1: collect labels and their byte offsets
int byteOffset = 0;
for (int i = 0; i < lines.Length; i++)
{
    string line = StripLine(lines[i]);
    if (line.Length == 0) continue;

    if (line.EndsWith(':'))
    {
        string label = line[..^1];
        if (labels.ContainsKey(label))
        {
            Console.Error.WriteLine($"line {i + 1}: duplicate label '{label}'");
            Environment.Exit(1);
        }
        labels[label] = byteOffset;
        continue;
    }

    string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (!Enum.TryParse(parts[0], ignoreCase: true, out OpCode opCode))
    {
        Console.Error.WriteLine($"line {i + 1}: unknown instruction '{parts[0]}'");
        Environment.Exit(1);
    }

    byteOffset += InstructionSize(opCode);
}

// pass 2: emit bytecode
int currentByte = 0;
for (int i = 0; i < lines.Length; i++)
{
    string line = StripLine(lines[i]);
    if (line.Length == 0) continue;
    if (line.EndsWith(':')) continue; // skip label definitions

    string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    Enum.TryParse(parts[0], ignoreCase: true, out OpCode opCode);

    if (opCode == OpCode.HALT)
    {
        byte code = parts.Length >= 2 ? ParseByte(parts[1], i + 1) : (byte)0;
        bytecode.Add((byte)opCode);
        bytecode.Add(code);
        currentByte += 2;
    }
    else if (opCode == OpCode.PUSH)
    {
        if (parts.Length < 2)
        {
            Console.Error.WriteLine($"line {i + 1}: PUSH requires an operand");
            Environment.Exit(1);
        }
        int value = ParseInt(parts[1], i + 1);
        EmitInt32(bytecode, (byte)opCode, value);
        currentByte += 5;
    }
    else if (opCode == OpCode.JUMP || opCode == OpCode.CALL)
    {
        if (parts.Length < 2)
        {
            Console.Error.WriteLine($"line {i + 1}: {opCode} requires an operand");
            Environment.Exit(1);
        }
        int targetByte = ResolveOffset(parts[1], currentByte + 5, labels, i + 1);
        EmitInt32(bytecode, (byte)opCode, targetByte);
        currentByte += 5;
    }
    else
    {
        bytecode.Add((byte)opCode);
        currentByte++;
    }
}

File.WriteAllBytes(outputPath, bytecode.ToArray());
Console.WriteLine($"assembled {lines.Length} lines -> {outputPath} ({bytecode.Count} bytes)");

static string StripLine(string line)
{
    int commentIndex = line.IndexOf("//");
    if (commentIndex >= 0)
        line = line[..commentIndex];
    return line.Trim();
}

static int InstructionSize(OpCode opCode) => opCode switch
{
    OpCode.HALT => 2,
    OpCode.PUSH => 5,
    OpCode.JUMP => 5,
    OpCode.CALL => 5,
    _ => 1
};

static void EmitInt32(List<byte> bytecode, byte opCode, int value)
{
    bytecode.Add(opCode);
    bytecode.Add((byte)(value & 0xFF));
    bytecode.Add((byte)((value >> 8) & 0xFF));
    bytecode.Add((byte)((value >> 16) & 0xFF));
    bytecode.Add((byte)((value >> 24) & 0xFF));
}

static int ResolveOffset(string operand, int nextIp, Dictionary<string, int> labels, int line)
{
    // raw number, use as absolute byte offset
    if (int.TryParse(operand, out int absolute))
        return absolute - nextIp;

    // label, resolve to relative offset
    if (labels.TryGetValue(operand, out int target))
        return target - nextIp;

    Console.Error.WriteLine($"line {line}: undefined label '{operand}'");
    Environment.Exit(1);
    return 0;
}

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
    if (s.Length == 3 && s[0] == '\'' && s[2] == '\'')
        return s[1];
    if (int.TryParse(s, out int result))
        return result;
    Console.Error.WriteLine($"line {line}: invalid integer value '{s}'");
    Environment.Exit(1);
    return 0;
}