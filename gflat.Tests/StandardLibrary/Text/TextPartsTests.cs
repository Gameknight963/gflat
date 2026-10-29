using gflat.CompileExceptions;

namespace gflat.Tests.Std.Text;

public sealed class TextPartsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProtectedSequencesAndCurrentObserversRetainIndependentBorrowedViews(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* data) nothrow{}
            }
            int main(){
                char[3] bytes=['a',',','b'];TextParts parts=new(new Span<readonly(char)>(bytes,3),',');
                readonly(TextParts)* shallow=&parts;immutable(TextParts)* deep=&parts;
                TextParts::Enumerator first=shallow.GetEnumerator();TextParts::Enumerator second=deep.GetEnumerator();
                var query=deep.AsEnumerable();var otherQuery=shallow.AsEnumerable();
                TextParts::Enumerator lazy=query.GetEnumerator();TextParts::Enumerator again=otherQuery.GetEnumerator();
                if(!first.MoveNext()||!second.MoveNext()||!lazy.MoveNext()||!again.MoveNext())return 1;
                immutable(TextParts::Enumerator)* observer=&first;readonly(TextParts::Enumerator)* shallowObserver=&second;
                Span<readonly(char)> current=observer.Current;Span<readonly(char)> other=shallowObserver.Current;
                bytes[0]='x';
                if(current.Length!=1||current[0]!='x'||other[0]!='x'||lazy.Current[0]!='x'||again.Current[0]!='x')return 2;
                if(!first.MoveNext()||observer.Current[0]!='b'||shallowObserver.Current[0]!='x')return 3;
                if(!second.MoveNext()||!lazy.MoveNext()||!again.MoveNext())return 4;
                if(first.MoveNext()||second.MoveNext()||lazy.MoveNext()||again.MoveNext()||allocations!=0)return 5;
                return 0;
            }
            """),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void ImmutableCurrentObserverCannotWriteBorrowedCharacters()
    {
        const string prefix="""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            void Test(immutable(TextParts::Enumerator)* observer){Span<readonly(char)> part=observer.Current;
            """;
        Compiler.Check(Sources(prefix+"char value=part[0];}"));
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(prefix+"part[0]='x';}")));
    }

    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\0", "\\0") + "\"";

    private static SourceFile[] Sources(string program, bool strings = false, bool formatting = false)
    {
        var names = new List<string> { "Memory/Span.gf", "Text/TextParts.gf", "Text/text_parts_span.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Queries/Enumerable.gf" };
        if (strings) names.AddRange(["Text/string_split.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "Text/String.gf", "Text/ByteSearch.gf", "primitive_format.gf"]);
        if (formatting) names.AddRange(["Text/BufferWriter.gf", "Text/BufferTooSmallException.gf", "primitive_format.gf"]);
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
    }

    private static void Run(string program, bool strings = false, bool formatting = false)
    {
        var result = CompilerTestHelper.Run(Sources(program, strings, formatting));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData(",,,")]
    [InlineData("plain")]
    [InlineData(",a,,b,")]
    [InlineData("a,b,c")]
    public void DelimiterBoundariesMatchPreservedAndRemovedEmptyParts(string input)
    {
        foreach (bool removeEmpty in new[] { false, true })
        {
            string[] expected = input.Split(',', removeEmpty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
            string checks = string.Join(Environment.NewLine, expected.Select((part, i) =>
                $"if (!Part(&iterator, {Literal(part)}, {part.Length})) return {i + 1};"));
            Run($$"""
                using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
                bool Part(TextParts::Enumerator* iterator, immutable(char*) expected, nuint count) {
                    if (!iterator.MoveNext()) return false;
                    Span<readonly(char)> part = iterator.Current;
                    if (part.Length != count) return false;
                    for (nuint i = 0; i < count; i++) if (part[i] != expected[i]) return false;
                    return true;
                }
                int main() {
                    Span<readonly(char)> text = new Span<readonly(char)>({{Literal(input)}}, {{input.Length}});
                    TextParts parts = text.Split(',', {{removeEmpty.ToString().ToLowerInvariant()}});
                    TextParts::Enumerator iterator = parts.GetEnumerator();
                    {{checks}}
                    if (iterator.MoveNext() || iterator.MoveNext()) return 20;
                    return 0;
                }
                """);
        }
    }

    [Fact]
    public void CurrentBoundsAndDefaultEmptySpanHaveDefinedBehavior()
    {
        Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Span<readonly(char)> empty = new Span<readonly(char)>();
                TextParts parts = new TextParts(empty, ',');
                TextParts::Enumerator iterator = parts.GetEnumerator();
                try { Span<readonly(char)> invalid = iterator.Current; return 1; } catch (Exception* error) { }
                if (!iterator.MoveNext()) return 2;
                Span<readonly(char)> part = iterator.Current;
                if (part.Length != (nuint)0) return 3;
                try { char invalid = part[0]; return 4; } catch (Exception* error) { }
                if (iterator.MoveNext()) return 5;
                try { Span<readonly(char)> invalid = iterator.Current; return 6; } catch (Exception* error) { }
                return 0;
            }
            """);
    }

    [Fact]
    public void ViewsBorrowMutableStorageAndIteratorsAreIndependent()
    {
        Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                public replace void Free(void* data) nothrow { }
            }
            int main() {
                char[5] bytes = ['a', ',', 'b', ',', 'c'];
                Span<char> storage = new Span<char>(bytes, 5);
                TextParts parts = storage.Split(',');
                TextParts::Enumerator first = parts.GetEnumerator();
                TextParts::Enumerator second = parts.GetEnumerator();
                storage[0] = 'z';
                if (!first.MoveNext()) return 1;
                Span<readonly(char)> a = first.Current;
                if (a.Length != (nuint)1 || a[0] != 'z') return 2;
                storage[0] = 'q';
                if (a[0] != 'q') return 3;
                if (!second.MoveNext()) return 4;
                Span<readonly(char)> b = second.Current;
                if (b[0] != 'q') return 5;
                storage[2] = 'x';
                if (!first.MoveNext()) return 6;
                Span<readonly(char)> c = first.Current;
                if (c[0] != 'x' || Calls::Allocations != 0) return 7;
                return 0;
            }
            """);
    }

    [Fact]
    public void CountedZeroDelimiterAndForeachUseViewsWithoutAllocating()
    {
        Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                public replace void Free(void* data) nothrow { }
            }
            int main() {
                Span<readonly(char)> input = new Span<readonly(char)>("a\0b\0", 4);
                TextParts parts = input.Split('\0');
                nuint count = 0; nuint sum = 0;
                foreach (Span<readonly(char)> part in parts) { count++; sum += part.Length; }
                if (count != (nuint)3 || sum != (nuint)2 || Calls::Allocations != 0) return 1;
                count = 0;
                foreach (Span<readonly(char)> part in parts) count++;
                if (count != (nuint)3) return 2;
                return 0;
            }
            """);
    }

    [Fact]
    public void StringReceiverBorrowsWithoutCopyAndUsesCountedContents()
    {
        Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            extern void*? malloc(nuint size);
            extern void free(void* data);
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return malloc(size); }
                public replace void Free(void* data) nothrow { free(data); }
            }
            int main() {
                String text = s"a\0b,c,";
                int before = Calls::Allocations;
                TextParts parts = text.Split(',');
                TextParts::Enumerator iterator = parts.GetEnumerator();
                if (!iterator.MoveNext()) return 1;
                Span<readonly(char)> first = iterator.Current;
                if (first.Length != (nuint)3 || first[1] != '\0') return 2;
                text[0] = 'z';
                if (first[0] != 'z') return 3;
                if (!iterator.MoveNext()) return 4;
                Span<readonly(char)> second = iterator.Current;
                if (second.Length != (nuint)1 || second[0] != 'c') return 5;
                if (!iterator.MoveNext()) return 6;
                Span<readonly(char)> last = iterator.Current;
                if (last.Length != (nuint)0 || iterator.MoveNext()) return 7;
                if (Calls::Allocations != before) return 8;
                return 0;
            }
            """, strings: true);
    }

    [Fact]
    public void TemporaryOwningStringCannotSupplyBorrowedParts()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            int main() { TextParts parts = s"a,b".Split(','); return 0; }
            """, strings: true)));
        Assert.Contains("Split", error.Message);
    }

    [Fact]
    public void EnumerableInterfaceAndFormattingRemainAllocationFree()
    {
        Run("""
            using std; using std::Text; using std::Memory; using std::Queries; using std::Collections;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                public replace void Free(void* data) nothrow { }
            }
            int main() {
                Span<readonly(char)> input = new Span<readonly(char)>("a,,b,", 5);
                TextParts parts = input.Split(',');
                IEnumerable<Span<readonly(char)>, TextParts::Enumerator>* sequence = &parts;
                TextParts::Enumerator iterator = sequence.GetEnumerator();
                char[16] storage;
                BufferWriter output = new BufferWriter(storage, 16);
                int count = 0;
                while (iterator.MoveNext()) {
                    if (count != 0) output.Write("/");
                    Span<readonly(char)> part = iterator.Current;
                    if (part.Length != 0) output.Write(part.Data!, part.Length);
                    count++;
                }
                count.Format(&output);
                if (Calls::Allocations != 0 || output.Length != (nuint)6) return 1;
                immutable(char*) expected = "a//b/4";
                for (nuint i = 0; i < output.Length; i++) if (output.Data[i] != expected[i]) return 2;
                return 0;
            }
            """, formatting: true);
    }
}
