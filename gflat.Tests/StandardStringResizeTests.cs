using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardStringResizeTests
{
    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(InterpolationTests.Sources(code), optimization).ExitCode);

    private const string Allocator = """
        using std; using std::Text; using std::Collections;
        int allocations=0;int releases=0;bool deny=false;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        namespace Memory{
            public replace void*? Allocate(nuint bytes){if(deny)return null;allocations++;return malloc(bytes);}
            public replace void Free(void* storage) nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultFillCountedNullsAndRetainedCapacityRespectEveryByte(int optimization)
        => Run(Allocator + """
            int main(){
                {
                    String text=new();readonly(char)* sentinel=text.Data;
                    deny=true;text.Resize(0);deny=false;
                    if(text.Data!=sentinel||allocations!=0)return 1;
                    text.Resize(3);
                    if(text.Length!=3||text.Data[3]!='\0')return 2;
                    for(nuint i=0;i<3;i++)if(text[i]!='\0')return 3;
                    text[1]='x';text.Resize(7,(char)255);
                    if(text[0]!='\0'||text[1]!='x'||text[2]!='\0')return 4;
                    for(nuint i=3;i<7;i++)if(text[i]!=(char)255)return 5;
                    text.Resize(32,text[1]);
                    for(nuint i=7;i<32;i++)if(text[i]!='x')return 6;
                    readonly(char)* address=text.Data;nuint capacity=text.Capacity;
                    int before=allocations;deny=true;
                    text.Resize(2);text.Resize(0);
                    if(text.Length!=0||text.Data[0]!='\0')return 7;
                    for(int value=0;value<256;value++){
                        text.Resize(0);text.Resize(32,(char)value);
                        if(text.Length!=32||text.Data[32]!='\0')return 8;
                        for(nuint i=0;i<32;i++)if(text[i]!=(char)value)return 9;
                        text.Resize(32,'!');if(text[0]!=(char)value)return 10;
                    }
                    deny=false;
                    if(text.Data!=address||text.Capacity!=capacity||allocations!=before)return 11;
                    text.Resize(3);text[0]='a';text[1]='\0';text[2]='b';
                    String copy=text;
                    if(copy.Length!=3||copy[0]!='a'||copy[1]!='\0'||copy[2]!='b'||copy.Data[3]!='\0')return 12;
                    text.Resize(1);if(text[0]!='a'||text.Data[1]!='\0'||copy.Length!=3)return 13;
                }
                if(allocations!=releases)return 14;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedGrowthAndOverflowPreserveStorageThenAllowRetry(int optimization)
        => Run(Allocator + """
            int main(){
                {
                    String text=s"a\0b";readonly(char)* address=text.Data;nuint capacity=text.Capacity;
                    deny=true;
                    try{text.Resize(32,'z');return 1;}catch(ref BadAllocException error){}
                    deny=false;
                    if(text.Length!=3||text.Capacity!=capacity||text.Data!=address)return 2;
                    if(text[0]!='a'||text[1]!='\0'||text[2]!='b'||text.Data[3]!='\0')return 3;
                    try{text.Resize((nuint)0-(nuint)1);return 4;}catch(ref Exception error){}
                    if(text.Length!=3||text.Capacity!=capacity||text.Data!=address)return 5;
                    if(text[0]!='a'||text[1]!='\0'||text[2]!='b'||text.Data[3]!='\0')return 6;
                    text.Resize(32,'z');
                    if(text.Length!=32||text[0]!='a'||text[1]!='\0'||text[2]!='b'||text.Data[32]!='\0')return 7;
                    for(nuint i=3;i<32;i++)if(text[i]!='z')return 8;
                }
                if(allocations!=releases)return 9;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("readonly")]
    [InlineData("immutable")]
    public void ProtectedReceiversCannotResize(string protection)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(InterpolationTests.Sources(
            $"void F(ref {protection}(std::Text::String) text){{text.Resize(1);}}")));
        Assert.Contains("Resize",error.Message);
    }
}
