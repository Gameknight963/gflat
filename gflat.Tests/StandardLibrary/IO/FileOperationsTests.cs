namespace gflat.Tests.Std.IO;

public sealed class FileOperationsTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string directory, string program, bool text = false)
    {
        var names = new List<string> { "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "libc/IO/File.gf" };
        if (text) names.AddRange(["core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf", "libc/IO/FileText.gf"]);
        var files = names
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        string ir = Path.Combine(directory, "program.ll"), exe = Path.Combine(directory, "program.exe");
        File.WriteAllText(ir, Compiler.Emit(files));
        var compiled = NativeToolchain.Compile(ir, exe);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(exe, [], 30000);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-file-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CopyUsesCurrentPositionAndPreservesBinaryBytesAcrossChunks()
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "source.bin"), target = Path.Combine(directory, "target.bin");
            byte[] bytes = Enumerable.Range(0, 13007).Select(i => (byte)(i * 37)).ToArray();
            File.WriteAllBytes(source, bytes);
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    File input = File::OpenRead({{Literal(source)}});
                    File output = File::OpenWrite({{Literal(target)}});
                    char[7] prefix;
                    input.ReadExactly(prefix, 7);
                    char[13] buffer;
                    if (input.CopyTo(&output, buffer, 13) != (nuint)13000) return 1;
                    if (input.CopyTo(&output) != (nuint)0) return 2;
                    output.Close();
                    return 0;
                }
                """);
            Assert.Equal(bytes[7..], File.ReadAllBytes(target));
        });
    }

    [Fact]
    public void StackCopyAndWriteLinesPreserveZerosAndUseLfOnAllPlatforms()
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "source.bin"), target = Path.Combine(directory, "target.bin");
            byte[] bytes = Enumerable.Range(0, 9001).Select(i => (byte)i).ToArray();
            File.WriteAllBytes(source, bytes);
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    File input = File::OpenRead({{Literal(source)}});
                    File output = File::OpenWrite({{Literal(target)}});
                    if (input.CopyTo(&output) != (nuint)9001) return 1;
                    output.WriteLine(); output.WriteLine("a\0ignored"); output.WriteLine("b\0c", 3);
                    output.Close();
                    return 0;
                }
                """);
            Assert.Equal(bytes.Concat(new byte[] { 10, 97, 10, 98, 0, 99, 10 }), File.ReadAllBytes(target));
        });
    }

    [Fact]
    public void ExactReadConsumesTruncatedPrefixAndReportsEndOfFile()
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "short.bin");
            File.WriteAllBytes(source, [65, 0, 66]);
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    File input = File::OpenRead({{Literal(source)}});
                    char[8] buffer;
                    input.ReadExactly(buffer, 0);
                    try { input.ReadExactly(buffer, 8); return 1; } catch (EndOfStreamException* error) { }
                    if (buffer[0] != 'A' || buffer[1] != '\0' || buffer[2] != 'B') return 2;
                    if (input.Read(buffer, 1) != (nuint)0) return 3;
                    try { input.ReadExactly(buffer, 1); return 5; } catch (IOException* error) { }
                    input.Close();
                    try { input.ReadExactly(buffer, 0); return 4; }
                    catch (EndOfStreamException* error) { return 6; }
                    catch (IOException* error) { }
                    return 0;
                }
                """);
        });
    }

    [Fact]
    public void InvalidCopyDoesNotConsumeInputAndRenameRemoveReportFailures()
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "source.bin"), renamed = Path.Combine(directory, "renamed.bin");
            File.WriteAllText(source, "abc");
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    File input = File::OpenRead({{Literal(source)}});
                    File closed = new File(); char[1] buffer;
                    try { input.CopyTo(&closed); return 1; } catch (IOException* error) { }
                    try { input.CopyTo(&input); return 2; } catch (IOException* error) { }
                    File output = File::OpenWrite({{Literal(renamed)}});
                    try { input.CopyTo(&output, buffer, 0); return 3; } catch (IOException* error) { }
                    input.ReadExactly(buffer, 1); if (buffer[0] != 'a') return 4;
                    output.Close(); input.Close();
                    File::Remove({{Literal(renamed)}});
                    File::Rename({{Literal(source)}}, {{Literal(renamed)}});
                    File::Remove({{Literal(renamed)}});
                    try { File::Remove({{Literal(renamed)}}); return 5; } catch (IOException* error) { }
                    try { File::Rename({{Literal(source)}}, {{Literal(renamed)}}); return 6; } catch (IOException* error) { }
                    return 0;
                }
                """);
            Assert.False(File.Exists(source));
            Assert.False(File.Exists(renamed));
        });
    }

    [Fact]
    public void WholeFileStringsPreserveBinaryBytesAndOwnReturnedStorage()
    {
        WithDirectory(directory =>
        {
            string source = Path.Combine(directory, "source.bin"), target = Path.Combine(directory, "target.bin");
            byte[] bytes = Enumerable.Range(0, 12001).Select(i => (byte)(i * 17)).ToArray();
            File.WriteAllBytes(source, bytes);
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    String text = FileText::ReadAll({{Literal(source)}});
                    if (text.Length != (nuint)12001 || text.Data[12001] != '\0') return 1;
                    FileText::WriteAll({{Literal(target)}}, ref text);
                    FileText::AppendAll({{Literal(target)}}, "\0tail", 5);
                    FileText::AppendAll({{Literal(target)}}, "!");
                    String suffix = s"?\0.";
                    FileText::AppendAll({{Literal(target)}}, ref suffix);
                    return 0;
                }
                """, text: true);
            Assert.Equal(bytes.Concat(new byte[] { 0, 116, 97, 105, 108, 33, 63, 0, 46 }), File.ReadAllBytes(target));
        });
    }

    [Fact]
    public void WholeFileWritesTruncateAndEmptyStringsRemainValid()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "empty.txt");
            File.WriteAllText(path, "old content");
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    FileText::WriteAll({{Literal(path)}}, "new\0ignored");
                    String first = FileText::ReadAll({{Literal(path)}});
                    if (first.Length != (nuint)3) return 1;
                    FileText::WriteAll({{Literal(path)}}, "ignored", 0);
                    String empty = FileText::ReadAll({{Literal(path)}});
                    if (empty.Length != (nuint)0 || empty.Data[0] != '\0') return 2;
                    return 0;
                }
                """, text: true);
            Assert.Empty(File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void WholeFileReadClosesDuringAllocationFailure()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "allocation-failure.txt");
            File.WriteAllText(path, "content requiring an owning buffer");
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                namespace Memory {
                    public replace void*? Allocate(nuint size) { return null; }
                    public replace void Free(void* storage) nothrow { }
                }
                int main() {
                    try { String value = FileText::ReadAll({{Literal(path)}}); return 1; }
                    catch (BadAllocException* error) { }
                    File::Remove({{Literal(path)}});
                    return 0;
                }
                """, text: true);
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void ByteOperationsDistinguishAllByteValuesFromEndOfFile()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "bytes.bin");
            Run(directory, $$"""
                using std; using std::Text; using std::IO;
                int main() {
                    { File output = File::OpenWrite({{Literal(path)}});
                      output.WriteByte('\0'); output.WriteByte((char)255); output.WriteByte('Z'); output.Close(); }
                    File input = File::OpenRead({{Literal(path)}});
                    if (input.ReadByte() != 0 || input.ReadByte() != 255 || input.ReadByte() != 90) return 1;
                    if (input.ReadByte() != -1 || input.ReadByte() != -1) return 2;
                    input.Close();
                    try { input.ReadByte(); return 3; } catch (IOException* error) { }
                    try { input.WriteByte('x'); return 4; } catch (IOException* error) { }
                    return 0;
                }
                """);
            Assert.Equal(new byte[] { 0, 255, 90 }, File.ReadAllBytes(path));
        });
    }
}
