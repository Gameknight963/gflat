namespace gflat.Tests;

public class StandardStringOperationsTests
{
    private static IEnumerable<SourceFile> Sources(string code) =>
        new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "IStringConvertable.gf", "String.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
    private static void Run(string code) => Assert.Equal(0, CompilerTestHelper.Run(Sources(code)).ExitCode);

    [Fact]
    public void SubstringsOwnTheirBuffersAndRejectOverflowingRanges()
        => Run("""
            using std;
            int main() {
                String original=String::From("ab\0cd",5);
                String part=original.Substring(1,3); String tail=original.Substring(3);
                original[1]='X'; original.Clear();
                if(part.Length!=3 || part[0]!='b' || part[1]!='\0' || part[2]!='c' || tail.Length!=2 || !tail.StartsWith("cd")) return 1;
                String empty=part.Substring(3); if(!empty.IsEmpty || empty.ToCString()[0]!='\0') return 2;
                int caught=0;
                try { part.Substring(4); } catch { caught++; }
                try { part.Substring(1,(nuint)0-(nuint)1); } catch { caught++; }
                return caught-2;
            }
            """);

    [Fact]
    public void SearchesUseByteOffsetsCountedNeedlesAndDefinedEmptyBehavior()
        => Run("""
            using std;
            int main() {
                String text=String::From("ab\0abcab",8);
                if(text.IndexOf('a')!=0 || text.IndexOf('a',1)!=3 || text.IndexOf('z')!=String::NotFound) return 1;
                if(text.IndexOf("ab",2,1)!=3 || text.IndexOf("\0a",2)!=2 || text.IndexOf("abca")!=3) return 2;
                if(text.IndexOf("",0,8)!=8 || text.IndexOf('a',8)!=String::NotFound) return 3;
                if(!text.Contains('\0') || !text.Contains("abc") || text.Contains("abcd") || !text.Contains("\0a",2)) return 4;
                if(!text.StartsWith("ab\0",3) || !text.EndsWith("cab") || !text.StartsWith("") || !text.EndsWith("")) return 5;
                if(text.StartsWith("abcdefghi") || text.EndsWith("abcdefghi")) return 6;
                int caught=0; try { text.IndexOf('x',9); } catch { caught++; }
                try { text.IndexOf("",0,(nuint)0-(nuint)1); } catch { caught++; }
                String empty=new(); if(empty.IndexOf("")!=0 || empty.Contains('a') || !empty.Contains("")) return 7;
                return caught-2;
            }
            """);

    [Fact]
    public void AppendingBorrowedSelfWorksWithAndWithoutGrowth()
        => Run("""
            using std;
            int main() {
                String text=s"ab"; text.Append(&text);
                if(text.Length!=4 || !text.StartsWith("abab")) return 1;
                text.Reserve(50); text.Append(text.Data+1,3); text.Append('!'); text.Append("?");
                if(text.Length!=9 || !text.StartsWith("ababbab!?")) return 2;
                text.Append('\0'); if(text.Length!=10 || text[9]!='\0' || text.Data[10]!='\0') return 3;
                String empty=new(); empty.Append(&empty); empty.Append("");
                if(!empty.IsEmpty) return 4;
                return 0;
            }
            """);

    [Fact]
    public void RemoveReplaceAndAsciiTrimPreserveTerminationWithoutAllocating()
        => Run("""
            using std;
            int main() {
                String text=s" \t hello world \r\n"; text.Trim();
                if(text.Length!=11 || !text.StartsWith("hello world")) return 1;
                text.Remove(5,1); text.Replace('l','L');
                if(text.Length!=10 || !text.StartsWith("heLLoworLd") || text.Data[text.Length]!='\0') return 2;
                text.Remove(text.Length,0);
                int caught=0; try { text.Remove(11,0); } catch { caught++; }
                try { text.Remove(1,(nuint)0-(nuint)1); } catch { caught++; }
                if(caught!=2 || text.Length!=10) return 3;
                text.Remove(0,text.Length); text.Trim(); if(!text.IsEmpty || text.Data[0]!='\0') return 4;
                String spaces=s" \t\r\n"; spaces.Trim(); if(!spaces.IsEmpty) return 5;
                String embedded=String::From(" \0 ",3); embedded.Trim();
                if(embedded.Length!=1 || embedded[0]!='\0') return 6;
                return 0;
            }
            """);
}
