namespace gflat.Tests.Std.IO;

public sealed class FileWriterTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private static void Run(string directory, string program, bool strings = false)
    {
        var names = new List<string> { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf", "core/Text/BufferWriter.gf", "core/primitive_format.gf", "libc/IO/File.gf" };
        if (strings) names.AddRange(["core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf"]);
        var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files));
        var compiled = NativeToolchain.Compile(ir, exe);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(exe, [], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void FilesImplementTextWritingWithoutChangingLayoutOrAllocating()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "formatted.bin");
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                static class Calls { public static int Allocations; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                    public replace void Free(void* data) nothrow { }
                }
                class Label : IFormattable {
                    public Label() { }
                    public immutable void Format(ITextWriter* output) { output.Write("<tag>", 5); }
                }
                int main() {
                    if (sizeof(File) != sizeof(void*)) return 1;
                    File file = File::OpenWrite({{Literal(path)}});
                    ITextWriter* writer = &file;
                    writer.Write("A\0B", 3);
                    int value = -42; value.Format(&file); false.Format(writer);
                    Label label = new Label(); label.Format(&file);
                    file.WriteLine();
                    if (!file.IsOpen || Calls::Allocations != 0) return 2;
                    file.Close();
                    return 0;
                }
                """);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("A\0B-42false<tag>\n"), File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void CopyToCallerBufferWriterPreservesEveryByteWithoutAllocations()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "source.bin");
            File.WriteAllBytes(path, Enumerable.Range(0, 12005).Select(i => (byte)i).ToArray());
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                static class Calls { public static int Allocations; }
                namespace Memory {
                    public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                    public replace void Free(void* data) nothrow { }
                }
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    char[12006] storage; BufferWriter output = new BufferWriter(storage, 12006);
                    char[31] scratch;
                    if (input.CopyTo(&output, scratch, 31) != (nuint)12005) return 1;
                    if (output.Length != (nuint)12005 || output.Data[12005] != '\0') return 2;
                    for (nuint i = 0; i < output.Length; i++) if ((byte)output.Data[i] != (byte)i) return 3;
                    if (input.CopyTo(&output) != (nuint)0 || !input.IsOpen || Calls::Allocations != 0) return 4;
                    return 0;
                }
                """);
        });
    }

    [Fact]
    public void SelfInterfaceCopyAndInvalidScratchAreRejectedBeforeReading()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "source.bin");
            File.WriteAllText(path, "AB");
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    ITextWriter* self = &input;
                    try { input.CopyTo(self); return 1; } catch (IOException* error) { }
                    char[8] storage; BufferWriter output = new BufferWriter(storage, 8);
                    char[1] scratch;
                    try { input.CopyTo(&output, scratch, 0); return 2; } catch (IOException* error) { }
                    if (input.ReadByte() != 65 || output.Length != (nuint)0) return 3;
                    input.Close();
                    try { input.CopyTo(&output); return 4; } catch (IOException* error) { }
                    try { self.Write("", 0); return 5; } catch (IOException* error) { }
                    return 0;
                }
                """);
        });
    }

    [Fact]
    public void ThrowingSinkLeavesInputOpenAfterConsumedChunk()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "source.bin");
            File.WriteAllBytes(path, Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                struct Reject : ITextWriter {
                    public nuint Count;
                    public Reject() { Count = 0; }
                    public void Write(readonly(char)* data, nuint count) { Count = count; throw new Exception("sink rejected bytes"); }
                }
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    Reject output = new Reject();
                    char[17] scratch;
                    try { input.CopyTo(&output, scratch, 17); return 1; } catch (Exception* error) { }
                    if (!input.IsOpen || output.Count != (nuint)17 || input.ReadByte() != 17) return 2;
                    return 0;
                }
                """);
        });
    }

    [Fact]
    public void OwningStringSinkAllocationFailurePreservesExistingString()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "source.bin");
            File.WriteAllBytes(path, Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                extern void*? malloc(nuint size);
                extern void free(void* data);
                namespace Memory {
                    public replace void*? Allocate(nuint size) {
                        if (size == (nuint)4101) return null;
                        return malloc(size);
                    }
                    public replace void Free(void* data) nothrow { free(data); }
                }
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    String output = s"kept";
                    readonly(char)* original = output.Data;
                    try { input.CopyTo(&output); return 1; } catch (BadAllocException* error) { }
                    if (output.Data != original || output.Length != (nuint)4 || output.Data[0] != 'k' || output.Data[3] != 't') return 2;
                    if (!input.IsOpen || input.ReadByte() != 0) return 3;
                    return 0;
                }
                """, strings: true);
        });
    }

    [Fact]
    public void EmptyInputDoesNotCallTheSink()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "empty.bin");
            File.WriteAllBytes(path, []);
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                struct Reject : ITextWriter {
                    public void Write(readonly(char)* data, nuint count) { throw new Exception("unexpected sink call"); }
                }
                int main() {
                    File input = File::OpenRead({{Literal(path)}});
                    Reject output = new Reject();
                    if (input.CopyTo(&output) != (nuint)0 || !input.IsOpen) return 1;
                    return 0;
                }
                """);
        });
    }
}
