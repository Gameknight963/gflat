using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class StringCustomTrimTests
{
    private const string Imports = "using Standard::Text;\n\n";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExplicitNonemptyByteSetsMatchDotNetTrimming(int optimization)
    {
        (string Text, string Set)[] cases = [
            ("", "x"), ("plain", "x"), ("xxx", "x"), ("xxmiddlexx", "x"),
            (";,,value,;", ";,"), ("\0\0middle\0", "\0"), ("\u00ff\u0080A\u0080\u00ff", "\u0080\u00ff"),
            (" a b ", " "), ("abcabc", "abc"), ("\"'quoted'\"", "'\"")];
        var globals = new StringBuilder(Imports);
        var body = new StringBuilder("int main() {\n");
        int id = 0;
        foreach (var item in cases)
        {
            foreach (string operation in new[] { "Trim", "TrimStart", "TrimEnd" })
            {
                string result = operation switch { "Trim" => item.Text.Trim(item.Set.ToCharArray()),
                    "TrimStart" => item.Text.TrimStart(item.Set.ToCharArray()), _ => item.Text.TrimEnd(item.Set.ToCharArray()) };
                byte[] source = Encoding.Latin1.GetBytes(item.Text), set = Encoding.Latin1.GetBytes(item.Set);
                byte[] expected = Encoding.Latin1.GetBytes(result);
                globals.Append(Bytes("Source" + id, source));
                globals.Append(Bytes("Set" + id, set));
                globals.Append(Bytes("Expected" + id, expected));
                body.AppendLine($$"""
                    {
                        readonly(byte)* source = Source{{id}};
                        readonly(byte)* set = Set{{id}};
                        String text = String::From(source.reinterpret<readonly(char)*>(), {{source.Length}});
                        readonly(char)* original = text.Data;
                        nuint capacity = text.Capacity;
                        text.{{operation}}(set.reinterpret<readonly(char)*>(), {{set.Length}});
                        if (text.Length != {{expected.Length}} || text.Data != original || text.Capacity != capacity) return 1;
                        for (nuint index = 0; index < text.Length; index++)
                            if ((byte)text.Data[index] != Expected{{id}}[index]) return 2;
                        if (text.Data[text.Length] != '\0') return 3;
                    }
                    """);
                id++;
            }
        }
        body.AppendLine("return 0; }");
        Run(globals.Append(body).ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SingleBytesEmptySetsAndSelfBorrowedSetsDoNotAllocateOrInvalidateStorage(int optimization)
        => Run(Imports + """
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* data);
            static class State { public static bool Failing; public static int Calls; public static int Live; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    State::Calls++;
                    if (State::Failing) return null;
                    void*? result = malloc(bytes);
                    if (result != null) State::Live++;
                    return result;
                }
                public replace void Free(void* data) nothrow { State::Live--; free(data); }
            }
            int main() {
                {
                    String both = String::From("aaXYZaa");
                    String endingSet = String::From("bXab");
                    String shiftedSet = String::From("aabc");
                    String leading = String::From("xx middlexx");
                    String trailing = String::From("xxmiddle xx");
                    char[5] zeros = ['\0', '\0', 'A', '\0', '\0'];
                    String zero = String::From(zeros, 5);
                    char[3] highBytes = [(char)255, 'B', (char)255];
                    String high = String::From(highBytes, 3);
                    String unchanged = String::From("  keep  ");
                    String empty = new();
                    readonly(char)* original = both.Data;
                    nuint capacity = both.Capacity;
                    int calls = State::Calls;
                    State::Failing = true;
                    both.Trim(both.Data, 2);
                    endingSet.Trim(endingSet.Data + 3, 1);
                    shiftedSet.TrimStart(shiftedSet.Data, 2);
                    leading.TrimStart('x');
                    trailing.TrimEnd('x');
                    zero.Trim('\0');
                    high.Trim((char)255);
                    unchanged.Trim(unchanged.Data, 0);
                    unchanged.TrimStart(unchanged.Data, 0);
                    unchanged.TrimEnd(unchanged.Data, 0);
                    empty.Trim('x');
                    empty.Trim("x", 1);
                    State::Failing = false;
                    if (State::Calls != calls) return 1;
                    if (!both.Equals("XYZ") || both.Data != original || both.Capacity != capacity) return 2;
                    if (!endingSet.Equals("Xa") || !shiftedSet.Equals("bc") || !leading.Equals(" middlexx") || !trailing.Equals("xxmiddle ")) return 3;
                    if (!zero.Equals("A") || !high.Equals("B") || !unchanged.Equals("  keep  ") || !empty.IsEmpty) return 4;
                }
                for (int mode = 0; mode < 3; mode++) {
                    String all = String::From("xxxx");
                    String none = String::From("value");
                    String empty = new();
                    readonly(char)* allData = all.Data;
                    readonly(char)* noneData = none.Data;
                    nuint allCapacity = all.Capacity;
                    nuint noneCapacity = none.Capacity;
                    int calls = State::Calls;
                    State::Failing = true;
                    if (mode == 0) { all.Trim('x'); none.Trim('x'); empty.Trim('x'); }
                    else if (mode == 1) { all.TrimStart('x'); none.TrimStart('x'); empty.TrimStart('x'); }
                    else { all.TrimEnd('x'); none.TrimEnd('x'); empty.TrimEnd('x'); }
                    State::Failing = false;
                    if (State::Calls != calls || !all.IsEmpty || all.Data[0] != '\0' || !none.Equals("value") || !empty.IsEmpty) return 6;
                    if (all.Data != allData || all.Capacity != allCapacity || none.Data != noneData || none.Capacity != noneCapacity) return 7;
                }
                if (State::Live != 0) return 5;
                return 0;
            }
            """, optimization);

    private static string Bytes(string name, byte[] bytes)
        => $"const byte[{bytes.Length}] {name} = [" + string.Join(",", bytes.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "];\n";

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/IStringConvertable.gf", "core/IHashable.gf",
            "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/primitive_format.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("string-custom-trim.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
