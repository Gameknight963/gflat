namespace gflat.Tests;

public sealed class StandardTypeConstantLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StringTypeConstantDoesNotOccupyPerValueStorage(int optimization)
    {
        const string program="""
            using Standard; using Standard::Text;
            int main(){
                // String contains its byte pointer, length and capacity. Type-wide
                // constants must not add a hidden word to every value or array slot.
                if(sizeof(String)!=3*sizeof(void*))return 1;
                if(String::NotFound!=(nuint)0-(nuint)1)return 2;
                String empty=new();
                if(empty.IndexOf('x')!=String::NotFound||empty.LastIndexOf("x")!=String::NotFound)return 3;
                String first=String::From("alpha");String copy=first;String moved=move first;
                String[2] values=[move copy,move moved];
                values[0].AppendLiteral("!",1);
                if(values[0].Length!=6||values[1].Length!=5)return 4;
                if(values[0].IndexOf('!')!=5||values[1].IndexOf('!')!=String::NotFound)return 5;
                if(values[0].LastIndexOf('a')!=4||values[1].IndexOf("pha")!=2)return 6;
                return 0;
            }
            """;
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("type-constant-layout.gf",program));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
