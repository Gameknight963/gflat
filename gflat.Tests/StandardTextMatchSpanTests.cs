using System.Text;

namespace gflat.Tests;

public sealed class StandardTextMatchSpanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndTerminatedPatternsMatchDotNetByteSpans(int optimization)
    {
        var random=new Random(7421);
        var cases=new List<(byte[] Text,byte[] Pattern)>{([],[]),([],[1]),([0],[0]),([255,128,0],[255,128]),
            ([65,66],[65,66]),([65,66],[65,66,67]),([65,66],[97]),([1,0,2],[0,2]),([0,1,0],[0])};
        for(int n=0;n<48;n++)
        {
            byte[] text=new byte[random.Next(1,17)];random.NextBytes(text);
            byte[] pattern=new byte[random.Next(0,19)];random.NextBytes(pattern);
            cases.Add((text,pattern));
            int length=random.Next(text.Length+1);
            cases.Add((text,text[..length]));cases.Add((text,text[(text.Length-length)..]));
        }
        var code=new StringBuilder("using std; using std::Memory; using std::Queries; using std::Collections;int main(){char[20] input;char[20] pattern;\n");
        foreach(var item in cases)
        {
            for(int i=0;i<item.Text.Length;i++)code.AppendLine($"input[{i}]=(char){item.Text[i]};");
            for(int i=0;i<item.Pattern.Length;i++)code.AppendLine($"pattern[{i}]=(char){item.Pattern[i]};");
            code.AppendLine($"pattern[{item.Pattern.Length}]='\\0';input[{item.Text.Length}]=(char)165;");
            bool prefix=item.Text.AsSpan().StartsWith(item.Pattern),suffix=item.Text.AsSpan().EndsWith(item.Pattern);
            int zero=Array.IndexOf(item.Pattern,(byte)0);byte[] cpattern=zero<0?item.Pattern:item.Pattern[..zero];
            bool cprefix=item.Text.AsSpan().StartsWith(cpattern),csuffix=item.Text.AsSpan().EndsWith(cpattern);
            code.AppendLine($$"""
                {Span<readonly(char)> text=new(input,{{item.Text.Length}});
                 if(text.StartsWith(pattern,{{item.Pattern.Length}})!={{Bool(prefix)}}||text.EndsWith(pattern,{{item.Pattern.Length}})!={{Bool(suffix)}})return 1;
                 if(text.StartsWith(pattern)!={{Bool(cprefix)}}||text.EndsWith(pattern)!={{Bool(csuffix)}})return 2;
                 if(input[{{item.Text.Length}}]!=(char)165)return 3;}
                """);
            for(int i=0;i<item.Text.Length;i++)code.AppendLine($"if(input[{i}]!=(char){item.Text[i]})return 4;");
            for(int i=0;i<item.Pattern.Length;i++)code.AppendLine($"if(pattern[{i}]!=(char){item.Pattern[i]})return 5;");
        }
        code.AppendLine("return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InteriorAliasedViewsAndEmptyChecksDoNotAllocateOrReadOutsideCounts(int optimization)
        => Run("""
            using std; using std::Memory; using std::Queries; using std::Collections;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* pointer) nothrow{}
            }
            int main(){
                char[7] bytes=['!',(char)255,'\0','x','?',(char)128,'!'];
                Span<char> mutable=new(bytes,7);Span<readonly(char)> text=mutable.AsReadOnly().Slice(1,3);
                if(!text.StartsWith(&bytes[1],2)||!text.EndsWith(&bytes[2],2))return 1;
                if(text.StartsWith(&bytes[2],2)||text.EndsWith(&bytes[1],2))return 2;
                if(!text.StartsWith((readonly(char)*)1,0)||!text.EndsWith((readonly(char)*)1,0))return 3;
                if(text.StartsWith((readonly(char)*)1,4)||text.EndsWith((readonly(char)*)1,4))return 4;
                Span<readonly(char)> empty=new();
                if(!empty.StartsWith((readonly(char)*)1,0)||!empty.EndsWith((readonly(char)*)1,0))return 5;
                if(empty.StartsWith((readonly(char)*)1,1)||empty.EndsWith((readonly(char)*)1,1))return 6;
                if(!text.StartsWith(&bytes[1])||!text.EndsWith("x"))return 7;
                if(bytes[0]!='!'||bytes[1]!=(char)255||bytes[2]!='\0'||bytes[3]!='x'||bytes[4]!='?'||bytes[5]!=(char)128||bytes[6]!='!')return 8;
                if(allocations!=0)return 9;return 0;
            }
            """,optimization);

    private static string Bool(bool value)=>value?"true":"false";
    private static void Run(string code,int optimization)
    {
        var sources=new[]{"Memory/Span.gf","CString.gf","TextMatchSpan.gf","Collections/IEnumerable.gf","Queries/Enumerable.gf"}
            .Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("match.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
