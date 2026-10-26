using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardCountingWriterTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("counting-writer.gf",program));

    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Counting writer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private const string DenyAllocation="""
        using std; using std::IO; using std::Encoding;int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IgnoredPointersCheckedOverflowClearAndCopiesStayNonthrowingAndAllocationFree(int optimization)
        => Run(DenyAllocation+"""
            bool Exercise() nothrow {
                CountingWriter writer=new();nuint maximum=(nuint)0-(nuint)1;
                if(writer.Length!=0||!writer.TryWrite((readonly(char)*)1,0))return false;
                for(nuint i=1;i<=1000;i++)if(!writer.TryWrite((readonly(char)*)1,i))return false;
                if(writer.Length!=500500)return false;
                CountingWriter copy=writer;writer.Clear();
                if(writer.Length!=0||copy.Length!=500500)return false;
                if(!writer.TryWrite((readonly(char)*)1,maximum-3))return false;
                if(writer.TryWrite((readonly(char)*)1,4)||writer.Length!=maximum-3)return false;
                if(!writer.TryWrite((readonly(char)*)1,3)||writer.Length!=maximum)return false;
                if(writer.TryWrite((readonly(char)*)1,1)||!writer.TryWrite((readonly(char)*)1,0)||writer.Length!=maximum)return false;
                writer.Clear();if(!writer.TryWrite((readonly(char)*)1,maximum)||writer.Length!=maximum)return false;
                copy.Clear();if(copy.Length!=0||writer.Length!=maximum)return false;
                CountingWriter moved=move copy;
                return moved.Length==0&&writer.Length==maximum;
            }
            int main(){if(!Exercise()||attempts!=0)return 1;return 0;}
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingOverflowPreservesPreviousCountAndDoesNotPoisonWriter(int optimization)
        => Run("""
            using std; using std::IO; using std::Encoding;
            int main(){
                CountingWriter writer=new();nuint maximum=(nuint)0-(nuint)1;
                writer.Write((readonly(char)*)1,maximum-1);int caught=0;
                try{writer.Write((readonly(char)*)1,2);}catch(Exception* error){caught++;}
                if(caught!=1||writer.Length!=maximum-1)return 1;
                writer.Write((readonly(char)*)1,1);
                if(writer.Length!=maximum)return 2;
                try{writer.Write((readonly(char)*)1,1);}catch(Exception* error){caught++;}
                if(caught!=2||writer.Length!=maximum)return 3;
                writer.Write((readonly(char)*)1,0);writer.Clear();
                ITextWriter* output=&writer;output.Write((readonly(char)*)1,123);
                if(writer.Length!=123)return 4;return 0;
            }
            """,optimization);

    [Fact]
    public void TryOnlyCodeHasNoExceptionBoundary()
    {
        string ir=Compiler.Emit(Sources("""
            using std; using std::IO; using std::Encoding;
            bool Inspect(nuint count) nothrow {
                CountingWriter writer=new();
                bool accepted=writer.TryWrite((readonly(char)*)1,count);
                writer.Clear();return accepted&&writer.Length==0;
            }
            int main()=>0;
            """));
        var function=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Inspect\([^\r\n]*\{\r?\n(?<body>.*?)^\}");
        Assert.True(function.Success);
        Assert.DoesNotContain("invoke",function.Value);
        Assert.DoesNotContain("personality",function.Value);
        Assert.DoesNotContain("__gflat_eh_",function.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveCustomAndCodecFormattingCountsMatchRealCountedOutput(int optimization)
        => Run(DenyAllocation+"""
            struct Message:IFormattable {
                private int number;
                public Message(int value){number=value;}
                public immutable void Format(ITextWriter* output){
                    output.Write("A\0",2);number.Format(output);output.Write("!",1);
                }
            }
            void Render(ITextWriter* output){
                int number=-42;number.Format(output);output.Write("|",1);
                bool flag=true;flag.Format(output);output.Write("|",1);
                Message message=new(17);IFormattable* item=&message;item.Format(output);
                char end='\0';end.Format(output);
            }
            int main(){
                CountingWriter count=new();Render(&count);
                char[128] storage;BufferWriter actual=new(storage,128);Render(&actual);
                if(count.Length!=actual.Length||count.Length!=15)return 1;
                if(actual.Data[10]!='\0'||actual.Data[14]!='\0')return 2;
                count.Clear();actual.Clear();
                char[256] binary;for(nuint i=0;i<256;i++)binary[i]=(char)i;
                Base64Encoder measured=new(&count);
                // Use a sufficiently large real destination for the encoded payload.
                char[400] encoded;BufferWriter encodedOutput=new(encoded,400);
                Base64Encoder realEncoder=new(&encodedOutput);
                measured.Write(binary,1);measured.Write((readonly(char)*)binary+1,255);measured.Complete();
                realEncoder.Write(binary,256);realEncoder.Complete();
                if(count.Length!=encodedOutput.Length||count.Length!=344)return 3;
                count.Clear();char[257] decoded;BufferWriter decodedOutput=new(decoded,257);
                Base64Decoder measuredDecode=new(&count);Base64Decoder realDecode=new(&decodedOutput);
                measuredDecode.Write(encodedOutput.Data,3);
                measuredDecode.Write(encodedOutput.Data+3,encodedOutput.Length-3);measuredDecode.Complete();
                realDecode.Write(encodedOutput.Data,encodedOutput.Length);realDecode.Complete();
                if(count.Length!=decodedOutput.Length||count.Length!=256)return 4;
                for(nuint i=0;i<256;i++)if(decodedOutput.Data[i]!=(char)i)return 5;
                if(attempts!=0)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FileDecoderPipelineCountsValidatedBytesWithoutStoringDecodedInput(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),"gflat-counting-writer-"+Guid.NewGuid().ToString("N")+".txt");
        try
        {
            byte[] bytes=new byte[65537];new Random(57493).NextBytes(bytes);
            byte[] encoded=Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes));File.WriteAllBytes(path,encoded);
            Run(DenyAllocation+"""
                int main(){
                    File input=File::OpenRead("PATH");CountingWriter count=new();
                    Base64Decoder decoder=new(&count);char[257] buffer;
                    if(input.CopyTo(&decoder,buffer,257)!=ENCODED)return 1;
                    decoder.Complete();input.Close();
                    if(count.Length!=65537||attempts!=0)return 2;return 0;
                }
                """.Replace("PATH",path.Replace("\\","/")).Replace("ENCODED",encoded.Length.ToString()),optimization);
        }
        finally{File.Delete(path);}
    }
}
