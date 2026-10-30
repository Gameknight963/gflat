using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class Base64EncoderTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("base64-encoder.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0,
            $"Base64 writer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private const string DenyAllocation = """
        using std; using std::IO; using std::Encoding;
        int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArbitraryChunksMatchDotNetAcrossEverySmallPartitionAndBlockBoundary(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),$"gflat-base64-stream-{Guid.NewGuid():N}.bin");
        try
        {
            int cases=0;var random=new Random(61357);
            using(var fixture=new BinaryWriter(File.Create(path)))
            {
                Write([],[]);
                for(int length=1;length<=8;length++)
                {
                    byte[] data=Enumerable.Range(0,length).Select(i=>(byte)(i*71+length)).ToArray();
                    for(int mask=0;mask<(1<<(length-1));mask++)
                    {
                        var chunks=new List<int>();int start=0;
                        for(int index=1;index<length;index++)
                            if((mask&(1<<(index-1)))!=0){chunks.Add(index-start);start=index;}
                        chunks.Add(length-start);Write(data,chunks.ToArray());
                    }
                }
                foreach(int length in new[]{189,190,191,192,193,194,195,255,256,257,383,384,385,4095,4096,4097,16385})
                {
                    byte[] data=new byte[length];random.NextBytes(data);
                    Write(data,[length]);Write(data,Enumerable.Repeat(1,length).ToArray());
                    for(int first=1;first<=3;first++)Write(data,[first,length-first]);
                }
                Write(Enumerable.Range(0,256).Select(i=>(byte)i).ToArray(),[1,2,190,63]);
                for(int sample=0;sample<100;sample++)
                {
                    byte[] data=new byte[random.Next(1,8193)];random.NextBytes(data);
                    var chunks=new List<int>();int position=0;
                    while(position<data.Length){int amount=Math.Min(data.Length-position,random.Next(1,401));chunks.Add(amount);position+=amount;}
                    Write(data,chunks.ToArray());
                }
                void Write(byte[] data,int[] chunks)
                {
                    Assert.Equal(data.Length,chunks.Sum());
                    byte[] expected=Encoding.ASCII.GetBytes(Convert.ToBase64String(data));
                    fixture.Write(data.Length);fixture.Write(chunks.Length);
                    foreach(int chunk in chunks)fixture.Write(chunk);
                    fixture.Write(data);fixture.Write(expected.Length);fixture.Write(expected);cases++;
                }
            }
            Run(DenyAllocation+"""
                extern void* fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* data,nuint size,nuint count,void* stream);
                extern int fclose(void* stream);
                int ReadInt(void* stream){int value=0;if(fread((void*)&value,4,1,stream)!=1)return -1;return value;}
                struct Sink:ITextWriter {
                    private char* data;public nuint Count;public int Calls;
                    public Sink(char* storage){data=storage;Count=0;Calls=0;}
                    public void Write(readonly(char)* text,nuint count){
                        if(count==0||count>256||Count+count>32768)throw new Exception("Invalid encoded block");
                        Calls++;for(nuint i=0;i<count;i++)data[Count+i]=text[i];Count+=count;
                    }
                }
                int main(){
                    void* stream=fopen("PATH","rb");
                    char[16385] input;char[32768] output;char[32768] expected;int[16385] chunks;
                    for(int sample=0;sample<CASES;sample++){
                        int size=ReadInt(stream);int count=ReadInt(stream);
                        if(size<0||size>16385||count<0||count>16385)return 1;
                        for(int i=0;i<count;i++){chunks[i]=ReadInt(stream);if(chunks[i]<=0)return 2;}
                        if(fread((void*)(char*)input,1,(nuint)size,stream)!=(nuint)size)return 3;
                        int encodedSize=ReadInt(stream);
                        if(encodedSize<0||encodedSize>32768)return 4;
                        if(fread((void*)(char*)expected,1,(nuint)encodedSize,stream)!=(nuint)encodedSize)return 5;
                        Sink sink=new(output);Base64Encoder writer=new(&sink);nuint position=0;
                        writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                        for(int i=0;i<count;i++){
                            writer.Write((readonly(char)*)input+position,(nuint)chunks[i]);position+=(nuint)chunks[i];
                            writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                        }
                        // Padding must be deferred, and no input pointer can survive Write.
                        for(nuint i=0;i<sink.Count;i++)if(output[i]=='=')return 6;
                        for(nuint i=0;i<(nuint)size;i++)input[i]='?';
                        writer.Complete();int calls=sink.Calls;writer.Complete();
                        if(calls!=sink.Calls||sink.Count!=(nuint)encodedSize)return 7;
                        for(nuint i=0;i<sink.Count;i++)if(output[i]!=expected[i])return 8;
                    }
                    if(attempts!=0)return 9;return fclose(stream);
                }
                """.Replace("PATH",path.Replace("\\","/")).Replace("CASES",cases.ToString()),optimization);
        }
        finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SinkFailuresDuringFullGroupsAndFinalPaddingLatchWithoutReplacingOriginalException(int optimization)
        => Run("""
            using std; using std::IO; using std::Encoding;
            class SinkFailure:Exception{public SinkFailure():base("sink failed",77){}}
            struct Sink:ITextWriter {
                public int Budget;public int Calls;public int Count;
                public Sink(int budget){Budget=budget;Calls=0;Count=0;}
                public void Write(readonly(char)* data,nuint count){
                    Calls++;for(nuint i=0;i<count;i++){
                        if(Budget==0)throw new SinkFailure();Budget--;Count++;
                    }
                }
            }
            bool Scenario(int budget,bool large){
                Sink sink=new(budget);Base64Encoder writer=new(&sink);int caught=0;
                try{
                    if(large){char[195] data;for(nuint i=0;i<195;i++)data[i]=(char)i;writer.Write(data,195);}
                    else{writer.Write("a",1);writer.Write("bcdef",5);writer.Write("g",1);}
                    writer.Complete();
                }catch(SinkFailure* error){if(error.GetCode()!=77)return false;caught++;}
                catch{return false;}
                if(caught!=1||sink.Count!=budget)return false;
                int calls=sink.Calls;sink.Budget=999;
                try{writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(Exception* error){caught++;}
                try{writer.Write("later",5);}catch(Exception* error){caught++;}
                try{writer.Complete();}catch(Exception* error){caught++;}
                Base64Encoder copy=writer;
                try{copy.Complete();}catch(Exception* error){caught++;}
                return caught==5&&sink.Calls==calls&&sink.Count==budget;
            }
            int main(){
                for(int budget=0;budget<12;budget++)if(!Scenario(budget,false))return budget+1;
                if(!Scenario(0,true)||!Scenario(255,true)||!Scenario(256,true)||!Scenario(259,true))return 20;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiedCarryMovesIndependentlyAndScopeExitNeverCompletesOrDisposesDestination(int optimization)
        => Run("""
            using std; using std::IO; using std::Encoding;
            struct Sink:ITextWriter {
                private char* data;public nuint Count;public int Calls;
                public Sink(char* storage){data=storage;Count=0;Calls=0;}
                public void Write(readonly(char)* text,nuint count){Calls++;for(nuint i=0;i<count;i++)data[Count+i]=text[i];Count+=count;}
            }
            int main(){
                char[64] output;
                for(nuint carry=1;carry<=2;carry++){
                Sink sink=new(output);
                {
                    char[2] source=['a','b'];Base64Encoder writer=new(&sink);writer.Write(source,carry);
                    Base64Encoder copy=writer;source[0]='x';source[1]='y';Base64Encoder moved=move copy;
                    readonly(char)* original="abc";moved.Write(original+carry,3-carry);moved.Complete();
                    if(sink.Count!=4||output[0]!='Y'||output[1]!='W'||output[2]!='J'||output[3]!='j')return 1;
                    Base64Encoder complete=moved;int calls=sink.Calls;complete.Complete();
                    int caught=0;try{complete.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(Exception* error){caught++;}
                    try{moved.Write("x",1);}catch(Exception* error){caught++;}
                    if(caught!=2||sink.Calls!=calls)return 2;
                    // Original writer still has a pending byte; its destruction must do nothing.
                }
                if(sink.Count!=4)return 3;
                {Base64Encoder abandoned=new(&sink);abandoned.Write("de",2);}
                if(sink.Count!=4)return 4;
                sink.Write("!",1);if(sink.Count!=5||output[4]!='!')return 5;
                Base64Encoder empty=new(&sink);int calls=sink.Calls;
                empty.Write(((nuint)1).reinterpret<readonly(char)*>(),0);empty.Complete();empty.Complete();
                if(sink.Calls!=calls)return 6;
                try{empty.Write(((nuint)1).reinterpret<readonly(char)*>(),0);return 7;}catch(Exception* error){}
                if(sink.Calls!=calls)return 8;
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BinaryFileCopyToFeedsWriterWithoutOwningWholeInputOrAllocatingEncodedString(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-base64-pipeline-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] data=new byte[65537];new Random(43067).NextBytes(data);
            string input=Path.Combine(directory,"input.bin"),output=Path.Combine(directory,"output.txt");
            File.WriteAllBytes(input,data);
            Run(DenyAllocation+"""
                int main(){
                    File input=File::OpenRead("INPUT");File output=File::OpenWrite("OUTPUT");
                    Base64Encoder writer=new(&output);char[257] buffer;
                    if(input.CopyTo(&writer,buffer,257)!=65537)return 1;
                    writer.Complete();input.Close();output.Close();
                    if(attempts!=0)return 2;return 0;
                }
                """.Replace("INPUT",input.Replace("\\","/")).Replace("OUTPUT",output.Replace("\\","/")),optimization);
            Assert.Equal(Encoding.ASCII.GetBytes(Convert.ToBase64String(data)),File.ReadAllBytes(output));
        }
        finally{Directory.Delete(directory,true);}
    }
}
