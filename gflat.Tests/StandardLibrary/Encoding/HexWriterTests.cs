using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class HexWriterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChunkedHexMatchesDotNetForBinaryPayloadsWithNoAdapterAllocation(int optimization)
    {
        var random=new Random(641820);
        List<byte[]> inputs=new[]{0,1,127,128,129,255,256,257,4097,16391}.Select(count=>{byte[] bytes=new byte[count];random.NextBytes(bytes);return bytes;}).ToList();
        inputs.Add(Enumerable.Range(0,256).Select(value=>(byte)value).ToArray());
        inputs.Add([0,255,0,255]);
        string path=Path.Combine(Path.GetTempPath(),"gflat_hex_writer_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var writer=new BinaryWriter(File.Create(path)))foreach(byte[] input in inputs){
                writer.Write(input.Length);writer.Write(input);
                string upper=Convert.ToHexString(input);writer.Write(Encoding.ASCII.GetBytes(upper));writer.Write(Encoding.ASCII.GetBytes(upper.ToLowerInvariant()));
            }
            Run($$"""
                using std; using std::Text; using std::Encoding;int allocations=0;
                namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}public replace void Free(void* storage)nothrow{} }
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);extern nuint fread(void* output,nuint size,nuint count,void* file);extern int fclose(void* file);extern int ferror(void* file);
                struct Sink:ITextWriter{
                    private char* output;public nuint Length;public int Calls;
                    public Sink(char* data){output=data;Length=0;Calls=0;}
                    public void Write(readonly(char)* data,nuint count){
                        if(count==0||count>256)throw new Exception("invalid chunk");Calls++;
                        for(nuint i=0;i<count;i++)output[Length+i]=data[i];Length+=count;
                    }
                }
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    char[20000] input;char[40000] upper;char[40000] lower;char[40002] output;
                    for(int sample=0;sample<{{inputs.Count}};sample++){
                        int size=0;if(fread(&size,4,1,file!)!=1||size<0||size>20000)return 2;nuint count=(nuint)size;
                        if(fread((void*)&input[0],1,count,file!)!=count||fread((void*)&upper[0],1,count*2,file!)!=count*2||fread((void*)&lower[0],1,count*2,file!)!=count*2)return 3;
                        for(int mode=0;mode<3;mode++){
                            for(nuint i=0;i<40002;i++)output[i]='Z';Sink sink=new(&output[1]);ITextWriter* writer=&sink;
                            if(mode==0)writer.WriteHex(&input[0],count);else writer.WriteHex(&input[0],count,mode==1);
                            if(sink.Length!=count*2||sink.Calls!=(int)((count+127)/128)||output[0]!='Z'||output[1+count*2]!='Z'||output[40001]!='Z')return 4;
                            for(nuint i=0;i<count*2;i++){char expected=upper[i];if(mode==2)expected=lower[i];if(output[i+1]!=expected)return 5;}
                        }
                    }
                    if(ferror(file!)!=0||allocations!=0)return 6;return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WriterFailuresKeepOnlyAcceptedPrefixesAndLaterCallsStartFresh(int optimization)
        =>Run("""
            using std; using std::Text; using std::Encoding;
            struct Sink:ITextWriter{
                private char* output;public nuint Length;public int Calls;public int FailAt;
                public Sink(char* data,int failure){output=data;Length=0;Calls=0;FailAt=failure;}
                public void Write(readonly(char)* data,nuint count){
                    Calls++;if(Calls==FailAt)throw new Exception("sink failure",73);
                    for(nuint i=0;i<count;i++)output[Length+i]=data[i];Length+=count;
                }
            }
            int main(){
                char[300] input;for(nuint i=0;i<300;i++)input[i]=(char)255;char[700] output;char[2] payload=['\0',(char)255];
                for(int failure=1;failure<=3;failure++){
                    for(nuint i=0;i<700;i++)output[i]='Z';Sink sink=new(&output[1],failure);ITextWriter* writer=&sink;bool caught=false;
                    try{writer.WriteHex(&input[0],300);}catch(ref Exception error){if(error.GetCode()!=73)return 1;readonly(char)* text=error.GetMessage();if(text[0]!='s'||text[1]!='i')return 2;caught=true;}
                    nuint prefix=(nuint)(failure-1)*256;
                    if(!caught||sink.Calls!=failure||sink.Length!=prefix||output[0]!='Z'||output[1+prefix]!='Z')return 3;
                    for(nuint i=0;i<prefix;i++)if(output[1+i]!='F')return 4;
                    sink.FailAt=0;writer.WriteHex(&payload[0],2,false);
                    if(sink.Calls!=failure+1||sink.Length!=prefix+4||output[1+prefix]!='0'||output[2+prefix]!='0'||output[3+prefix]!='f'||output[4+prefix]!='f'||output[5+prefix]!='Z')return 5;
                    writer.WriteHex((readonly(char)*)1,0);if(sink.Calls!=failure+1)return 6;
                }
                String text=s"prefix:";ITextWriter* writer=&text;writer.WriteHex(&payload[0],2);writer.WriteHex(&payload[0],2,false);
                if(text.Length!=15||!CString::Equals(text.Data,"prefix:00FF00ff"))return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EmptyAndImpossibleLengthsNeverTouchInputOrWriterWithOnlyCoreContracts(int optimization)
    {
        const string program="""
            using std; using std::Text; using std::Encoding;
            namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
            struct Sink:ITextWriter{public int Calls;public Sink(){Calls=0;}public void Write(readonly(char)* data,nuint count){Calls++;throw new Exception("touched writer",77);}}
            int main(){
                Sink sink=new();ITextWriter* writer=&sink;writer.WriteHex((readonly(char)*)1,0);writer.WriteHex((readonly(char)*)1,0,false);
                if(sink.Calls!=0)return 1;nuint maximum=(nuint)0-(nuint)1;nuint[2] counts=[maximum/2+1,maximum];
                for(nuint i=0;i<2;i++){
                    bool caught=false;try{writer.WriteHex((readonly(char)*)1,counts[i]);}catch(ref Exception error){
                        readonly(char)* message=error.GetMessage();readonly(char)* expected="Hex length overflow";
                        for(nuint at=0;at<20;at++)if(message[at]!=expected[at])return 2;caught=true;
                    }
                    if(!caught||sink.Calls!=0)return 3;
                }
                return 0;
            }
            """;
        string[] names=["Encoding/Hex.gf","Encoding/hex_writer.gf","Text.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("hex-writer-contract.gf",program));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,InternalizeFunctions=true});
        Assert.DoesNotContain("gflat$String$",ir);
        string directory=Path.Combine(Path.GetTempPath(),"gflat_hex_writer_core_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string input=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");File.WriteAllText(input,ir);
            var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var result=NativeToolchain.Run(output,[]);Assert.True(result.ExitCode==0,$"Hex writer selective exit {result.ExitCode}: {result.StandardError}");
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("hex-writer.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Hex writer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
