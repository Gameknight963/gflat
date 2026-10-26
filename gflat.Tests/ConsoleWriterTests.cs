namespace gflat.Tests;

public sealed class ConsoleWriterTests
{
    internal static SourceFile[] Sources(string program) => new[] {
        "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf", "core/Formatting.gf",
        "libc/Console/Console.gf", "libc/Console/Writer.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
        .Append(new SourceFile("main.gf", program)).ToArray();

    [Fact]
    public void WriterPreservesCountedBytesAndFormatsWithoutAnIntermediateAllocation()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::IO;
            extern int printf(immutable(char*) text, ...);
            extern void* malloc(nuint count);
            extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint count) { printf("allocation"); return malloc(count); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int main() {
                Console::Writer output = new Console::Writer();
                ITextWriter* writer = &output;
                writer.Write("x\0y", 3);
                writer.Write("", 0);
                int value = -42;
                value.Format(writer);
                true.Format(writer);
                Console::WriteLine();
                Console::Flush();
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("x\0y-42true\n", result.StandardOutput.Replace("\r\n", "\n"));
    }
}
