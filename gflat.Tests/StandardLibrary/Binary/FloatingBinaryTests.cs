using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace gflat.Tests.Std.Binary;

public sealed class FloatingBinaryTests
{
    [Fact]
    public void IntegerOnlyWindowsO0ObjectNeedsNoCrtFunctions()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        SourceFile[] sources = [
            new("BinaryPrimitives.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Binary/BinaryPrimitives.gf"))),
            new("main.gf", "int main() { byte[4] data; if (!std::Binary::BinaryPrimitives::TryWriteUInt32BigEndian(data, 4, 123u)) return 1; return 0; }")
        ];
        string directory = Path.Combine(Path.GetTempPath(), "gflat-ieee-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "integer.ll"), output = Path.Combine(directory, "integer.obj");
            File.WriteAllText(input, Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true }));
            var compiled = NativeToolchain.Run(NativeToolchain.FindClang(), ["-c", "-O0", input, "-o", output], 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            // Read COFF symbols directly; inspecting this ABI requirement must not
            // require an additional llvm-nm installation in the test environment.
            byte[] image = File.ReadAllBytes(output);
            Assert.Equal((ushort)0x8664, BinaryPrimitives.ReadUInt16LittleEndian(image));
            int start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(8)));
            int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(12)));
            for (int i = 0; i < count;)
            {
                int symbol = checked(start + i * 18);
                if (BinaryPrimitives.ReadInt16LittleEndian(image.AsSpan(symbol + 12)) == 0 && image[symbol + 16] == 2)
                {
                    string name;
                    if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(symbol)) == 0)
                    {
                        int text = checked(start + count * 18 + (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(symbol + 4)));
                        int end = Array.IndexOf(image, (byte)0, text);
                        name = Encoding.ASCII.GetString(image, text, end - text);
                    }
                    else name = Encoding.ASCII.GetString(image, symbol, 8).TrimEnd('\0');
                    // The ABI marker is supplied by startup, not a CRT function.
                    // Removing unused float definitions in future may eliminate it;
                    // that improvement should continue to pass this test.
                    Assert.Equal("_fltused", name);
                }
                i += 1 + image[symbol + 17]; // Skip auxiliary records.
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ulong[] Values(bool wide)
    {
        var values = new HashSet<ulong>(wide
            ? [0, 0x8000000000000000, 1, 0x000fffffffffffff, 0x0010000000000000,
                0x3ff0000000000000, 0xbff0000000000000, 0x7fefffffffffffff, 0xffefffffffffffff,
                0x7ff0000000000000, 0xfff0000000000000, 0x7ff8000000000000, 0xfff8000000000000,
                0x7ff0000000000001, 0xfff0000000000001, ulong.MaxValue]
            : [0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x3f800000, 0xbf800000, 0x7f7fffff,
                0xff7fffff, 0x7f800000, 0xff800000, 0x7fc00000, 0xffc00000, 0x7f800001, 0xff800001, uint.MaxValue]);
        var random = new Random(987221);
        for (int i = 0; i < 128; i++)
        {
            byte[] bytes = new byte[8]; random.NextBytes(bytes);
            ulong value = System.BitConverter.ToUInt64(bytes);
            values.Add(wide ? value : (uint)value);
        }
        return values.ToArray();
    }

    private static byte[] Encode(ulong bits, bool wide, bool little)
    {
        byte[] bytes = new byte[wide ? 8 : 4];
        if (wide)
        {
            double value = System.BitConverter.UInt64BitsToDouble(bits);
            if (little) BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
            else BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        }
        else
        {
            float value = System.BitConverter.UInt32BitsToSingle((uint)bits);
            if (little) BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
            else BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        }
        return bytes;
    }

    private static string Declarations(bool wide, bool little)
    {
        ulong[] values = Values(wide);
        string integer = wide ? "ulong" : "uint", suffix = wide ? "ul" : "u";
        byte[] bytes = values.SelectMany(value => Encode(value, wide, little)).ToArray();
        return $$"""
            {{integer}}[{{values.Length}}] values = [{{string.Join(",", values.Select(value => value + suffix))}}];
            byte[{{bytes.Length}}] expected = [{{string.Join(",", bytes.Select(value => "(byte)" + value))}}];
            """;
    }

    private static void Run(string program, int optimization, bool cursors = false)
    {
        string[] names = cursors ? ["Binary/BinaryPrimitives.gf", "Binary/BinaryBufferReader.gf", "Binary/BinaryBufferWriter.gf", "Memory/bytes.gf"] : ["Binary/BinaryPrimitives.gf"];
        SourceFile[] sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$Allocate", "gflat$Memory$Free", "@malloc", "@free" })
            Assert.DoesNotContain(forbidden, ir);
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnalignedPrimitiveReadsAndWritesMatchDotNetForEveryIeeeCategoryAndRandomBits(int optimization)
    {
        var code = new StringBuilder("using std; using std::Binary; int main() {\n");
        foreach (bool wide in new[] { false, true })
        foreach (bool little in new[] { false, true })
        {
            string type = wide ? "double" : "float", integer = wide ? "ulong" : "uint";
            string name = (wide ? "Double" : "Float") + (little ? "LittleEndian" : "BigEndian");
            int width = wide ? 8 : 4;
            code.AppendLine("{").AppendLine(Declarations(wide, little));
            code.AppendLine($$"""
                byte[10] data;
                for (nuint index = 0; index < {{Values(wide).Length}}; index++) {
                    {{integer}} bits = values[index];
                    {{type}} original = *(({{type}}*)&bits);
                    {{type}} decoded = ({{type}})1;
                    for (nuint i = 0; i < 10; i++) data[i] = (byte)165;
                    if (!BinaryPrimitives::TryWrite{{name}}(&data[1], 9, original)) return 1;
                    if (data[0] != (byte)165 || data[{{width + 1}}] != (byte)165) return 2;
                    for (nuint i = 0; i < {{width}}; i++) if (data[i + 1] != expected[index * {{width}} + i]) return 3;
                    if (!BinaryPrimitives::TryRead{{name}}(&data[1], {{width}}, ref decoded) || *(({{integer}}*)&decoded) != bits) return 4;
                    for (nuint count = 0; count < {{width}}; count++) {
                        decoded = original;
                        if (BinaryPrimitives::TryRead{{name}}(&data[1], count, ref decoded) || *(({{integer}}*)&decoded) != ({{integer}})0) return 5;
                        if (BinaryPrimitives::TryWrite{{name}}(&data[1], count, ({{type}})0)) return 6;
                        for (nuint i = 0; i < {{width}}; i++) if (data[i + 1] != expected[index * {{width}} + i]) return 7;
                        if (data[0] != (byte)165 || data[{{width + 1}}] != (byte)165) return 8;
                    }
                    // The aligned output value may occupy the input itself.
                    byte* sameStorage = (byte*)&decoded;
                    for (nuint i = 0; i < {{width}}; i++) sameStorage[i] = expected[index * {{width}} + i];
                    if (!BinaryPrimitives::TryRead{{name}}(sameStorage, {{width}}, ref decoded) || *(({{integer}}*)&decoded) != bits) return 9;
                }
                byte*? absent = null;
                {{type}} zero = ({{type}})1;
                if (BinaryPrimitives::TryRead{{name}}(absent!, 0, ref zero) || *(({{integer}}*)&zero) != ({{integer}})0) return 10;
                if (BinaryPrimitives::TryWrite{{name}}(absent!, 0, zero)) return 11;
                """);
            code.AppendLine("}");
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CursorsPreserveBitsAndOnlyAdvanceForCompleteValues(int optimization)
    {
        var code = new StringBuilder("using std; using std::Binary; int main() {\n");
        foreach (bool wide in new[] { false, true })
        foreach (bool little in new[] { false, true })
        {
            string type = wide ? "double" : "float", integer = wide ? "ulong" : "uint";
            string name = (wide ? "Double" : "Float") + (little ? "LittleEndian" : "BigEndian");
            int width = wide ? 8 : 4;
            code.AppendLine("{").AppendLine(Declarations(wide, little));
            code.AppendLine($$"""
                byte[10] data;
                for (nuint index = 0; index < {{Values(wide).Length}}; index++) {
                    {{integer}} bits = values[index];
                    {{type}} original = *(({{type}}*)&bits);
                    for (nuint i = 0; i < 10; i++) data[i] = (byte)165;
                    BinaryBufferWriter writer = new(data, {{width + 1}});
                    if (!writer.TrySkip(1) || !writer.TryWrite{{name}}(original) || writer.Position != {{width + 1}} || writer.Remaining != 0) return 1;
                    if (writer.TryWrite{{name}}(original) || writer.Position != {{width + 1}} || data[0] != (byte)165 || data[{{width + 1}}] != (byte)165) return 2;
                    for (nuint i = 0; i < {{width}}; i++) if (data[i + 1] != expected[index * {{width}} + i]) return 3;
                    BinaryBufferReader reader = new(data, {{width + 1}});
                    {{type}} decoded = ({{type}})1;
                    if (!reader.TrySkip(1) || !reader.TryRead{{name}}(ref decoded) || *(({{integer}}*)&decoded) != bits || reader.Position != {{width + 1}}) return 4;
                    for (nuint count = 0; count < {{width}}; count++) {
                        BinaryBufferReader shortReader = new(data, count + 1);
                        BinaryBufferWriter shortWriter = new(data, count + 1);
                        if (!shortReader.TrySkip(1) || !shortWriter.TrySkip(1)) return 5;
                        decoded = original;
                        if (shortReader.TryRead{{name}}(ref decoded) || *(({{integer}}*)&decoded) != ({{integer}})0 || shortReader.Position != 1) return 6;
                        if (shortWriter.TryWrite{{name}}(({{type}})0) || shortWriter.Position != 1) return 7;
                        for (nuint i = 0; i < {{width}}; i++) if (data[i + 1] != expected[index * {{width}} + i]) return 8;
                        if (data[0] != (byte)165 || data[{{width + 1}}] != (byte)165) return 9;
                    }
                }
                BinaryBufferReader emptyReader = new(); BinaryBufferWriter emptyWriter = new();
                {{type}} zero = ({{type}})1;
                if (emptyReader.TryRead{{name}}(ref zero) || *(({{integer}}*)&zero) != ({{integer}})0 || emptyReader.Position != 0) return 10;
                if (emptyWriter.TryWrite{{name}}(zero) || emptyWriter.Position != 0) return 11;
                """);
            code.AppendLine("}");
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization, cursors: true);
    }
}
