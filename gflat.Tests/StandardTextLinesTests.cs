using System.Text;

namespace gflat.Tests;

public sealed class StandardTextLinesTests
{
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
            using std;
            namespace Memory { replace void*? Allocate(nuint size) => null; replace void Free(void* data) {} }
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
            using std;
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

    private static void Run(string program, int optimization)
    {
        var names = new[] { "Span.gf", "TextLines.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Collections/Enumerable.gf" };
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
