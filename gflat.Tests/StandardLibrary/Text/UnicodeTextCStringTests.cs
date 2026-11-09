using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class UnicodeTextCStringTests
{
    private const string Prelude = """
        using Standard;
        using Standard::Collections;
        using Standard::Text;

        extern "C" void*? malloc(nuint count);
        extern "C" void free(void* storage);
        int allocations = 0;
        int releases = 0;
        int denied = 0;
        int failAfter = -1;
        namespace Memory
        {
            public replace void*? Allocate(nuint count)
            {
                if (failAfter == 0) { failAfter = -1; denied++; return null; }
                if (failAfter > 0) failAfter--;
                void*? storage = malloc(count);
                if (storage != null) allocations++;
                return storage;
            }
            public replace void Free(void* storage) nothrow
            {
                releases++;
                free(storage);
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TerminatedBuffersMatchUtf16CodeUnitsWithOneAllocation(int optimization)
    {
        var utf8 = new UTF8Encoding(false, true);
        List<string> values = ["", "\0", "A\0B", "\ufeff", "\u03a9\ud83d\ude42", "\udbff\udfff",
            new string(Enumerable.Range(0, 128).Select(value => (char)value).ToArray()), new string('\u03a9', 300)];
        var random = new Random(639124);
        for (int row = 0; row < 24; row++)
        {
            var text = new StringBuilder();
            for (int index = 0; index < 12; index++)
            {
                int scalar = random.Next(0x110000);
                if (scalar is >= 0xd800 and <= 0xdfff) scalar = 0;
                text.Append(char.ConvertFromUtf32(scalar));
            }
            values.Add(text.ToString());
        }
        var code = new StringBuilder(Prelude + "\nint main() { char[1024] input; ushort[512] expected;\n");
        foreach (string text in values)
        {
            byte[] bytes = utf8.GetBytes(text);
            for (int index = 0; index < bytes.Length; index++) code.AppendLine($"input[{index}] = (char){bytes[index]};");
            for (int index = 0; index < text.Length; index++) code.AppendLine($"expected[{index}] = (ushort){(int)text[index]};");
            code.AppendLine($$"""
                {
                    int before = allocations;
                    Vector<ushort> result = UnicodeText::ToUtf16CString(input, {{bytes.Length}});
                    if (allocations != before + 1 || result.Count != {{text.Length + 1}}) return 1;
                    for (nuint index = 0; index < {{text.Length}}; index++)
                        if (result[index] != expected[index]) return 2;
                    if (result[{{text.Length}}] != 0) return 3;
                }
                """);
            for (int index = 0; index < bytes.Length; index++) code.AppendLine($"if ((byte)input[{index}] != (byte){bytes[index]}) return 4;");
        }
        code.AppendLine("""
            {
                int before = allocations;
                Vector<ushort> empty = UnicodeText::ToUtf16CString(((nuint)1).reinterpret<readonly(char)*>(), 0);
                if (empty.Count != 1 || empty[0] != 0 || allocations != before + 1) return 5;
            }
            if (allocations != releases) return 6;
            return 0;
            }
            """);
        Run(code.ToString(), optimization, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidInputAndAllocationFailurePreserveTheOldOwner(int optimization)
        => Run(Prelude + """
            int main()
            {
                {
                    Vector<ushort> result = new();
                    result.Add((ushort)77);
                    ushort*? original = result.AsSpan().Data;
                    char[2] input = [(char)0xc0, (char)0xaf];
                    bool caught = false;
                    try { result = UnicodeText::ToUtf16CString(input, 2); }
                    catch (ref Exception error) { caught = true; }
                    if (!caught || result.Count != 1 || result[0] != 77 || result.AsSpan().Data != original) return 1;
                    input[0] = 'A';
                    for (int empty = 0; empty < 2; empty++)
                    {
                        int live = allocations - releases;
                        int deniedBefore = denied;
                        caught = false;
                        failAfter = 0;
                        try { result = UnicodeText::ToUtf16CString(input, (nuint)empty); }
                        catch (ref BadAllocException error) { caught = true; }
                        failAfter = -1;
                        if (!caught || denied != deniedBefore + 1 || allocations - releases != live) return 2;
                        if (result.Count != 1 || result[0] != 77 || result.AsSpan().Data != original) return 3;
                    }
                }
                if (allocations != releases) return 4;
                return 0;
            }
            """, optimization, 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeUtf16CStringReadersReceiveTheTerminatorForEmptyAndNonemptyText(int optimization)
        => Run("""
            #lang cpp
            int NativeLength(const char16_t* value) noexcept
            {
                int length = 0;
                while (value[length] != 0) ++length;
                return length;
            }
            #lang gflat
            """ + "\n" + Prelude + """
            int main()
            {
                {
                    char[7] input = ['A', (char)0xc3, (char)0xa9, (char)0xf0, (char)0x9f, (char)0x99, (char)0x82];
                    Vector<ushort> result = UnicodeText::ToUtf16CString(input, 7);
                    if (result.Count != 5 || NativeLength(result.AsSpan().Data) != 4) return 1;
                    Vector<ushort> empty = UnicodeText::ToUtf16CString(input, 0);
                    if (empty.Count != 1 || NativeLength(empty.AsSpan().Data) != 0) return 2;
                }
                if (allocations != releases) return 3;
                return 42;
            }
            """, optimization, 42);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeUtf16CStringResultsConvertWithoutAnExplicitLength(int optimization)
        => Run("""
            #lang cpp
            const char16_t* NativeText() noexcept { return u"A\u00e9\U0001f642"; }
            const char16_t* NativeEmpty() noexcept { return u""; }
            #lang gflat
            """ + "\n" + Prelude + """
            int main()
            {
                {
                    char[7] expected = ['A', (char)0xc3, (char)0xa9, (char)0xf0, (char)0x9f, (char)0x99, (char)0x82];
                    String text = UnicodeText::FromUtf16(NativeText()!);
                    if (text.Length != 7) return 1;
                    for (nuint index = 0; index < 7; index++)
                        if (text.Data[index] != expected[index]) return 2;
                    String empty = UnicodeText::FromUtf16(NativeEmpty()!);
                    if (!empty.IsEmpty) return 3;
                }
                if (allocations != releases) return 4;
                return 42;
            }
            """, optimization, 42);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Text
        .Concat(StandardTestSources.Load("core/Binary/BinaryPrimitives.gf"))
        .Append(new SourceFile("utf16-cstring.gf", code));

    private static void Run(string code, int optimization, int expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-utf16-cstring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new CompilationOptions { NoStdLib = true, InternalizeFunctions = true,
                ForeignCompilerResolver = () => NativeToolchain.FindClang(), ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(true) };
            var program = Compiler.EmitProgram(Sources(code), options: options);
            string ir = Path.Combine(root, "program.ll"), executable = Path.Combine(root, "program.exe");
            File.WriteAllText(ir, program.Ir);
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization, foreignUnits: program.ForeignUnits);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == expected, $"UTF-16 C string exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
