namespace gflat.Tests.Std.Text;

public class StringOperationsTests
{
    private static IEnumerable<SourceFile> Sources(string code) =>
        new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "IStringConvertable.gf", "Text/String.gf", "Text/ByteSearch.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
    private static void Run(string code) => Assert.Equal(0, CompilerTestHelper.Run(Sources(code)).ExitCode);

    [Fact]
    public void SubstringsOwnTheirBuffersAndRejectOverflowingRanges()
        => Run("""
            using std; using std::Text;
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
            using std; using std::Text;
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

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedSearchMatchesOrdinalSearchAcrossRandomBytesAndRepeatedPrefixes(int optimization)
    {
        var random=new Random(403);
        const string alphabet="abc\0";
        string MakeText(int count)=>new(Enumerable.Range(0,count).Select(_=>alphabet[random.Next(alphabet.Length)]).ToArray());
        string Literal(string value)=>"\""+value.Replace("\0","\\0")+"\"";
        var body=new System.Text.StringBuilder("using std; using std::Text; int main(){");
        for(int sample=0;sample<96;sample++)
        {
            string text=MakeText(random.Next(81)), needle=MakeText(random.Next(21));
            int start=random.Next(text.Length+1);
            int expected=text.IndexOf(needle,start,StringComparison.Ordinal);
            body.Append($$"""
                {String text=String::From({{Literal(text)}},{{text.Length}});
                 if(text.IndexOf({{Literal(needle)}},{{needle.Length}},{{start}})!={{(expected<0 ? "String::NotFound" : expected.ToString())}}) return {{sample+1}};}
                """);
        }
        body.Append("""
            char[2001] bytes; char[512] needle;
            for(nuint i=0;i<2001;i++) bytes[i]='a';
            for(nuint i=0;i<512;i++) needle[i]='a';
            needle[511]='b';
            String repeated=String::From(bytes,2001);
            if(repeated.IndexOf(needle,512)!=String::NotFound) return 100;
            repeated[2000]='b';
            if(repeated.IndexOf(needle,512)!=1489) return 101;
            return 0;}
            """);
        Assert.Equal(0,CompilerTestHelper.Run(Sources(body.ToString()),optimization).ExitCode);
    }

    [Fact]
    public void AppendingBorrowedSelfWorksWithAndWithoutGrowth()
        => Run("""
            using std; using std::Text;
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
            using std; using std::Text;
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

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InsertHandlesAllSelfSlicesBeforeAndAfterGrowth(int optimization)
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Text;
            int main() {
                for (int reserved=0; reserved<2; reserved++) {
                    for (nuint at=0; at<=5; at++) {
                        for (nuint start=0; start<=5; start++) {
                            for (nuint count=0; count<=6-start; count++) {
                                String text=String::From("ab\0cd",5);
                                String original=text;
                                if(reserved!=0) text.Reserve(50);
                                // Includes slices containing the original terminator.
                                text.Insert(at,text.Data+start,count);
                                if(text.Length!=5+count || text.Data[text.Length]!='\0') return 1;
                                for(nuint i=0;i<at;i++) if(text[i]!=original[i]) return 2;
                                for(nuint i=0;i<count;i++) if(text[at+i]!=original.Data[start+i]) return 3;
                                for(nuint i=at;i<5;i++) if(text[i+count]!=original[i]) return 4;
                            }
                        }
                    }
                }
                String text=s"ad"; text.Insert(1,"bc"); text.Insert(4,'!');
                String prefix=s"start:"; text.Insert(0,&prefix);
                if(text.Length!=11 || !text.StartsWith("start:abcd!")) return 5;
                int caught=0;
                try { text.Insert(12,"",0); } catch { caught++; }
                try { text.Insert(0,"x",(nuint)0-(nuint)1); } catch { caught++; }
                return caught-2;
            }
            """),optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Fact]
    public void FailedSelfInsertionLeavesOriginalBufferUnchangedAndReleasesSnapshot()
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Text;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(readonly(char)* text,...);
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    if(size==7) return null;
                    printf("A"); return malloc(size);
                }
                public replace void Free(void* p) nothrow { printf("F"); free(p); }
            }
            int main() {
                String text=s"abc"; readonly(char)* old=text.Data;
                try { text.Insert(1,text.Data,2); return 1; } catch { }
                if(text.Data!=old || text.Length!=3 || text.Capacity!=3 || !text.StartsWith("abc") || text.Data[3]!='\0') return 2;
                return 0;
            }
            """));
        Assert.Equal(0,result.ExitCode);
        // Original string, snapshot, and exception storage are each released.
        Assert.Equal("AAAFFF",result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedReplacementMatchesDotNetEvenWhenBothPatternsBorrowSelf(int optimization)
    {
        const string original="ab\0aba";
        var random=new Random(173);
        var body=new System.Text.StringBuilder("using std; using std::Text; int main(){");
        for(int test=0;test<48;test++)
        {
            int oldStart=random.Next(original.Length), oldCount=random.Next(1,original.Length-oldStart+1);
            int newStart=random.Next(original.Length+1), newCount=random.Next(original.Length-newStart+1);
            string expected=original.Replace(original.Substring(oldStart,oldCount),original.Substring(newStart,newCount),StringComparison.Ordinal);
            string literal="\""+expected.Replace("\0","\\0")+"\"";
            body.Append($$"""
                { String text=String::From("ab\0aba",6);
                  text.Replace(text.Data+{{oldStart}},{{oldCount}},text.Data+{{newStart}},{{newCount}});
                  if(text.Length!={{expected.Length}} || text.Data[text.Length]!='\0') return 1;
                  for(nuint i=0;i<text.Length;i++) if(text[i]!={{literal}}[i]) return 2; }
                """);
        }
        body.Append("""
            String repeated=s"aaaaa"; repeated.Replace("aa","b");
            if(repeated.Length!=3 || !repeated.StartsWith("bba")) return 3;
            repeated.Replace("bba",""); if(!repeated.IsEmpty || repeated.Data[0]!='\0') return 4;
            String unchanged=s"unchanged"; readonly(char)* old=unchanged.Data;
            unchanged.Replace("not present","x");
            if(unchanged.Data!=old) return 5;
            int caught=0; try { unchanged.Replace("","x"); } catch { caught++; }
            try { unchanged.Replace("u",1,"x",(nuint)0-(nuint)1); } catch { caught++; }
            if(caught!=2 || !unchanged.StartsWith("unchanged")) return 6;
            return 0;}
            """);
        Assert.Equal(0,CompilerTestHelper.Run(Sources(body.ToString()),optimization).ExitCode);
    }

    [Fact]
    public void FailedTextReplacementPreservesOriginalStorage()
        => Run("""
            using std; using std::Text;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    if(size==9) return null;
                    return malloc(size);
                }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int main() {
                String text=s"abcabc"; readonly(char)* old=text.Data;
                try { text.Replace("bc","XYZ"); return 1; } catch (BadAllocException* error) { }
                if(text.Data!=old || text.Length!=6 || text.Capacity!=6 || !text.StartsWith("abcabc") || text.Data[6]!='\0') return 2;
                return 0;
            }
            """);

    [Fact]
    public void SuccessfulReplacementReleasesOldAndReplacementBuffersExactlyOnce()
    {
        var result=CompilerTestHelper.Run(Sources("""
            using std; using std::Text;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(readonly(char)* text,...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
                public replace void Free(void* storage) nothrow { printf("F"); free(storage); }
            }
            int main() {
                String text=s"ababa";
                text.Replace(text.Data,3,text.Data+4,1);
                if(text.Length!=3 || !text.StartsWith("aba")) return 1;
                return 0;
            }
            """),2);
        Assert.Equal(0,result.ExitCode);
        Assert.Equal("AAFF",result.StandardOutput);
    }
}
