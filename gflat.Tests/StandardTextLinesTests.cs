using System.Text;

namespace gflat.Tests;

public sealed class StandardTextLinesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ProtectedSequencesAndCurrentObserversRetainIndependentBorrowedViews(int optimization)
        => Run("""
            using std; using std::Collections;
            int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* data) nothrow{}
            }
            int main(){
                char[3] bytes=['a','\n','b'];TextLines lines=new(new Span<readonly(char)>(bytes,3));
                readonly(TextLines)* shallow=&lines;immutable(TextLines)* deep=&lines;
                TextLines::Enumerator first=shallow.GetEnumerator();TextLines::Enumerator second=deep.GetEnumerator();
                var query=deep.AsEnumerable();var otherQuery=shallow.AsEnumerable();
                TextLines::Enumerator lazy=query.GetEnumerator();TextLines::Enumerator again=otherQuery.GetEnumerator();
                if(!first.MoveNext()||!second.MoveNext()||!lazy.MoveNext()||!again.MoveNext())return 1;
                immutable(TextLines::Enumerator)* observer=&first;readonly(TextLines::Enumerator)* shallowObserver=&second;
                Span<readonly(char)> current=observer.Current;Span<readonly(char)> other=shallowObserver.Current;
                bytes[0]='x';
                if(current.Length!=1||current[0]!='x'||other[0]!='x'||lazy.Current[0]!='x'||again.Current[0]!='x')return 2;
                if(!first.MoveNext()||observer.Current[0]!='b'||shallowObserver.Current[0]!='x')return 3;
                if(!second.MoveNext()||!lazy.MoveNext()||!again.MoveNext())return 4;
                if(first.MoveNext()||second.MoveNext()||lazy.MoveNext()||again.MoveNext()||allocations!=0)return 5;
                return 0;
            }
            """,optimization);

    [Fact]
    public void ImmutableCurrentObserverCannotWriteBorrowedCharacters()
    {
        const string prefix="""
            using std; using std::Collections;
            void Test(immutable(TextLines::Enumerator)* observer){Span<readonly(char)> line=observer.Current;
            """;
        Compiler.Check(Sources(prefix+"char value=line[0];}"));
        Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Check(Sources(prefix+"line[0]='x';}")));
    }

    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\0", "\\0").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedLinesMatchLfSplittingWithCrLfTerminatorsAndPreservedBinaryText(int optimization)
    {
        var cases = new List<string> { "", "\n", "\r", "\r\n", "\n\n", "a\n", "a\n\n", "a\rb", "a\r\rb\r\n", "a\0b\r\n\0", "\r\r\n\r", "last\r" };
        var random = new Random(7281);
        const string alphabet = "ab\r\n\0";
        for (int n = 1; n <= 40; n++) cases.Add(new string(Enumerable.Range(0, n).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray()));
        var program = new StringBuilder("""
            using std; using std::Collections;
            namespace Memory { replace void*? Allocate(nuint size) => null; replace void Free(void* data) nothrow {} }
            bool Next(TextLines::Enumerator* iterator, readonly(char)* expected, nuint length) {
                if(!iterator.MoveNext())return false;
                Span<readonly(char)> line=iterator.Current;
                if(line.Length!=length)return false;
                for(nuint i=0;i<length;i++)if(line[i]!=expected[i])return false;
                return true;
            }
            int main(){
            """);
        foreach (string text in cases)
        {
            var split = text.Split('\n');
            int count = text.Length == 0 ? 0 : split.Length - (text.EndsWith('\n') ? 1 : 0);
            program.AppendLine($$"""
                { Span<readonly(char)> input=new({{Literal(text)}},{{text.Length}});
                  TextLines lines=new(input); TextLines::Enumerator iterator=lines.GetEnumerator();
                """);
            for (int i = 0; i < count; i++)
            {
                string line = split[i];
                if (i < split.Length - 1 && line.EndsWith('\r')) line = line[..^1];
                program.AppendLine($"if(!Next(&iterator,{Literal(line)},{line.Length}))return 1;");
            }
            program.AppendLine("if(iterator.MoveNext()||iterator.MoveNext())return 2;}");
        }
        program.Append("return 0;}");
        Run(program.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IteratorsBorrowExactSlicesAndCopiesAdvanceIndependently(int optimization)
        => Run("""
            using std; using std::Collections;
            int main(){
                char[7] bytes=['a','\r','\n','b','\0','c','\n'];
                TextLines lines=new(new Span<readonly(char)>(bytes,7));
                TextLines::Enumerator first=lines.GetEnumerator();
                try { Span<readonly(char)> invalid=first.Current; return 1; } catch(Exception* error){}
                if(!first.MoveNext())return 2;
                Span<readonly(char)> a=first.Current;
                if(a.Data!=&bytes[0]||a.Length!=1)return 3;
                TextLines::Enumerator second=first;
                if(!first.MoveNext())return 4;
                Span<readonly(char)> b=first.Current;
                if(b.Data!=&bytes[3]||b.Length!=3||b[1]!='\0')return 5;
                if(second.Current.Data!=&bytes[0]||!second.MoveNext()||second.Current.Data!=b.Data)return 6;
                if(first.MoveNext())return 7;
                try { Span<readonly(char)> invalid=first.Current; return 8; } catch(Exception* error){}
                int count=0;
                foreach(Span<readonly(char)> line in lines){count++; if(count==1)break;}
                foreach(Span<readonly(char)> line in lines)count++;
                if(count!=3)return 9;
                TextLines empty=new(new Span<readonly(char)>());
                TextLines::Enumerator none=empty.GetEnumerator();
                if(none.MoveNext()||none.MoveNext())return 10;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamedStringAndSpanConveniencesBorrowAndSupportLazyQueries(int optimization)
        => Run("""
            using std; using std::Collections;
            static class Calls { public static int Allocations; }
            namespace detail {
                extern void*? malloc(nuint size); extern void free(void* data);
            }
            namespace Memory {
                replace void*? Allocate(nuint size) { Calls::Allocations++; return detail::malloc(size); }
                replace void Free(void* data) nothrow { detail::free(data); }
            }
            bool Nonempty(Span<readonly(char)> line) => !line.IsEmpty;
            int main(){
                String text=s"a\r\n\nb\0c\n";
                int baseline=Calls::Allocations;
                TextLines lines=text.Lines();
                TextLines::Enumerator iterator=lines.GetEnumerator();
                if(!iterator.MoveNext()||iterator.Current.Data!=text.Data)return 1;
                var selected=lines.AsEnumerable().Where(Nonempty);
                if(selected.Count()!=2)return 2;
                Span<readonly(char)> last=new();
                if(!selected.TryLast(ref last)||last.Length!=3||last[1]!='\0')return 3;
                if(last.Data!=text.Data+4)return 4;
                char[2] bytes=['x','\n'];
                Span<char> writable=new(bytes,2);
                TextLines mutableLines=writable.Lines();
                TextLines readonlyLines=writable.AsImmutable().Lines();
                if(mutableLines.AsEnumerable().Count()!=1||readonlyLines.AsEnumerable().Count()!=1)return 5;
                String empty=new();
                if(empty.Lines().AsEnumerable().Any())return 6;
                if(Calls::Allocations!=baseline)return 7;
                return 0;
            }
            """, optimization, conveniences: true);

    [Fact]
    public void StringConvenienceRejectsTemporaryOwners()
    {
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(Sources("""
            using std; using std::Collections;
            int main(){ TextLines lines=s"a\n".Lines(); return 0; }
            """, conveniences: true)));
        Assert.Contains("Lines", error.Message);
    }

    private static SourceFile[] Sources(string program, bool conveniences = false)
    {
        var names = new List<string> { "Span.gf", "TextLines.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Collections/Enumerable.gf" };
        if(conveniences) names.AddRange(["TextLinesSpan.gf","StringLines.gf","Collections/Vector.gf","String.gf", "ByteSearch.gf","IStringConvertable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing.gf","Formatting.gf",
            "Collections/EnumerableExtensions.gf","Collections/WhereSource.gf","Collections/SelectSource.gf","Collections/SliceSource.gf","Collections/TakeWhileSource.gf","Collections/SkipWhileSource.gf"]);
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
    }

    private static void Run(string program, int optimization, bool conveniences = false)
    {
        var result = CompilerTestHelper.Run(Sources(program, conveniences), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
