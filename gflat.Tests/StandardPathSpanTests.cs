namespace gflat.Tests;

public sealed class StandardPathSpanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedResultsMatchExistingLexicalPolicyForRootsAndDotComponents(int optimization)
    {
        string[] paths=["", ".", "..", "...", ".config", "file.", "dir/file.tar.gz", "dir/", "a//b", "/", "//", "///leaf", "C:", "C:.", "C:..", "C:folder", "C:/", "C:/dir/file.ext", "//host", "//host/", "//host/share", "//host/share/", "//host/share//leaf", "//host/share/../file.tar", @"dir\leaf.ext", @"\/host\share/sub//file."];
        string[] methods=["GetFileName","GetDirectoryName","GetExtension","GetFileNameWithoutExtension","GetRoot"];
        var program=new System.Text.StringBuilder("using std; int main(){\n");
        foreach(string style in new[]{"Unix","Windows"})foreach(string path in paths)
        {
            string literal="\""+path.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
            program.AppendLine($$"""
                { Span<readonly(char)> input=new({{literal}},{{path.Length}});
                """);
            foreach(string method in methods)
                program.AppendLine($$"""
                    { String expected=Paths::{{method}}({{literal}},PathStyle::{{style}});
                      var actual=Paths::{{method}}(input,PathStyle::{{style}});
                      if(actual.Length!=expected.Length)return 1;
                      for(nuint i=0;i<actual.Length;i++)if(actual[i]!=expected.Data[i])return 2;
                      if(actual.IsEmpty&&actual.Data!=null)return 3; }
                    """);
            program.AppendLine("}");
        }
        program.AppendLine("return 0;}");Run(program.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedInteriorViewsPreserveZeroAndHighBytesWithoutAllocation(int optimization)
        =>Run("""
            using std;int allocations=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){allocations++;return null;}
                public replace void Free(void* pointer){}
            }
            int main(){
                char[17] bytes=['x','C',':','/','d','i','r','/','f','i','l','e','.','e','x','t','x'];
                Span<readonly(char)> path=new(&bytes[1],15);
                var name=Paths::GetFileName(path,PathStyle::Windows);
                var directory=Paths::GetDirectoryName(path,PathStyle::Windows);
                var extension=Paths::GetExtension(path,PathStyle::Windows);
                var stem=Paths::GetFileNameWithoutExtension(path,PathStyle::Windows);
                var root=Paths::GetRoot(path,PathStyle::Windows);
                if(name.Length!=8||directory.Length!=6||extension.Length!=4||stem.Length!=4||root.Length!=3)return 1;
                if(name.Data!=&bytes[8]||stem.Data!=&bytes[8]||extension.Data!=&bytes[12]||root.Data!=&bytes[1]||directory.Data!=&bytes[1])return 2;
                bytes[8]='F';if(name[0]!='F'||stem[0]!='F')return 3;
                char[7] counted=['a','/','b','\0',(char)255,'.','d'];path=new(counted,7);
                name=Paths::GetFileName(path,PathStyle::Unix);extension=Paths::GetExtension(path,PathStyle::Unix);
                if(name.Length!=5||name[1]!='\0'||name[2]!=(char)255||extension.Length!=2||extension[1]!='d')return 4;
                if(counted[0]!='a'||counted[1]!='/'||counted[2]!='b'||counted[5]!='.')return 5;
                Span<readonly(char)> empty=new();
                if(Paths::GetFileName(empty,PathStyle::Windows).Data!=null||Paths::GetDirectoryName(empty,PathStyle::Unix).Data!=null||Paths::GetExtension(empty,PathStyle::Windows).Data!=null||Paths::GetFileNameWithoutExtension(empty,PathStyle::Unix).Data!=null||Paths::GetRoot(empty,PathStyle::Windows).Data!=null)return 6;
                Span<readonly(char)> zero=new((readonly(char)*)1,0);
                if(!Paths::GetFileName(zero,PathStyle::Windows).IsEmpty||!Paths::GetRoot(zero,PathStyle::Windows).IsEmpty)return 7;
                if(allocations!=0)return 8;return 0;
            }
            """,optimization);

    [Fact]
    public void BorrowedPathResultsProtectCharacters()
    {
        var error=Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("""
            using std;int main(){char[3] bytes=['a','/','b'];Span<readonly(char)> path=new(bytes,3);
                var name=Paths::GetFileName(path,PathStyle::Unix);name[0]='c';return 0;}
            """)));
        Assert.Contains("readonly",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("path-span.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }
}
