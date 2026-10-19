using System.Text;

namespace gflat.Tests;

public sealed class StandardAsciiComparisonTests
{
    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = [new("Ascii.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Ascii.gf"))), new("main.gf", program)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        Assert.DoesNotContain("__gflat_eh", ir);
        Assert.DoesNotContain("@malloc", ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-ascii-compare-" + Guid.NewGuid().ToString("N"));
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
    public void EveryBytePairMatchesUnsignedAsciiOnlyFolding(int optimization)
    {
        // High bytes deliberately do not undergo Unicode case folding.
        string oracle = string.Join("\n", Enumerable.Range(0, 256).Select(value =>
            $"folded[{value}] = (byte){(value is >= 'a' and <= 'z' ? value - 32 : value)};"));
        Run($$"""
            int main() nothrow {
                byte[256] folded;
                {{oracle}}
                for (int i = 0; i < 256; i++) {
                    for (int j = 0; j < 256; j++) {
                        char left = (char)i; char right = (char)j;
                        int expected = 0;
                        if (folded[i] < folded[j]) expected = -1;
                        if (folded[i] > folded[j]) expected = 1;
                        if (std::Ascii::CompareIgnoreCase(&left, 1, &right, 1) != expected) return 1;
                        if (std::Ascii::EqualsIgnoreCase(&left, 1, &right, 1) != (expected == 0)) return 2;
                    }
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAsciiSequencesMatchDotNetOrdinalIgnoreCase(int optimization)
    {
        var random = new Random(44321);
        var pairs = new List<(string Left, string Right)>
        {
            ("", ""), ("", "A"), ("a", ""), ("hello", "HELLO"),
            ("prefix", "prefix-more"), ("a\0b", "A\0B"), ("a\0a", "A\0B"),
            ("z", "["), ("_", "a"), ("A", "A\0"), ("\0", "\0"),
        };
        for (int i = 0; i < 100; i++)
        {
            string left = new(Enumerable.Range(0, random.Next(1, 25)).Select(_ => (char)random.Next(128)).ToArray());
            string right = i % 3 == 0 ? left.ToUpperInvariant() : new(Enumerable.Range(0, random.Next(25)).Select(_ => (char)random.Next(128)).ToArray());
            pairs.Add((left, right));
        }
        var code = new StringBuilder("int main() nothrow { char[24] left; char[24] right;\n");
        foreach (var (left, right) in pairs)
        {
            for (int i = 0; i < left.Length; i++) code.AppendLine($"left[{i}] = (char){(int)left[i]};");
            for (int i = 0; i < right.Length; i++) code.AppendLine($"right[{i}] = (char){(int)right[i]};");
            int expected = Math.Sign(string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            code.AppendLine($"if (std::Ascii::CompareIgnoreCase(left, {left.Length}, right, {right.Length}) != {expected}) return 3;");
            code.AppendLine($"if (std::Ascii::EqualsIgnoreCase(left, {left.Length}, right, {right.Length}) != {(expected == 0 ? "true" : "false")}) return 4;");
        }
        code.AppendLine("""
            readonly(char)*? absent = null;
            readonly(char)* nowhere = absent!;
            if (std::Ascii::CompareIgnoreCase(nowhere, 0, nowhere, 0) != 0) return 5;
            if (!std::Ascii::EqualsIgnoreCase(nowhere, 0, nowhere, 0)) return 6;
            if (std::Ascii::CompareIgnoreCase(nowhere, 0, left, 1) != -1) return 7;
            if (std::Ascii::CompareIgnoreCase(right, 1, nowhere, 0) != 1) return 8;
            if (std::Ascii::EqualsIgnoreCase(nowhere, 0, left, 1)) return 9;
            return 0;
            }
            """);
        Run(code.ToString(), optimization);
    }
}
