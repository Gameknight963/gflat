namespace gflat.Tests.Std.Text;

public sealed class StringSpanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ViewsBorrowCountedBytesWithoutCopyingOrAllocating(int optimization)
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory;
            extern void*? malloc(nuint count);extern void free(void* storage);
            bool deny=false;int allocations=0;int releases=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){if(deny)return null;allocations++;return malloc(count);}
                public replace void Free(void* storage) nothrow{releases++;free(storage);}
            }
            bool Inspect(ref immutable(String) text){
                Span<readonly(char)> view=text.AsImmutable();
                return view.Length==4&&view.Data==text.Data&&view[0]=='a'&&view[1]=='\0';
            }
            int main(){
                {char[4] source=['a','\0','c',(char)255];String text=String::From(source,4);
                 nuint capacity=text.Capacity;int before=allocations;deny=true;
                 Span<char> bytes=text.AsSpan();
                 if(bytes.Length!=4||bytes.Data!=(char*)text.Data||!Inspect(ref text))return 1;
                 Span<char> slice=bytes.Slice(1,2);slice[0]='B';slice[1]='\0';
                 if(text[1]!='B'||text[2]!='\0'||text.Length!=4||text.Capacity!=capacity||text.Data[4]!='\0')return 2;
                 Span<readonly(char)> read=text.AsImmutable();if(read[1]!='B'||read[2]!='\0')return 3;
                 if(allocations!=before)return 4;deny=false;}
                {immutable(String) text=s"read";deny=true;
                 Span<readonly(char)> read=text.AsImmutable();if(read.Length!=4||read[0]!='r')return 5;deny=false;}
                if(allocations!=releases)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryEmptyViewHasNullDataAndExposesNeitherCapacityNorSentinel(int optimization)
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory;
            bool Empty(ref String text){
                Span<char> bytes=text.AsSpan();Span<readonly(char)> read=text.AsImmutable();
                return bytes.Length==0&&bytes.Data==null&&read.Length==0&&read.Data==null;
            }
            int main(){String text=new();if(!Empty(ref text))return 1;
                text.Reserve(10);if(text.Capacity<10||!Empty(ref text))return 2;
                text.Append('x');text.Clear();if(!Empty(ref text)||text.Data[0]!='\0')return 3;
                text.Append('y');Span<char> bytes=text.AsSpan();if(bytes.Length!=1||bytes[0]!='y')return 4;
                return 0;}
            """,optimization);

    [Theory]
    [InlineData("immutable(String) text=s\"read\";text.AsSpan();")]
    public void ViewsRejectReadonlyMutation(string statement)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Emit(Sources("using Standard; using Standard::Text; using Standard::Memory;int main(){"+statement+"return 0;}")));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TemporaryStringViewsCanBeConsumedBeforeFullExpressionCleanup(int optimization)
    {
        const string program = """
            using Standard; using Standard::Text; using Standard::Memory;
            extern void*? malloc(nuint size);extern void free(void* data);
            int allocated=0;int released=0;
            namespace Memory {
                public replace void*? Allocate(nuint size){void*? result=malloc(size);if(result!=null)allocated++;return result;}
                public replace void Free(void* data)nothrow{released++;free(data);}
            }
            int Consume(Span<readonly(char)> view){
                if(allocated-released!=1||view.Length!=3||view[0]!='a'||view[2]!='c')return 1;
                return 0;
            }
            int ConsumeMutable(Span<char> view){
                if(allocated-released!=1||view.Length!=3||view[0]!='a'||view[2]!='c')return 2;
                view[1]='X';if(view[1]!='X')return 3;return 0;
            }
            int main(){
                int result=ConsumeMutable(String::From("abc").AsSpan());
                if(result!=0)return result;if(allocated!=released)return 4;
                result=Consume(String::From("abc").AsReadOnly());
                if(result!=0)return result;if(allocated!=released)return 5;
                result=Consume(String::From("abc").AsImmutable());
                if(result!=0)return result;if(allocated!=released)return 6;
                return 0;
            }
            """;
        string ir = Compiler.Emit(Sources(program), options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData("AsSpan")]
    [InlineData("AsReadOnly")]
    [InlineData("AsImmutable")]
    public void ExplicitRefViewFunctionsStillRequireExistingOwners(string name)
    {
        string program = "using Standard;using Standard::Text;using Standard::Memory;int main(){" + name + "(ref String::From(\"abc\"));return 0;}";
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(Sources(program)));
        Assert.Contains("existing storage", error.Message);
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("main.gf",code));
    private static void Run(string code,int optimization)
        => Assert.Equal(0,CompilerTestHelper.Run(Sources(code),optimization).ExitCode);
}
