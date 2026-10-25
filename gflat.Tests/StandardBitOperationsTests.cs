using System.Numerics;

namespace gflat.Tests;

public class StandardBitOperationsTests
{
    private static readonly int[] Offsets = [int.MinValue, -129, -65, -64, -63, -33, -32, -31, -1, 0, 1, 7, 31, 32, 33, 63, 64, 65, int.MaxValue];

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(0x80000000u)]
    [InlineData(uint.MaxValue)]
    [InlineData(0x55555555u)]
    [InlineData(0x12345678u)]
    public void UIntValuesMatchDotNetCountsAndMaskedRotations(uint value)
    {
        string rotations = string.Join("\n", Offsets.Select(offset => $$"""
            if (std::Numerics::BitOperations::RotateLeft({{value}}u, {{Offset(offset)}}) != {{BitOperations.RotateLeft(value, offset)}}u) return 5;
            if (std::Numerics::BitOperations::RotateRight({{value}}u, {{Offset(offset)}}) != {{BitOperations.RotateRight(value, offset)}}u) return 6;
            """));
        Run($$"""
            int main() {
                if (std::Numerics::BitOperations::PopCount({{value}}u) != {{BitOperations.PopCount(value)}}) return 1;
                if (std::Numerics::BitOperations::LeadingZeroCount({{value}}u) != {{BitOperations.LeadingZeroCount(value)}}) return 2;
                if (std::Numerics::BitOperations::TrailingZeroCount({{value}}u) != {{BitOperations.TrailingZeroCount(value)}}) return 3;
                if (std::Numerics::BitOperations::IsPowerOfTwo({{value}}u) != {{BitOperations.IsPow2(value).ToString().ToLowerInvariant()}}) return 4;
                {{rotations}}
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(1ul)]
    [InlineData(2ul)]
    [InlineData(3ul)]
    [InlineData(0x8000000000000000ul)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x5555555555555555ul)]
    [InlineData(0x0123456789abcdeful)]
    public void ULongValuesMatchDotNetCountsAndMaskedRotations(ulong value)
    {
        string rotations = string.Join("\n", Offsets.Select(offset => $$"""
            if (std::Numerics::BitOperations::RotateLeft({{value}}ul, {{Offset(offset)}}) != {{BitOperations.RotateLeft(value, offset)}}ul) return 5;
            if (std::Numerics::BitOperations::RotateRight({{value}}ul, {{Offset(offset)}}) != {{BitOperations.RotateRight(value, offset)}}ul) return 6;
            """));
        Run($$"""
            int main() {
                if (std::Numerics::BitOperations::PopCount({{value}}ul) != {{BitOperations.PopCount(value)}}) return 1;
                if (std::Numerics::BitOperations::LeadingZeroCount({{value}}ul) != {{BitOperations.LeadingZeroCount(value)}}) return 2;
                if (std::Numerics::BitOperations::TrailingZeroCount({{value}}ul) != {{BitOperations.TrailingZeroCount(value)}}) return 3;
                if (std::Numerics::BitOperations::IsPowerOfTwo({{value}}ul) != {{BitOperations.IsPow2(value).ToString().ToLowerInvariant()}}) return 4;
                {{rotations}}
                return 0;
            }
            """);
    }

    private static string Offset(int value) => value == int.MinValue ? "-2147483647 - 1" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Run(string program)
    {
        var sources = new[] {
            new SourceFile("BitOperations.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Numerics/BitOperations.gf"))),
            new SourceFile("main.gf", program)
        };
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        Assert.DoesNotContain("gflat$Memory$", ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-bits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "bits.ll"), output = Path.Combine(directory, "bits.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(0, NativeToolchain.Run(output, []).ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}
