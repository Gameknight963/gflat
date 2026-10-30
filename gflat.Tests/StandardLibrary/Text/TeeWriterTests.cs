namespace gflat.Tests.Std.Text;

public sealed class TeeWriterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BinaryChunksAndSingleFormattingPassReachBothOutputsWithoutInternalAllocation(int optimization)
        => Run("""
            using std;using std::Text;
            int attempts=0;int formatCalls=0;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){attempts++;return null;}
                public replace void Free(void* storage)nothrow{}
            }
            struct Message:IFormattable{
                public Message(){}
                public immutable void Format(ITextWriter* output){
                    formatCalls++;output.Write("A\0",2);int number=-42;number.Format(output);output.Write("\n",1);
                }
            }
            int main(){
                char[300] a;char[300] b;char[256] binary;
                for(nuint i=0;i<256;i++)binary[i]=(char)i;
                BufferWriter first=new(a,300);BufferWriter second=new(b,300);
                TeeWriter tee=new(&first,&second);ITextWriter* output=&tee;
                output.Write(binary,3);output.Write(&binary[3],253);
                if(first.Length!=256||second.Length!=256)return 1;
                for(nuint i=0;i<256;i++)if(a[i]!=binary[i]||b[i]!=binary[i]||binary[i]!=(char)i)return 2;
                if(a[256]!='\0'||b[256]!='\0')return 3;
                first.Clear();CountingWriter count=new();TeeWriter measured=new(&first,&count);
                Message value=new();IFormattable* formatted=&value;formatted.Format(&measured);
                if(formatCalls!=1||first.Length!=6||count.Length!=6)return 4;
                char[6] expected=['A','\0','-','4','2','\n'];
                for(nuint i=0;i<6;i++)if(a[i]!=expected[i])return 5;
                if(attempts!=0)return 6;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallsKeepEndpointOrderIncludingZeroCountsAndIdenticalEndpoints(int optimization)
        => Run("""
            using std;using std::Text;
            struct Sink:ITextWriter{
                private int id;private int* events;private int* used;
                public int Calls;public nuint Length;
                public Sink(int value,int* log,int* count){id=value;events=log;used=count;Calls=0;Length=0;}
                public void Write(readonly(char)* data,nuint count){events[*used]=id;(*used)++;Calls++;Length+=count;}
            }
            int main(){
                int[8] events;int used=0;Sink first=new(1,events,&used);Sink second=new(2,events,&used);
                TeeWriter tee=new(&first,&second);tee.Write("abc",3);tee.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                if(used!=4||first.Calls!=2||second.Calls!=2||first.Length!=3||second.Length!=3)return 1;
                for(int i=0;i<4;i++)if(events[i]!=(i%2)+1)return 2;
                TeeWriter twice=new(&first,&first);twice.Write("hi",2);twice.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                if(used!=8||first.Calls!=6||first.Length!=7||second.Calls!=2)return 3;
                for(int i=4;i<8;i++)if(events[i]!=1)return 4;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PartialFailuresPropagateInOrderAndRetriesCanDuplicateAcceptedOutput(int optimization)
        => Run("""
            using std;using std::Text;
            struct Sink:ITextWriter{
                private int id;private char* bytes;private int* events;private int* used;
                public bool Fail;public nuint Length;public int Calls;
                public Sink(int value,char* storage,int* log,int* count){id=value;bytes=storage;events=log;used=count;Fail=false;Length=0;Calls=0;}
                public void Write(readonly(char)* data,nuint count){
                    events[*used]=id;(*used)++;Calls++;
                    nuint take=count;if(Fail&&take>1)take=1;
                    for(nuint i=0;i<take;i++)bytes[Length+i]=data[i];Length+=take;
                    if(Fail){if(id==1)throw new Exception("first failed");throw new Exception("second failed");}
                }
            }
            int main(){
                char[32] a;char[32] b;int[16] events;int used=0;
                Sink first=new(1,a,events,&used);Sink second=new(2,b,events,&used);TeeWriter tee=new(&first,&second);
                first.Fail=true;int caught=0;
                try{tee.Write("ABC",3);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"first failed"))return 1;caught++;}
                if(caught!=1||used!=1||events[0]!=1||first.Length!=1||a[0]!='A'||second.Calls!=0)return 2;
                first.Fail=false;first.Length=0;used=0;second.Fail=true;
                try{tee.Write("ABC",3);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"second failed"))return 3;caught++;}
                if(caught!=2||used!=2||events[0]!=1||events[1]!=2||first.Length!=3||second.Length!=1)return 4;
                second.Fail=false;tee.Write("ABC",3);
                if(first.Length!=6||second.Length!=4)return 5;
                for(nuint i=0;i<6;i++)if(a[i]!="ABCABC"[i])return 6;
                for(nuint i=0;i<4;i++)if(b[i]!="AABC"[i])return 7;
                second.Fail=true;
                try{tee.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"second failed"))return 8;caught++;}
                if(caught!=3||first.Length!=6||second.Length!=4)return 9;
                second.Fail=false;tee.Write("Z",1);
                if(first.Length!=7||second.Length!=5||a[6]!='Z'||b[4]!='Z')return 10;
                for(int i=0;i<used;i++)if(events[i]!=(i%2)+1)return 11;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesAndMovesShareBorrowedSinksWithoutWritingOrDestroyingThemOnExit(int optimization)
        => Run("""
            using std;using std::Text;
            struct Sink:ITextWriter{
                private int* destroyed;public nuint Length;public int Calls;
                public Sink(int* count){destroyed=count;Length=0;Calls=0;}
                public void Write(readonly(char)* data,nuint count){Length+=count;Calls++;}
                ~Sink(){(*destroyed)++;}
            }
            int main(){
                int destroyed=0;
                {
                    Sink first=new(&destroyed);Sink second=new(&destroyed);
                    {
                        TeeWriter original=new(&first,&second);TeeWriter copy=original;
                        copy.Write("a",1);TeeWriter moved=move original;moved.Write("bc",2);
                        if(first.Length!=3||second.Length!=3||first.Calls!=2||second.Calls!=2||destroyed!=0)return 1;
                    }
                    if(destroyed!=0||first.Calls!=2||second.Calls!=2)return 2;
                    first.Write("d",1);second.Write("e",1);
                    if(first.Length!=4||second.Length!=4)return 3;
                }
                if(destroyed!=2)return 4;return 0;
            }
            """,optimization);

    private static void Run(string code,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("tee-writer.gf",code));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"TeeWriter exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
