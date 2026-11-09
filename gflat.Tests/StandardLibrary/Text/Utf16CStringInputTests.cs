using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class Utf16CStringInputTests
{
    private const string Prelude = """
        using Standard;
        using Standard::Text;

        extern "C" void*? malloc(nuint count);
        extern "C" void free(void* storage);
        int allocations = 0;
        int releases = 0;
        bool deny = false;
        namespace Memory
        {
            public replace void*? Allocate(nuint count)
            {
                if (deny) return null;
                allocations++;
                return malloc(count);
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
    public void NullTerminatedConversionsMatchUtf8AndOwnTheirResult(int optimization)
    {
        string[] values = ["", "\0ignored", "A\0B", "\ufeff\u00e9", "\ud83d\ude42", "\udbff\udfff", new string('\u03a9', 100)];
        var code = new StringBuilder(Prelude + "\nint main()\n{\nushort[256] input;\n");
        foreach (string value in values)
        {
            int terminator = value.IndexOf('\0');
            string prefix = terminator < 0 ? value : value[..terminator];
            byte[] expected = new UTF8Encoding(false, true).GetBytes(prefix);
            for (int index = 0; index < value.Length; index++)
                code.AppendLine($"input[{index}] = (ushort){(int)value[index]};");
            code.AppendLine($"input[{value.Length}] = 0; input[{value.Length + 1}] = (ushort)0xd800;");
            code.AppendLine($$"""
                {
                    int before = allocations;
                    String text = UnicodeText::FromUtf16(input);
                    if (text.Length != {{expected.Length}} || allocations != before + {{(expected.Length == 0 ? 0 : 1)}}) return 1;
                    if (text.Data[text.Length] != '\0') return 2;
                """);
            if (prefix.Length != 0)
                code.AppendLine($"if (input[0] != (ushort){(int)prefix[0]}) return 3; input[0] = 0;");
            for (int index = 0; index < expected.Length; index++)
                code.AppendLine($"if ((byte)text.Data[{index}] != (byte){expected[index]}) return 4;");
            code.AppendLine("}");
        }
        code.AppendLine("if (allocations != releases) return 5; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidInputAndAllocationFailurePreserveAnExistingOwner(int optimization)
    {
        Run(Prelude + "\n" + """
            int main()
            {
                ushort[3] input = [(ushort)0xd800, 0, (ushort)0xdc00];
                {
                    String destination = s"unchanged";
                    readonly(char)* original = destination.Data;
                    try
                    {
                        destination = UnicodeText::FromUtf16(input);
                        return 1;
                    }
                    catch (ref Exception error) {}
                    if (destination.Data != original || !destination.Equals("unchanged")) return 2;
                    input[0] = (ushort)'x';
                    deny = true;
                    try
                    {
                        destination = UnicodeText::FromUtf16(input);
                        return 3;
                    }
                    catch (ref BadAllocException error) {}
                    deny = false;
                    if (destination.Data != original || !destination.Equals("unchanged")) return 4;
                    input[0] = 0;
                    deny = true;
                    String empty = UnicodeText::FromUtf16(input);
                    deny = false;
                    if (!empty.IsEmpty) return 5;
                }
                if (allocations != releases) return 6;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Text
            .Concat(StandardTestSources.Load("core/Binary/BinaryPrimitives.gf"))
            .Append(new SourceFile("utf16-cstring-input.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}
