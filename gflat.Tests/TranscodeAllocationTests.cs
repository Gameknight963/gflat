using System.Text;

namespace gflat.Tests;

public sealed class TranscodeAllocationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConversionAllocatesOnlyItsResultAndPreservesBorrowedInputs(int optimization)
    {
        var utf8 = new UTF8Encoding(false, true);
        var utf16 = new UnicodeEncoding(false, false, true);
        var code = new StringBuilder("""
            using std; using std::Collections;
            extern void*? malloc(nuint bytes); extern void free(void* storage);
            int allocations = 0; int releases = 0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    void*? result = malloc(bytes);
                    if (result != null) allocations++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            int main() {
            """);
        foreach (string text in new[] { "", "ASCII", "A\0\u20ac\ud83d\ude00", "\ufeff\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\ud800\udc00\udbff\udfff" })
        {
            byte[] input = utf8.GetBytes(text), expected = utf16.GetBytes(text);
            string original = string.Join(", ", input.DefaultIfEmpty((byte)0).Select(value => $"(byte){value}"));
            string wide = string.Join(", ", expected.DefaultIfEmpty((byte)0).Select(value => $"(byte){value}"));
            code.AppendLine($$"""
                {
                    byte[{{Math.Max(1, input.Length)}}] original = [{{original}}];
                    byte[{{Math.Max(1, expected.Length)}}] expected = [{{wide}}];
                    Vector<byte> source = new(); source.Reserve({{input.Length}});
                    for (nuint i = 0; i < {{input.Length}}; i++) source.Add(original[i]);
                    immutable(byte)*? identity = source.AsImmutable().Data;
                    int before = allocations;
                    Vector<byte> encoded = ToUtf16Le(ref source);
                    if (allocations - before != {{(input.Length == 0 ? 0 : 1)}} || encoded.Count != {{expected.Length}}) return 1;
                    if (source.AsImmutable().Data != identity || source.Count != {{input.Length}}) return 2;
                    for (nuint i = 0; i < {{input.Length}}; i++) if (source[i] != original[i]) return 3;
                    for (nuint i = 0; i < {{expected.Length}}; i++) if (encoded[i] != expected[i]) return 4;
                    immutable(byte)*? encodedIdentity = encoded.AsImmutable().Data;
                    before = allocations;
                    Vector<byte> decoded = FromUtf16Le(ref encoded);
                    if (allocations - before != {{(input.Length == 0 ? 0 : 1)}} || decoded.Count != {{input.Length}}) return 5;
                    if (encoded.AsImmutable().Data != encodedIdentity || encoded.Count != {{expected.Length}}) return 6;
                    for (nuint i = 0; i < {{input.Length}}; i++) if (decoded[i] != original[i]) return 7;
                    for (nuint i = 0; i < {{expected.Length}}; i++) if (encoded[i] != expected[i]) return 8;
                }
                """);
        }
        code.AppendLine("if (allocations != releases) return 9; return 0; }");
        string tool = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/transcode.gf"));
        const string entry = "int main(int argc,char** argv)";
        Assert.Equal(1, tool.Split(entry).Length - 1);
        // Keep the actual conversion functions; only rename the CLI entry point.
        tool = tool.Replace(entry, "int TranscodeToolMain(int argc,char** argv)");
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("transcode.gf", tool))
            .Append(new SourceFile("allocations.gf", code.ToString()));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}