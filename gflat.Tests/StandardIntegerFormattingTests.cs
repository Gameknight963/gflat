using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class StandardIntegerFormattingTests
{
    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = [new("IntegerFormatting.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/IntegerFormatting.gf"))), new("main.gf", program)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        foreach (string forbidden in new[] { "__gflat_eh", "@malloc", "@free", "gflat$Memory$", "ITextWriter" })
            Assert.DoesNotContain(forbidden, ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-integer-format-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "program.ll"), output = Path.Combine(directory, "program.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllIntegerWidthsMatchInvariantDecimalWithAtomicCapacityFailures(int optimization)
    {
        (string Type, int Bits, bool Signed)[] widths = [
            ("sbyte", 8, true), ("byte", 8, false), ("short", 16, true), ("ushort", 16, false),
            ("int", 32, true), ("uint", 32, false), ("long", 64, true), ("ulong", 64, false),
            ("nint", IntPtr.Size * 8, true), ("nuint", IntPtr.Size * 8, false)];
        var random = new Random(774319);
        var code = new StringBuilder("int main() nothrow { char[23] output; nuint written = 99;\n");
        foreach (var (type, bits, signed) in widths)
        {
            ulong mask = bits == 64 ? ulong.MaxValue : (1ul << bits) - 1;
            ulong signBit = 1ul << (bits - 1);
            var values = new List<ulong> { 0, 1, 9, 10, 99, 100, mask, signBit, signBit - 1 };
            for (int i = 0; i < 20; i++) values.Add(BitConverter.ToUInt64(RandomBytes(random)) & mask);
            ulong[] distinct = values.Distinct().ToArray();
            string[] expected = distinct.Select(raw => signed
                    ? unchecked((long)(raw << (64 - bits)) >> (64 - bits)).ToString(CultureInfo.InvariantCulture)
                    : raw.ToString(CultureInfo.InvariantCulture)).ToArray();
            code.AppendLine($$"""
                {
                    {{type}}[{{distinct.Length}}] values = [{{string.Join(",", distinct.Select(value => $"({type}){value}ul"))}}];
                    readonly(char)*[{{distinct.Length}}] expected = [{{string.Join(",", expected.Select(text => $"\"{text}\""))}}];
                    nuint[{{distinct.Length}}] lengths = [{{string.Join(",", expected.Select(text => $"(nuint){text.Length}"))}}];
                    for (nuint test = 0; test < (nuint){{distinct.Length}}; test++) {
                    for (nuint i = 0; i < 23; i++) output[i] = 'Z';
                    for (nuint capacity = 0; capacity < lengths[test]; capacity++) {
                        written = 99;
                        if (std::IntegerFormatting::TryFormat(values[test], &output[1], capacity, ref written) || written != 0) return 1;
                        for (nuint i = 0; i < 23; i++) if (output[i] != 'Z') return 2;
                    }
                    if (!std::IntegerFormatting::TryFormat(values[test], &output[1], lengths[test], ref written) || written != lengths[test]) return 3;
                    if (output[0] != 'Z') return 4;
                    for (nuint i = 0; i < written; i++) if (output[i + 1] != expected[test][i]) return 5;
                    for (nuint i = written + 1; i < 23; i++) if (output[i] != 'Z') return 6;
                    if (!std::IntegerFormatting::TryFormat(values[test], &output[1], 21, ref written) || written != lengths[test]) return 8;
                    for (nuint i = written + 1; i < 23; i++) if (output[i] != 'Z') return 9;
                    }
                }
                """);
        }
        code.AppendLine("""
            char*? absent = null;
            if (std::IntegerFormatting::TryFormat(0, absent!, 0, ref written) || written != 0) return 7;
            return 0;
            }
            """);
        Run(code.ToString(), optimization);
    }

    private static byte[] RandomBytes(Random random)
    {
        var bytes = new byte[8]; random.NextBytes(bytes); return bytes;
    }
}
