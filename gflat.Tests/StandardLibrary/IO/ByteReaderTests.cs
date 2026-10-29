namespace gflat.Tests.Std.IO;

public sealed class ByteReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChunkedSourcesMatchHostBytesWithExactReadsCopyAndStackScratch(int optimization)
    {
        byte[] bytes = Enumerable.Range(0, 513).Select(index => (byte)(index * 73 + 19)).ToArray();
        string values = string.Join(",", bytes.Select(value => "(char)" + value));
        Run("""
            using std;using std::IO;
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){attempts++;return null;}
                public replace void Free(void* storage)nothrow{}
            }
            struct Source:IByteReader{
                private readonly(char)* data;private nuint length;private nuint chunk;
                public nuint Position;public nuint Calls;public nuint MaxRequest;
                public Source(readonly(char)* input,nuint size,nuint block){data=input;length=size;chunk=block;Position=0;Calls=0;MaxRequest=0;}
                public nuint Read(char* output,nuint count){
                    Calls++;if(count>MaxRequest)MaxRequest=count;nuint take=length-Position;if(take>count)take=count;if(take>chunk)take=chunk;
                    for(nuint i=0;i<take;i++)output[i]=data[Position+i];Position+=take;return take;
                }
            }
            struct Sink:ITextWriter{
                private char* data;public nuint Length;public nuint Calls;
                public Sink(char* output){data=output;Length=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    if(count==0||count>513-Length)throw new Exception("Unexpected sink count");
                    for(nuint i=0;i<count;i++)data[Length+i]=input[i];Length+=count;Calls++;
                }
            }
            int main(){
                char[513] input=[VALUES];nuint[8] lengths=[0,1,2,3,7,255,256,513];
                nuint[5] chunks=[1,2,3,17,1024];nuint[5] capacities=[1,2,7,257,513];
                for(nuint n=0;n<8;n++)for(nuint c=0;c<5;c++){
                    nuint count=lengths[n];char[515] output;for(nuint i=0;i<515;i++)output[i]='?';
                    Source source=new(input,count,chunks[c]);IByteReader* reader=&source;
                    reader.ReadExactly(&output[1],count);
                    if(source.Position!=count||source.Calls==0||output[0]!='?'||output[count+1]!='?')return 1;
                    for(nuint i=0;i<count;i++)if(output[i+1]!=input[i])return 2;
                    for(nuint b=0;b<5;b++){
                        for(nuint i=0;i<515;i++)output[i]='?';char[515] scratch;
                        for(nuint i=0;i<515;i++)scratch[i]='!';
                        Source next=new(input,count,chunks[c]);Sink sink=new(&output[1]);
                        if(CopyTo(&next,&sink,&scratch[1],capacities[b])!=count)return 3;
                        if(next.Position!=count||sink.Length!=count||output[0]!='?'||output[count+1]!='?')return 4;
                        if(scratch[0]!='!'||scratch[capacities[b]+1]!='!')return 5;
                        for(nuint i=0;i<count;i++)if(output[i+1]!=input[i])return 6;
                        if(count==0&&sink.Calls!=0)return 7;
                    }
                    Source stackSource=new(input,count,chunks[c]);Sink stackSink=new(&output[1]);
                    if(CopyTo(&stackSource,&stackSink)!=count||stackSink.Length!=count||stackSource.MaxRequest!=4096)return 8;
                    for(nuint i=0;i<count;i++)if(output[i+1]!=input[i])return 9;
                    if(output[0]!='?'||output[count+1]!='?')return 11;
                }
                if(attempts!=0)return 10;return 0;
            }
            """.Replace("VALUES", values), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailureBoundariesPreserveGuardsAndDoNotRetryConsumedPrefixes(int optimization)
        => Run("""
            using std;using std::IO;
            struct Source:IByteReader{
                public nuint Calls;public nuint Position;public int Mode;
                public Source(int mode){Calls=0;Position=0;Mode=mode;}
                public nuint Read(char* output,nuint count){
                    Calls++;
                    if(Mode==1)throw new IOException("Closed source");if(Mode==4)return 1;
                    if(count==0)return 0;
                    if(Mode==2){if(Calls==1)return count+1;return 0;}
                    if(Mode==3&&Calls==2){output[0]='X';Position++;throw new IOException("Source failure");}
                    if(Position==3)return 0;
                    nuint take=3-Position;if(take>count)take=count;if(take>2)take=2;
                    for(nuint i=0;i<take;i++)output[i]=(char)(65+Position+i);Position+=take;return take;
                }
            }
            struct Sink:ITextWriter{
                public nuint Calls;public nuint Count;public bool Fail;
                public Sink(bool fail){Calls=0;Count=0;Fail=fail;}
                public void Write(readonly(char)* input,nuint count){Calls++;if(Fail){Count++;throw new IOException("Sink failure");}Count+=count;}
            }
            int main(){
                Source empty=new(0);ReadExactly(&empty,(char*)1,0);if(empty.Calls!=1||empty.Position!=0)return 1;
                Source closed=new(1);bool caught=false;
                try{ReadExactly(&closed,(char*)1,0);}catch(ref IOException error){caught=true;}
                if(!caught||closed.Calls!=1)return 2;
                Source invalidZero=new(4);caught=false;
                try{ReadExactly(&invalidZero,(char*)1,0);}catch(ref IOException error){caught=true;}
                if(!caught||invalidZero.Calls!=1)return 11;
                char[7] bytes;for(nuint i=0;i<7;i++)bytes[i]='?';Source shortInput=new(0);caught=false;
                try{ReadExactly(&shortInput,&bytes[1],5);}catch(ref EndOfStreamException error){caught=true;}
                if(!caught||shortInput.Position!=3||shortInput.Calls!=3||bytes[0]!='?'||bytes[1]!='A'||bytes[2]!='B'||bytes[3]!='C'||bytes[4]!='?'||bytes[6]!='?')return 3;
                Source invalid=new(2);Sink untouched=new(false);caught=false;
                try{CopyTo(&invalid,&untouched,bytes,3);}catch(ref IOException error){caught=true;}
                if(!caught||invalid.Calls!=1||untouched.Calls!=0)return 4;
                invalid=new(2);caught=false;
                try{ReadExactly(&invalid,bytes,3);}catch(ref IOException error){caught=true;}
                if(!caught||invalid.Calls!=1)return 5;
                Source noRead=new(0);caught=false;
                try{CopyTo(&noRead,&untouched,(char*)1,0);}catch(ref IOException error){caught=true;}
                if(!caught||noRead.Calls!=0||untouched.Calls!=0)return 6;
                Source failure=new(3);Sink beforeError=new(false);caught=false;
                try{CopyTo(&failure,&beforeError,&bytes[1],2);}catch(ref IOException error){caught=true;}
                if(!caught||failure.Calls!=2||failure.Position!=3||beforeError.Calls!=1||beforeError.Count!=2||bytes[1]!='X'||bytes[0]!='?')return 7;
                Source readAhead=new(0);Sink broken=new(true);caught=false;
                try{CopyTo(&readAhead,&broken,&bytes[1],2);}catch(ref IOException error){caught=true;}
                if(!caught||readAhead.Calls!=1||readAhead.Position!=2||broken.Calls!=1||broken.Count!=1)return 8;
                Source exactFailure=new(3);caught=false;
                try{ReadExactly(&exactFailure,&bytes[1],3);}catch(ref IOException error){caught=true;}
                if(!caught||exactFailure.Calls!=2||exactFailure.Position!=3||bytes[1]!='A'||bytes[2]!='B'||bytes[3]!='X')return 9;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedFileThroughHexDecoderPreservesOwnershipAndBinaryBytes(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-byte-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool passed = false;
        try
        {
            byte[] expected = Enumerable.Range(0, 257).Select(index => (byte)(index * 53)).ToArray();
            string path = Path.Combine(directory, "input.hex");
            File.WriteAllText(path, Convert.ToHexString(expected));
            string wanted = string.Join(",", expected.Select(value => "(char)" + value));
            Run("""
                using std;using std::IO;using std::Text;using std::Encoding;
                int attempts=0;
                namespace Memory{
                    public replace void*? Allocate(nuint size){attempts++;return null;}
                    public replace void Free(void* storage)nothrow{}
                }
                int main(){
                    File file=File::OpenRead("PATH");IByteReader* input=&file;
                    char[260] output;output[0]='?';output[259]='?';BufferWriter sink=new(&output[1],258);
                    HexDecoder decoder=new(&sink);char[9] scratch;for(nuint i=0;i<9;i++)scratch[i]='!';
                    if(CopyTo(input,&decoder,&scratch[1],7)!=514)return 1;
                    decoder.Complete();if(!file.IsOpen||sink.Length!=257||output[0]!='?'||output[258]!='\0'||output[259]!='?'||scratch[0]!='!'||scratch[8]!='!')return 2;
                    char[257] expected=[VALUES];for(nuint i=0;i<257;i++)if(output[i+1]!=expected[i])return 3;
                    ReadExactly(input,(char*)1,0);if(!file.IsOpen)return 4;
                    if(attempts!=0)return 6;file.Close();bool caught=false;
                    try{ReadExactly(input,(char*)1,0);}catch(ref IOException error){caught=true;}
                    if(!caught||file.IsOpen)return 5;
                    return 0;
                }
                """.Replace("PATH", path.Replace("\\", "/")).Replace("VALUES", wanted), optimization);
            passed = true;
        }
        catch (Exception error)
        {
            error.Data["ByteReaderFileDirectory"] = directory;
            throw;
        }
        finally
        {
            if (passed) Directory.Delete(directory, recursive: true);
        }
    }

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("byte-reader-tests.gf", code));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
