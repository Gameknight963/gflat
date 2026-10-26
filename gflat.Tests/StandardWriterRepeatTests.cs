namespace gflat.Tests;

public sealed class StandardWriterRepeatTests
{
    private const string Probe = """
        using std; using std::Text;
        int destroyed=0;
        struct Sink : ITextWriter {
            private char* data;
            public nuint Length;
            public int Calls;
            public nuint Largest;
            public int FailAt;
            private nuint partial;
            public Sink(char* storage,int failAt,nuint accepted){
                data=storage;Length=0;Calls=0;Largest=0;FailAt=failAt;partial=accepted;
            }
            public void Write(readonly(char)* text,nuint count){
                Calls++;
                if(count>Largest)Largest=count;
                nuint accepted=count;
                if(Calls==FailAt && partial<accepted)accepted=partial;
                for(nuint i=0;i<accepted;i++)data[Length+i]=text[i];
                Length+=accepted;
                if(Calls==FailAt)throw new Exception("sink rejected bytes",73);
            }
            ~Sink(){destroyed++;}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllBytesAndChunkBoundariesPreserveGuardsWithoutAllocating(int optimization)
        => Run(Probe + """
            int allocations=0;
            replace void*? Memory::Allocate(nuint bytes){allocations++;return null;}
            replace void Memory::Free(void* storage)nothrow{allocations++;}
            int main(){
                char[4100] bytes;
                nuint[9] counts=[0,1,2,63,64,65,128,129,4097];
                int expectedDestroyed=0;
                for(int value=0;value<256;value++){
                    for(nuint row=0;row<9;row++){
                        for(nuint i=0;i<4100;i++)bytes[i]='?';
                        nuint count=counts[row];
                        {
                            Sink sink=new(&bytes[1],-1,0);ITextWriter* writer=&sink;
                            writer.WriteRepeated((char)value,count);
                            if(sink.Length!=count||sink.Calls!=(int)((count+63)/64)||sink.Largest>64)return 1;
                            if(destroyed!=expectedDestroyed)return 2;
                            if(bytes[0]!='?')return 3;
                            for(nuint i=0;i<count;i++)if(bytes[i+1]!=(char)value)return 4;
                            for(nuint i=count+1;i<4100;i++)if(bytes[i]!='?')return 5;
                        }
                        expectedDestroyed++;
                        if(destroyed!=expectedDestroyed)return 6;
                    }
                }
                if(allocations!=0)return 7;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PartialFailuresPropagateOnceAndLeaveTheBorrowedWriterUsable(int optimization)
        => Run(Probe + """
            int main(){
                char[260] bytes;int expectedDestroyed=0;
                for(int failAt=1;failAt<=3;failAt++){
                    for(nuint partial=0;partial<=1;partial++){
                        for(nuint i=0;i<260;i++)bytes[i]='?';
                        {
                            Sink sink=new(&bytes[1],failAt,partial);ITextWriter* writer=&sink;
                            bool caught=false;
                            try{writer.WriteRepeated('#',129);}
                            catch(ref Exception error){caught=true;if(error.GetCode()!=73)return 1;}
                            nuint prefix=(nuint)(failAt-1)*64+partial;
                            if(!caught||sink.Calls!=failAt||sink.Length!=prefix||destroyed!=expectedDestroyed)return 2;
                            for(nuint i=0;i<prefix;i++)if(bytes[i+1]!='#')return 3;
                            if(bytes[0]!='?')return 4;
                            for(nuint i=prefix+1;i<260;i++)if(bytes[i]!='?')return 5;
                            sink.FailAt=-1;writer.Write("ok",2);
                            if(sink.Calls!=failAt+1||sink.Length!=prefix+2||bytes[prefix+1]!='o'||bytes[prefix+2]!='k')return 6;
                        }
                        expectedDestroyed++;
                    }
                }
                {
                    Sink sink=new(bytes,1,1);ITextWriter* writer=&sink;bool caught=false;
                    try{writer.WriteRepeated('x',(nuint)0-(nuint)1);}
                    catch(ref Exception error){caught=true;if(error.GetCode()!=73)return 7;}
                    if(!caught||sink.Calls!=1||sink.Length!=1||sink.Largest!=64)return 8;
                }
                expectedDestroyed++;
                {
                    Sink sink=new(bytes,1,0);ITextWriter* writer=&sink;
                    writer.WriteRepeated('x',0);
                    if(sink.Calls!=0||sink.Length!=0||destroyed!=expectedDestroyed)return 9;
                }
                expectedDestroyed++;
                if(destroyed!=expectedDestroyed)return 10;
                return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        SourceFile[] sources=[new("text_writer_repeated.gf",File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory,"Std/core/Text/text_writer_repeated.gf"))),new("main.gf",program)];
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Writer repeat exit {result.ExitCode}: {result.StandardError}");
    }
}
