using System.Collections;
using System.Text;

namespace gflat.Tests.Std.Collections;

public sealed class BitSetRangeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WordRangeMutationsMatchBitArray(int optimization)
    {
        var random = new Random(91207);
        var source = new StringBuilder("""
            using std; using std::Queries; using std::Collections;
            extern int puts(immutable(char*) message);
            bool Matches(immutable(BitSet)* bits, readonly(uint)* words, nuint count) {
                if (bits.CountSetBits() != count) return false;
                for (nuint i = 0; i < bits.Length; i++) {
                    bool expected = (words[i / 32] & ((uint)1 << (int)(i % 32))) != (uint)0;
                    if ((*bits)[i] != expected) return false;
                }
                return true;
            }
            int main() { BitSet bits = new();
            """);
        int[] lengths = [0, 1, 2, 31, 32, 33, 63, 64, 65, 95, 96, 97, 127, 128, 129];
        foreach (int length in lengths)
        {
            var reference = new BitArray(length);
            source.AppendLine($"bits.Resize({length}); bits.Clear();");
            for (int step = 0; step < 24; step++)
            {
                int start = random.Next(length + 1), count = random.Next(length - start + 1), operation = random.Next(3);
                if (step == 0) { start = 0; count = length; operation = 1; }
                if (step == 1) { start = 0; count = length; operation = 2; }
                if (step == 2 && length >= 65) { start = 31; count = 34; operation = 1; }
                if (step == 3 && length >= 64) { start = 32; count = 32; operation = 2; }
                for (int i = start; i < start + count; i++)
                    reference[i] = operation == 2 ? !reference[i] : operation == 1;
                source.AppendLine(operation == 2 ? $"bits.FlipRange({start},{count});" : $"bits.SetRange({start},{count},{(operation == 1 ? "true" : "false")});");
                uint[] words = new uint[5]; int set = 0;
                for (int i = 0; i < length; i++) if (reference[i]) { words[i / 32] |= 1u << (i % 32); set++; }
                source.AppendLine("{ uint[5] expected = [" + string.Join(",", words.Select(word => "(uint)" + word)) + "];");
                source.AppendLine($"if (!Matches(&bits,(readonly(uint)*)expected,{set})) {{ puts(\"range length {length} step {step}\"); return 1; }} }}");
            }
        }
        source.AppendLine("return 0; }");
        Run(source.ToString(), optimization);
    }

    [Fact]
    public void InvalidRangesPreserveBitsAndValidRangesNeedNoAllocationAcrossMoves()
        => Run("""
            using std; using std::Queries; using std::Collections;
            int allocations = 0; int releases = 0; bool failAllocation = false;
            private extern void*? malloc(nuint bytes);
            private extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint bytes) { if (failAllocation) return null; allocations++; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            int main() {
                {
                    BitSet bits = new(65); bits.SetRange(31,34,true);
                    int caught = 0;
                    try { bits.SetRange(66,0,true); } catch { caught++; }
                    try { bits.FlipRange(66,0); } catch { caught++; }
                    try { bits.SetRange(65,1,false); } catch { caught++; }
                    try { bits.FlipRange(64,2); } catch { caught++; }
                    try { bits.SetRange(0,(nuint)0-(nuint)1,false); } catch { caught++; }
                    try { bits.FlipRange((nuint)0-(nuint)1,2); } catch { caught++; }
                    if (caught != 6 || bits.CountSetBits() != 34 || bits[30] || !bits[31] || !bits[64]) return 1;
                    bits.SetRange(65,0,true); bits.FlipRange(65,0);
                    BitSet moved = move bits;
                    int before = allocations; int freed = releases; failAllocation = true;
                    moved.FlipRange(32,32); moved.SetRange(31,1,false);
                    if (moved.Length != 65 || moved.CountSetBits() != 1 || !moved[64]) return 2;
                    if (allocations != before || releases != freed) return 3;
                    failAllocation = false;
                    bits = new BitSet(1); bits.SetRange(0,1,true);
                    if (!bits[0] || !moved[64]) return 4;
                    BitSet empty = new(); empty.SetRange(0,0,true); empty.FlipRange(0,0);
                }
                return allocations - releases;
            }
            """, 0);

    private static void Run(string program, int optimization)
    {
        var sources = new[] { "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing/primitive_hashing.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("ranges.gf", program));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_bit_ranges_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "ranges.ll"), executable = Path.Combine(directory, "ranges.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native bit ranges exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
