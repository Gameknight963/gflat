namespace gflat.Tests.Std.Text;

public sealed class BufferedWriterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BinaryChunksMatchExpectedBytesAndDrainSizesAcrossSmallCapacities(int optimization)
        => Run("""
            using Standard;using Standard::Text;
            struct Sink:ITextWriter{
                private char* bytes;private nuint* sizes;public nuint Length;public nuint Calls;
                public Sink(char* storage,nuint* counts){bytes=storage;sizes=counts;Length=0;Calls=0;}
                public void Write(readonly(char)* data,nuint count){
                    sizes[Calls]=count;Calls++;
                    for(nuint i=0;i<count;i++)bytes[Length+i]=data[i];Length+=count;
                }
            }
            int main(){
                char[33] input;for(nuint i=0;i<33;i++)input[i]=(char)(i*29);
                nuint[4] capacities=[0,1,3,8];nuint[11] lengths=[0,1,2,3,4,7,8,9,16,17,33];
                for(nuint c=0;c<4;c++)for(nuint n=0;n<11;n++)for(nuint split=0;split<=lengths[n];split++){
                    nuint capacity=capacities[c];nuint count=lengths[n];char[64] bytes;char[10] scratch;nuint[64] sizes;
                    for(nuint i=0;i<10;i++)scratch[i]='?';
                    Sink sink=new(bytes,sizes);char* storage=&scratch[1];if(capacity==0)storage=((nuint)1).reinterpret<char*>();
                    BufferedWriter writer=new(&sink,storage,capacity);
                    writer.Flush();writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                    if(sink.Calls!=0)return 1;
                    writer.Write(input,split);writer.Write((readonly(char)*)input+split,count-split);
                    nuint before=count;if(capacity!=0)before=(count/capacity)*capacity;
                    if(sink.Length!=before)return 2;
                    writer.Flush();nuint calls=sink.Calls;writer.Flush();writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                    if(sink.Length!=count||sink.Calls!=calls)return 3;
                    for(nuint i=0;i<count;i++)if(bytes[i]!=input[i]||input[i]!=(char)(i*29))return 4;
                    if(capacity==0){
                        nuint expected=0;if(split!=0)expected++;if(count!=split)expected++;
                        if(calls!=expected)return 5;
                        for(nuint i=0;i<10;i++)if(scratch[i]!='?')return 6;
                    }else{
                        nuint expected=count/capacity;if(count%capacity!=0)expected++;
                        if(calls!=expected||scratch[0]!='?'||scratch[capacity+1]!='?')return 7;
                        for(nuint i=0;i<calls;i++){
                            nuint take=capacity;if(i+1==calls&&count%capacity!=0)take=count%capacity;
                            if(sizes[i]!=take)return 8;
                        }
                    }
                }
                char[256] all;char[300] output;char[8] block;nuint[64] sizes;
                for(nuint i=0;i<256;i++)all[i]=(char)i;
                Sink sink=new(output,sizes);BufferedWriter writer=new(&sink,block,8);
                writer.Write(all,256);writer.Flush();if(sink.Calls!=32||sink.Length!=256)return 9;
                for(nuint i=0;i<256;i++)if(output[i]!=all[i])return 10;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TinyFormattingWritesCoalesceOnceWithoutAllocationOrFlushingTheSink(int optimization)
        => Run("""
            using Standard;using Standard::Text;
            int attempts=0;int formatted=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){attempts++;return null;}
                public replace void Free(void* storage)nothrow{}
            }
            struct Sink:ITextWriter{
                private char* data;public nuint Length;public int Calls;public int Flushes;
                public Sink(char* storage){data=storage;Length=0;Calls=0;Flushes=0;}
                public void Write(readonly(char)* input,nuint count){Calls++;for(nuint i=0;i<count;i++)data[Length+i]=input[i];Length+=count;}
                public void Flush(){Flushes++;}
            }
            struct Message:IFormattable{
                public Message(){}
                public immutable void Format(ITextWriter* output){formatted++;output.Write("A\0",2);int value=-42;value.Format(output);output.Write("\n",1);}
            }
            int main(){
                char[32] bytes;char[8] scratch;Sink sink=new(bytes);BufferedWriter writer=new(&sink,scratch,8);
                Message value=new();IFormattable* item=&value;item.Format(&writer);
                if(formatted!=1||sink.Calls!=0||sink.Length!=0)return 1;
                writer.Flush();if(sink.Calls!=1||sink.Length!=6||sink.Flushes!=0)return 2;
                char[6] expected=['A','\0','-','4','2','\n'];for(nuint i=0;i<6;i++)if(bytes[i]!=expected[i])return 3;
                CountingWriter count=new();BufferedWriter measured=new(&count,scratch,8);item.Format(&measured);
                if(formatted!=2||count.Length!=0)return 4;measured.Flush();if(count.Length!=6)return 5;
                if(attempts!=0)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PartialSinkFailuresLatchAutomaticExplicitAndPassthroughDrains(int optimization)
        => Run("""
            using Standard;using Standard::Text;
            struct Sink:ITextWriter{
                private char* bytes;public int Calls;public nuint Length;public int FailCall;
                public Sink(char* storage,int failure){bytes=storage;Calls=0;Length=0;FailCall=failure;}
                public void Write(readonly(char)* data,nuint count){
                    Calls++;nuint take=count;if(Calls==FailCall&&take>1)take=1;
                    for(nuint i=0;i<take;i++)bytes[Length+i]=data[i];Length+=take;
                    if(Calls==FailCall)throw new Exception("original sink failure",73);
                }
            }
            int main(){
                for(int mode=0;mode<3;mode++){
                    char[32] bytes;char[4] scratch;int failCall=1;nuint capacity=4;
                    if(mode==0)failCall=2;if(mode==2)capacity=0;
                    Sink sink=new(bytes,failCall);BufferedWriter writer=new(&sink,scratch,capacity);int caught=0;
                    try{
                        if(mode==0)writer.Write("ABCDEFGHIJ",10);
                        else if(mode==1){writer.Write("ABC",3);writer.Flush();}
                        else writer.Write("ABC",3);
                    }catch(ref Exception error){
                        if(error.GetCode()!=73||!CString::Equals(error.GetMessage(),"original sink failure"))return 1;caught++;
                    }
                    nuint accepted=1;if(mode==0)accepted=5;
                    if(caught!=1||sink.Length!=accepted||sink.Calls!=failCall)return 2;
                    for(nuint i=0;i<accepted;i++)if(bytes[i]!=(char)('A'+i))return 3;
                    int calls=sink.Calls;sink.FailCall=0;
                    try{writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch{caught++;}
                    try{writer.Write(((nuint)1).reinterpret<readonly(char)*>(),100);}catch{caught++;}
                    try{writer.Flush();}catch{caught++;}
                    if(caught!=4||sink.Calls!=calls||sink.Length!=accepted)return 4;
                }
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SharedScratchCopiesDuplicateCountsAndMetadataHandoffAddsNoDestructorOutput(int optimization)
        => Run("""
            using Standard;using Standard::Text;
            struct Sink:ITextWriter{
                private char* bytes;private int* destroyed;public nuint Length;public int Calls;public bool Fail;
                public Sink(char* storage,int* counter){bytes=storage;destroyed=counter;Length=0;Calls=0;Fail=false;}
                public void Write(readonly(char)* data,nuint count){Calls++;if(Fail)throw new Exception("sink",71);for(nuint i=0;i<count;i++)bytes[Length+i]=data[i];Length+=count;}
                ~Sink(){(*destroyed)++;}
            }
            int main(){
                int destroyed=0;char[64] bytes;char[8] scratch;
                {
                    Sink sink=new(bytes,&destroyed);
                    {BufferedWriter first=new(&sink,scratch,8);first.Write("ab",2);BufferedWriter copy=first;first.Flush();copy.Flush();}
                    if(sink.Length!=4||sink.Calls!=2||bytes[0]!='a'||bytes[1]!='b'||bytes[2]!='a'||bytes[3]!='b')return 1;
                    {
                        BufferedWriter first=new(&sink,scratch,8);first.Write("ab",2);BufferedWriter copy=first;
                        first.Write("C",1);copy.Write("XY",2);first.Flush();copy.Flush();
                    }
                    // Each position is independent, but both see the current shared scratch.
                    if(sink.Length!=11||bytes[4]!='a'||bytes[5]!='b'||bytes[6]!='X'||bytes[7]!='a'||bytes[8]!='b'||bytes[9]!='X'||bytes[10]!='Y')return 2;
                    {
                        BufferedWriter original=new(&sink,scratch,8);original.Write("pq",2);BufferedWriter handoff=original;
                        BufferedWriter moved=move handoff;BufferedWriter* borrowed=&moved;borrowed.Write("r",1);borrowed.Flush();
                    }
                    if(sink.Length!=14||bytes[11]!='p'||bytes[12]!='q'||bytes[13]!='r'||destroyed!=0)return 3;
                    int calls=sink.Calls;{BufferedWriter dropped=new(&sink,scratch,8);dropped.Write("lost",4);}
                    if(sink.Calls!=calls||sink.Length!=14||destroyed!=0)return 4;
                    {
                        BufferedWriter first=new(&sink,scratch,8);first.Write("z",1);BufferedWriter copy=first;sink.Fail=true;
                        bool caught=false;try{first.Flush();}catch{caught=true;}sink.Fail=false;
                        if(!caught)return 5;copy.Flush();
                        caught=false;try{first.Flush();}catch{caught=true;}if(!caught)return 6;
                    }
                    if(sink.Length!=15||bytes[14]!='z'||destroyed!=0)return 7;
                }
                if(destroyed!=1)return 8;return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("buffered-writer.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"BufferedWriter exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
