using gflat.ast;

namespace gflat.Tests.Std.IO;

public sealed class ByteReaderReadByteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryByteAndRepeatedEofMatchHostValuesAcrossBorrowedCallForms(int optimization)
        => Run("""
            using Standard::IO;
            int allocations = 0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { allocations++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            struct Source : IByteReader {
                private readonly(char)* data;
                public nuint Position; public nuint Calls; public nuint Maximum;
                public Source(readonly(char)* input) { data = input; Position = 0; Calls = 0; Maximum = 0; }
                public nuint Read(char* output, nuint count) {
                    Calls++; if (count > Maximum) Maximum = count;
                    if (Position == 256 || count == 0) return 0;
                    output[0] = data[Position]; Position++; return 1;
                }
            }
            int main() {
                char[256] bytes; for (nuint i = 0; i < 256; i++) bytes[i] = (char)i;
                Source direct = new(bytes); Source value = new(bytes);
                Source pointed = new(bytes); Source indirect = new(bytes);
                Source* pointer = &pointed; IByteReader* input = &indirect;
                for (int expected = 0; expected < 256; expected++) {
                    if (ReadByte(&direct) != expected || value.ReadByte() != expected) return 1;
                    if (pointer.ReadByte() != expected || input.ReadByte() != expected) return 2;
                }
                for (int attempt = 0; attempt < 3; attempt++) {
                    if (ReadByte(&direct) != -1 || value.ReadByte() != -1 || pointer.ReadByte() != -1 || input.ReadByte() != -1) return 3;
                }
                if (direct.Position != 256 || value.Position != 256 || pointed.Position != 256 || indirect.Position != 256) return 4;
                if (direct.Maximum > 1 || value.Maximum > 1 || pointed.Maximum > 1 || indirect.Maximum > 1) return 5;
                return allocations;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverReportsAreRejectedBeforeNewlineOrByte255CanBecomeAResult(int optimization)
        => Run("""
            using Standard::IO;
            struct Malformed : IByteReader {
                public int Mode; public nuint Calls; public nuint Requested;
                public Malformed(int mode) { Mode = mode; Calls = 0; Requested = 0; }
                public nuint Read(char* data, nuint count) {
                    Calls++; Requested = count;
                    if (Mode == 0) { data[0] = '\n'; return 2; }
                    data[0] = (char)255; return (nuint)0 - (nuint)1;
                }
            }
            int main() {
                for (int mode = 0; mode < 2; mode++) {
                    Malformed source = new(mode); IByteReader* input = &source;
                    int result = 1234; bool caught = false;
                    try { result = input.ReadByte(); }
                    catch (ref IOException error) { caught = true; }
                    if (!caught || result != 1234 || source.Calls != 1 || source.Requested != 1) return 10;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SourceExceptionTypeAndCodePropagateAfterPossibleConsumption(int optimization)
        => Run("""
            using Standard::IO;
            class InputFailure : IOException { public InputFailure() : base("read byte failed") { code = 81; } }
            struct Source : IByteReader {
                public nuint Position;
                public Source() { Position = 0; }
                public nuint Read(char* data, nuint count) {
                    data[0] = (char)255; Position++; throw new InputFailure();
                }
            }
            int main() {
                Source source = new(); int result = 1234; bool caught = false;
                try { result = ReadByte(&source); }
                catch (ref InputFailure error) { caught = true; if (error.GetCode() != 81) return 20; }
                if (!caught || result != 1234 || source.Position != 1) return 21;
                return 0;
            }
            """, optimization);

    [Fact]
    public void ExistingFileReadByteRemainsTheSelectedInstanceMember()
    {
        var source = new SourceFile("file-read-byte-selection.gf", "using Standard::IO; int Probe(File* input) => input.ReadByte();");
        var snapshot = new AnalysisSnapshot(Sources(source));
        var call = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<CallExpression>(),
            candidate => candidate.Span.SourceId == source.Id && candidate.Callee is MemberAccessExpression { Member: "ReadByte" });
        Assert.NotNull(snapshot.Checker);
        Assert.Null(snapshot.Checker.GetExtensionCall(call));
        var selected = Assert.IsType<MethodDeclaration>(snapshot.Checker.GetResolvedCall(call));
        Assert.Equal("ReadByte", selected.Name);
        Assert.Null(selected.ExtensionReceiverType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FileOwnMemberInterfaceExtensionAndOrdinaryHelperConsumeOneSharedCursor(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-read-byte-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool passed = false;
        try
        {
            string path = Path.Combine(directory, "bytes.bin");
            File.WriteAllBytes(path, [0, 255, 10]);
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            Run($$"""
                using Standard::IO;
                int main() {
                    File file = File::OpenRead({{literal}}); IByteReader* input = &file;
                    if (file.ReadByte() != 0 || input.ReadByte() != 255 || ReadByte(&file) != 10) return 30;
                    if (file.ReadByte() != -1 || input.ReadByte() != -1 || !file.IsOpen) return 31;
                    file.Close(); return 0;
                }
                """, optimization);
            passed = true;
        }
        catch (Exception error) { error.Data["ReadByteDirectory"] = directory; throw; }
        finally { if (passed) Directory.Delete(directory, recursive: true); }
    }

    private static IEnumerable<SourceFile> Sources(SourceFile program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).Append(program);

    private static void Run(string code, int optimization)
    {
        string ir = Compiler.Emit(Sources(new SourceFile("read-byte-tests.gf", code)), options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
