using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonStringWriterTests
{
    private const string Counters="""
        using std;
        extern void*? malloc(nuint size);extern void free(void* storage);
        int allocations=0;int releases=0;int destroyed=0;
        namespace Memory {
            public replace void*? Allocate(nuint size) {allocations++;return malloc(size);}
            public replace void Free(void* storage) {releases++;free(storage);}
        }
        struct TrackingWriter : ITextWriter {
            private char* data;
            public nuint Length;
            public int Calls;
            public int FailAt;
            public nuint Largest;
            public TrackingWriter(char* storage,int failAt) {data=storage;Length=0;Calls=0;FailAt=failAt;Largest=0;}
            ~TrackingWriter() {destroyed++;}
            public void Write(readonly(char)* text,nuint count) {
                Calls++;
                if(Calls==FailAt)throw new Exception("writer failed",77);
                if(count>Largest)Largest=count;
                for(nuint i=0;i<count;i++)data[Length+i]=text[i];
                Length+=count;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnescapedSpansAreBatchedWithoutAllocatingOrCopyingWriter(int optimization)
        => Run(Counters+"""
            int main() {
                char[5000] input;char[5004] storage;
                for(nuint i=0;i<5000;i++)input[i]='a';
                {
                    TrackingWriter sink=new(storage,0);ITextWriter* output=&sink;
                    output.WriteJsonString(input,5000);
                    if(sink.Calls!=3||sink.Largest!=5000||sink.Length!=5002)return 1;
                    if(storage[0]!='"'||storage[5001]!='"')return 2;
                    for(nuint i=1;i<=5000;i++)if(storage[i]!='a')return 3;
                    if(allocations!=0||releases!=0||destroyed!=0)return 4;
                    sink.Length=0;sink.Calls=0;
                    output.WriteJsonString("",0);
                    if(sink.Calls!=2||sink.Length!=2||storage[0]!='"'||storage[1]!='"')return 6;
                }
                if(destroyed!=1)return 5;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidationPrecedesWritesAndWriterExceptionsExposeOnlyAcceptedPrefix(int optimization)
        => Run(Counters+"""
            int main() {
                char[32] storage;
                {
                    TrackingWriter sink=new(storage,1);ITextWriter* output=&sink;
                    char[4] invalid=['a','b','c',(char)255];
                    try {output.WriteJsonString(invalid,4);return 1;}catch(ref Exception error) {}
                    if(sink.Calls!=0||sink.Length!=0)return 2;
                }
                readonly(char)* expected="\"ab\\\"cd\\n\"";
                for(int fail=1;fail<=6;fail++) {
                    TrackingWriter sink=new(storage,fail);ITextWriter* output=&sink;
                    try {output.WriteJsonString("ab\"cd\n",6);return 3;}
                    catch(ref Exception error) {if(error.GetCode()!=77)return 4;}
                    nuint accepted=0;
                    if(fail>1)accepted=(nuint)(fail*2-3);
                    if(sink.Calls!=fail||sink.Length!=accepted)return 5;
                    for(nuint i=0;i<accepted;i++)if(storage[i]!=expected[i])return 6;
                }
                if(destroyed!=7||allocations!=releases)return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BufferOwningStringAndFileWritersComposeWithIndependentJsonRoundTrip(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-writers-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"quoted.json");
            string text="hello\0\n\t\"\\ / < > \uffff😀";byte[] bytes=Encoding.UTF8.GetBytes(text);
            string assignments=string.Join("\n",bytes.Select((value,index)=>$"input[{index}]=(char){value};"));
            Run(Counters+$$"""
                int main() {
                    char[{{bytes.Length}}] input;{{assignments}}
                    char[256] storage;BufferWriter buffer=new(storage,256);
                    ITextWriter* buffered=&buffer;buffered.WriteJsonString(input,{{bytes.Length}});
                    if(allocations!=0||releases!=0)return 1;
                    {
                        String text=new();text.Reserve(256);
                        int baseline=allocations;
                        ITextWriter* owning=&text;owning.WriteJsonString(input,{{bytes.Length}});
                        if(allocations!=baseline||text.Length!=buffer.Length)return 2;
                        for(nuint i=0;i<text.Length;i++)if(text.Data[i]!=buffer.Data[i])return 3;
                        File file=File::OpenWrite({{Literal(path)}});ITextWriter* output=&file;
                        output.WriteJsonString(input,{{bytes.Length}});file.Close();
                        if(allocations!=baseline)return 4;
                    }
                    if(allocations!=releases)return 5;
                    return 0;
                }
                """,optimization,true);
            using var document=JsonDocument.Parse(File.ReadAllBytes(path));Assert.Equal(text,document.RootElement.GetString());
        }
        finally {Directory.Delete(directory,true);}
    }

    private static string Literal(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";

    private static void Run(string code,int optimization,bool file=false)
    {
        var names=new List<string>{"core/Utf8.gf","core/JsonString.gf","core/JsonStringWriter.gf"};
        if(file)names.AddRange(["core/IOException.gf","core/EndOfStreamException.gf","core/BufferTooSmallException.gf","core/BufferWriter.gf",
            "core/IStringConvertable.gf","core/IHashable.gf","core/IEquatable.gf","core/IComparable.gf","core/Hashing.gf","core/String.gf","libc/File.gf"]);
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))))
            .Append(new SourceFile("main.gf",code));
        Assert.Equal(0,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }
}
