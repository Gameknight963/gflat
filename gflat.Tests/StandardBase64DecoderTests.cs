using System.Text;

namespace gflat.Tests;

public sealed class StandardBase64DecoderTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("base64-decoder.gf",program));

    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Base64 decoder exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private const string DenyAllocation="""
        using std;int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage){free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArbitraryTextChunksMatchDotNetIncludingEverySmallPartitionWithoutAllocating(int optimization)
    {
        string path=Path.Combine(Path.GetTempPath(),"gflat-base64-decode-"+Guid.NewGuid().ToString("N")+".bin");
        try
        {
            int cases=0;var random=new Random(50359);
            using(var fixture=new BinaryWriter(File.Create(path)))
            {
                Write([],[]);
                for(int length=1;length<=6;length++)
                {
                    byte[] data=Enumerable.Range(0,length).Select(i=>(byte)(i*73+length)).ToArray();
                    int count=Convert.ToBase64String(data).Length;
                    for(int mask=0;mask<(1<<(count-1));mask++)
                    {
                        var chunks=new List<int>();int start=0;
                        for(int index=1;index<count;index++)
                            if((mask&(1<<(index-1)))!=0){chunks.Add(index-start);start=index;}
                        chunks.Add(count-start);Write(data,chunks.ToArray());
                    }
                }
                foreach(int length in new[]{189,190,191,192,193,194,195,255,256,257,383,384,385,4095,4096,4097,16385})
                {
                    byte[] data=new byte[length];random.NextBytes(data);
                    int count=Convert.ToBase64String(data).Length;
                    Write(data,[count]);Write(data,Enumerable.Repeat(1,count).ToArray());
                    for(int first=1;first<=3;first++)Write(data,[first,count-first]);
                }
                Write(Enumerable.Range(0,256).Select(i=>(byte)i).ToArray(),[1,2,253,88]);
                for(int sample=0;sample<100;sample++)
                {
                    byte[] data=new byte[random.Next(1,8193)];random.NextBytes(data);
                    int count=Convert.ToBase64String(data).Length;var chunks=new List<int>();int position=0;
                    while(position<count){int amount=Math.Min(count-position,random.Next(1,401));chunks.Add(amount);position+=amount;}
                    Write(data,chunks.ToArray());
                }
                void Write(byte[] data,int[] chunks)
                {
                    byte[] text=Encoding.ASCII.GetBytes(Convert.ToBase64String(data));
                    Assert.Equal(text.Length,chunks.Sum());
                    fixture.Write(text.Length);fixture.Write(chunks.Length);
                    foreach(int chunk in chunks)fixture.Write(chunk);
                    fixture.Write(text);fixture.Write(data.Length);fixture.Write(data);cases++;
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
                        if(count==0||count>192||Count+count>16385)throw new Exception("Invalid decoded block");
                        Calls++;for(nuint i=0;i<count;i++)data[Count+i]=text[i];Count+=count;
                    }
                }
                int main(){
                    void* stream=fopen("PATH","rb");
                    char[32768] input;char[16385] output;char[16385] expected;int[32768] chunks;
                    for(int sample=0;sample<CASES;sample++){
                        int size=ReadInt(stream);int count=ReadInt(stream);
                        if(size<0||size>32768||count<0||count>32768)return 1;
                        for(int i=0;i<count;i++){chunks[i]=ReadInt(stream);if(chunks[i]<=0)return 2;}
                        if(fread((void*)(char*)input,1,(nuint)size,stream)!=(nuint)size)return 3;
                        int decodedSize=ReadInt(stream);if(decodedSize<0||decodedSize>16385)return 4;
                        if(fread((void*)(char*)expected,1,(nuint)decodedSize,stream)!=(nuint)decodedSize)return 5;
                        Sink sink=new(output);Base64Decoder decoder=new(&sink);nuint position=0;
                        decoder.Write((readonly(char)*)1,0);
                        for(int i=0;i<count;i++){
                            decoder.Write((readonly(char)*)input+position,(nuint)chunks[i]);
                            // Every consumed chunk may be reused immediately, even before a quartet completes.
                            for(nuint j=0;j<(nuint)chunks[i];j++)input[position+j]='?';
                            position+=(nuint)chunks[i];decoder.Write((readonly(char)*)1,0);
                        }
                        int calls=sink.Calls;decoder.Complete();decoder.Complete();
                        if(sink.Calls!=calls||sink.Count!=(nuint)decodedSize)return 6;
                        for(nuint i=0;i<sink.Count;i++)if(output[i]!=expected[i])return 7;
                    }
                    if(attempts!=0)return 8;return fclose(stream);
                }
                """.Replace("PATH",path.Replace("\\","/")).Replace("CASES",cases.ToString()),optimization);
        }
        finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictInvalidCorpusPoisonsAcrossEverySplitWithoutPromisingRollback(int optimization)
    {
        byte[][] invalid=
        [
            Bytes("A"),Bytes("AA"),Bytes("AAA"),Bytes("AAAAA"),Bytes("Zg"),Bytes("Zg="),
            Bytes("Zg==="),Bytes("===="),Bytes("A==="),Bytes("=AAA"),Bytes("A=AA"),Bytes("AA=A"),
            Bytes("Zh=="),Bytes("Zm9="),Bytes("Zg==AAAA"),Bytes("AAAAZg==AAAA"),
            Bytes("AA-A"),Bytes("AA_A"),Bytes("AAAA\n"),Bytes(" Zg=="),Bytes("Zg==\t"),
            [65,65,0,65],[65,65,255,65],Bytes("!!!!"),
            Bytes(new string('A',260)+"!"),Bytes(new string('A',256)+"Zg==x")
        ];
        var code=new StringBuilder("""
            using std;
            struct Sink:ITextWriter {
                public int Calls;public nuint Count;
                public Sink(){Calls=0;Count=0;}
                public void Write(readonly(char)* data,nuint count){Calls++;Count+=count;}
            }
            bool Check(readonly(char)* input,nuint length,nuint split){
                Sink sink=new();Base64Decoder decoder=new(&sink);int caught=0;
                try{decoder.Write(input,split);decoder.Write(input+split,length-split);decoder.Complete();}
                catch(Exception* error){caught++;}
                if(caught!=1)return false;int calls=sink.Calls;nuint count=sink.Count;
                try{decoder.Write((readonly(char)*)1,0);}catch(Exception* error){caught++;}
                try{decoder.Write("AAAA",4);}catch(Exception* error){caught++;}
                try{decoder.Complete();}catch(Exception* error){caught++;}
                Base64Decoder copy=decoder;
                try{copy.Complete();}catch(Exception* error){caught++;}
                return caught==5&&sink.Calls==calls&&sink.Count==count;
            }
            int main(){
            """);
        foreach(byte[] text in invalid)
        {
            code.AppendLine($"{{char[{text.Length}] input=[{string.Join(",",text.Select(value=>$"(char){value}"))}];");
            code.AppendLine($"for(nuint split=0;split<={text.Length};split++)if(!Check(input,{text.Length},split))return 1;}}");
        }
        code.AppendLine("return 0;}");Run(code.ToString(),optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OriginalPartialSinkFailurePoisonsBothWritesAndCompletion(int optimization)
        => Run("""
            using std;
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
                Sink sink=new(budget);Base64Decoder decoder=new(&sink);int caught=0;
                try{
                    if(large){char[260] data;for(nuint i=0;i<260;i++)data[i]='A';decoder.Write(data,260);}
                    else{decoder.Write("YW",2);decoder.Write("JjZA==",6);}
                    decoder.Complete();
                }catch(SinkFailure* error){if(error.GetCode()!=77)return false;caught++;}
                catch{return false;}
                if(caught!=1||sink.Count!=budget)return false;
                int calls=sink.Calls;sink.Budget=999;
                try{decoder.Write((readonly(char)*)1,0);}catch(Exception* error){caught++;}
                try{decoder.Complete();}catch(Exception* error){caught++;}
                return caught==3&&sink.Calls==calls&&sink.Count==budget;
            }
            int main(){
                for(int budget=0;budget<4;budget++)if(!Scenario(budget,false))return budget+1;
                if(!Scenario(0,true)||!Scenario(191,true)||!Scenario(192,true)||!Scenario(194,true))return 10;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiedCarryAndPaddedCompletionNeverOwnOrCompleteSinkImplicitly(int optimization)
        => Run("""
            using std;
            struct Sink:ITextWriter {
                private char* data;public nuint Count;public int Calls;
                public Sink(char* storage){data=storage;Count=0;Calls=0;}
                public void Write(readonly(char)* text,nuint count){Calls++;for(nuint i=0;i<count;i++)data[Count+i]=text[i];Count+=count;}
            }
            int main(){
                char[64] output;
                for(nuint carry=1;carry<=3;carry++){
                    Sink sink=new(output);
                    {
                        char[4] source=['Y','W','J','j'];Base64Decoder decoder=new(&sink);decoder.Write(source,carry);
                        Base64Decoder copy=decoder;for(nuint i=0;i<4;i++)source[i]='?';
                        Base64Decoder moved=move copy;readonly(char)* original="YWJj";
                        moved.Write(original+carry,4-carry);moved.Complete();
                        if(sink.Count!=3||output[0]!='a'||output[1]!='b'||output[2]!='c')return 1;
                        Base64Decoder complete=moved;int calls=sink.Calls;complete.Complete();
                        try{complete.Write((readonly(char)*)1,0);return 2;}catch(Exception* error){}
                        if(sink.Calls!=calls)return 3;
                    }
                    if(sink.Count!=3)return 4;
                    {Base64Decoder abandoned=new(&sink);abandoned.Write("YQ=",3);}
                    if(sink.Count!=3)return 5;sink.Write("!",1);
                    if(sink.Count!=4||output[3]!='!')return 6;
                    Base64Decoder padded=new(&sink);padded.Write("YQ==",4);
                    padded.Write((readonly(char)*)1,0);padded.Complete();padded.Complete();
                    if(sink.Count!=5||output[4]!='a')return 7;
                    Base64Decoder empty=new(&sink);int calls=sink.Calls;
                    empty.Write((readonly(char)*)1,0);empty.Complete();empty.Complete();
                    if(sink.Calls!=calls)return 8;
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EncodedFileCopyToDecodesDirectlyIntoBinaryFileWithoutWholeInputString(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-base64-decode-pipeline-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] data=new byte[65537];new Random(39703).NextBytes(data);
            string input=Path.Combine(directory,"input.txt"),output=Path.Combine(directory,"output.bin");
            byte[] encoded=Encoding.ASCII.GetBytes(Convert.ToBase64String(data));File.WriteAllBytes(input,encoded);
            Run(DenyAllocation+"""
                int main(){
                    File input=File::OpenRead("INPUT");File output=File::OpenWrite("OUTPUT");
                    Base64Decoder decoder=new(&output);char[257] buffer;
                    if(input.CopyTo(&decoder,buffer,257)!=LENGTH)return 1;
                    decoder.Complete();input.Close();output.Close();
                    if(attempts!=0)return 2;return 0;
                }
                """.Replace("INPUT",input.Replace("\\","/")).Replace("OUTPUT",output.Replace("\\","/"))
                    .Replace("LENGTH",encoded.Length.ToString()),optimization);
            Assert.Equal(data,File.ReadAllBytes(output));
        }
        finally{Directory.Delete(directory,true);}
    }

    private static byte[] Bytes(string text)=>Encoding.ASCII.GetBytes(text);
}
