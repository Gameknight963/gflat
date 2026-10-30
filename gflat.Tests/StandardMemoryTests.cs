namespace gflat.Tests;

public sealed class StandardMemoryTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] { "Memory/bytes.gf" }
        .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", file))))
        .Append(new SourceFile("main.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyFillAndUnsignedComparisonHandleOffsetsAndZeroCounts(int optimization)
    {
        Run("""
            int main() {
                byte[64] aStorage; byte[64] bStorage;
                byte* a = aStorage; byte* b = bStorage;
                Memory::Fill((void*)a, (byte)170, 64);
                Memory::Fill((void*)b, (byte)85, 64);
                for (nuint i = 0; i < 32; i++) a[i + 3] = (byte)(i + 128);
                Memory::Copy((void*)(b + 7), (readonly(void)*)(a + 3), 32);
                if (b[6] != (byte)85 || b[39] != (byte)85) return 1;
                for (nuint i = 0; i < 32; i++) if (b[i + 7] != (byte)(i + 128)) return 2;
                if (!Memory::Equal((readonly(void)*)(a + 3), (readonly(void)*)(b + 7), 32)) return 3;
                Memory::Copy((void*)b, (readonly(void)*)a, 0);
                Memory::Fill((void*)b, (byte)255, 0);
                if (b[0] != (byte)85) return 4;
                if (!Memory::Equal((readonly(void)*)a, (readonly(void)*)b, 0)) return 5;
                if (Memory::Compare((readonly(void)*)a, (readonly(void)*)b, 0) != 0) return 6;
                a[0] = (byte)255; b[0] = (byte)127;
                if (Memory::Compare((readonly(void)*)a, (readonly(void)*)b, 1) != 128) return 7;
                if (Memory::Compare((readonly(void)*)b, (readonly(void)*)a, 1) != -128) return 8;
                a[0] = (byte)0; b[0] = (byte)255;
                if (Memory::Compare((readonly(void)*)a, (readonly(void)*)b, 1) != -255) return 9;
                if (Memory::Equal((readonly(void)*)a, (readonly(void)*)b, 1)) return 10;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MoveHandlesBothOverlapDirectionsAndEverySmallRange(int optimization)
    {
        Run("""
            int main() {
                byte[32] actualStorage; byte[32] expectedStorage;
                byte* actual = actualStorage; byte* expected = expectedStorage;
                for (nuint count = 0; count <= 16; count++) {
                    for (nuint source = 0; source <= 16; source++) {
                        for (nuint destination = 0; destination <= 16; destination++) {
                            for (nuint i = 0; i < 32; i++) { actual[i] = (byte)i; expected[i] = (byte)i; }
                            for (nuint i = 0; i < count; i++) expected[destination + i] = (byte)(source + i);
                            Memory::Move((void*)(actual + destination), (readonly(void)*)(actual + source), count);
                            if (!Memory::Equal((readonly(void)*)actual, (readonly(void)*)expected, 32)) return 1;
                        }
                    }
                }
                return 0;
            }
            """, optimization);
    }

    [Fact]
    public void ByteUtilitiesHaveNoSourceLevelAllocatorOrExceptionDependency()
    {
        string ir = Compiler.Emit(Sources("int main() => 0;"), options: new() { NoStdLib = true });
        Assert.DoesNotContain("@malloc", ir);
        Assert.DoesNotContain("@free", ir);
        Assert.DoesNotContain("gflat$Memory$Allocate", ir);
        Assert.DoesNotContain("__gflat_eh", ir);
        Assert.DoesNotContain("personality", ir);
    }

    private static void Run(string program, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_memory_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "memory.ll"), executable = Path.Combine(directory, "memory.exe");
            File.WriteAllText(ir, Compiler.Emit(Sources(program), options: new() { NoStdLib = true, InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.Equal(0, result.ExitCode);
        }
        finally { Directory.Delete(directory, true); }
    }
}
