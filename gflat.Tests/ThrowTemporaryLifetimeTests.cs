namespace gflat.Tests;

public sealed class ThrowTemporaryLifetimeTests
{
    private const string Prelude="""
        extern int putchar(int value);
        private extern void*? malloc(nuint bytes);private extern void free(void* storage);
        int allocations=0;int releases=0;int buffers=0;int bufferDead=0;int made=0;int dead=0;int holders=0;int copies=0;bool failCopy=false;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){void*? result=malloc(bytes);if(result!=null)allocations++;return result;}
            public replace void Free(void* storage){releases++;free(storage);}
        }
        struct Buffer{
            public int* Data;
            public Buffer(int value){Data=new* int(value);buffers++;}
            public copy Buffer(immutable(Buffer)* source){Data=new* int(*source.Data);buffers++;}
            ~Buffer(){bufferDead++;delete Data;}
        }
        class Failure:Exception{
            public Buffer Value;
            public Failure(int value){Value=new Buffer(value);made++;}
            public copy Failure(immutable(Failure)* source){
                copies++;Value=source.Value;putchar(67);
                if(failCopy)throw new Exception();made++;
            }
            ~Failure(){dead++;putchar(69);}
        }
        struct Holder{
            public Failure Error;
            public Holder(){Error=new Failure(42);}
            public ref Failure Borrow{get=>Error;}
            public Failure Copy=>Error;
            public Failure Fresh()=>new Failure(99);
            public Holder Next()=>new Holder();
            ~Holder(){holders++;putchar(72);}
        }
        Holder Make()=>new Holder();
        """;

    [Theory]
    [InlineData(0,"Make().Error",42,1,1,"CHEKE")]
    [InlineData(2,"Make().Error",42,1,1,"CHEKE")]
    [InlineData(0,"Make().Borrow",42,1,1,"CHEKE")]
    [InlineData(2,"Make().Borrow",42,1,1,"CHEKE")]
    [InlineData(0,"Make().Copy",42,1,1,"CHEKE")]
    [InlineData(2,"Make().Copy",42,1,1,"CHEKE")]
    [InlineData(0,"Make().Fresh()",99,0,1,"HEKE")]
    [InlineData(2,"Make().Fresh()",99,0,1,"HEKE")]
    [InlineData(0,"Make().Next().Fresh()",99,0,2,"HEHEKE")]
    [InlineData(2,"Make().Next().Fresh()",99,0,2,"HEHEKE")]
    public void PayloadTransferPreservesReceiversUntilUnwindAndDestroysEveryOwner(int optimization,string expression,int value,int expectedCopies,int expectedHolders,string output)
        => Run(Prelude+"""
            int main(){bool caught=false;
                try{throw EXPRESSION;}catch(Failure* error){caught=true;if(*error.Value.Data!=VALUE || holders!=HOLDERS)return 1;putchar(75);}
                if(!caught || copies!=COPIES || holders!=HOLDERS || made!=HOLDERS+1 || made!=dead || buffers!=bufferDead || allocations!=releases)return 2;
                return 0;}
            """.Replace("EXPRESSION",expression).Replace("VALUE",value.ToString()).Replace("HOLDERS",expectedHolders.ToString()).Replace("COPIES",expectedCopies.ToString()),optimization,output);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailingCopyCleansPartialFieldsReceiverAndUnusedExceptionAllocation(int optimization)
        => Run(Prelude+"""
            int main(){bool caught=false;failCopy=true;
                try{throw Make().Error;}catch{caught=true;putchar(75);}
                if(!caught || copies!=1 || holders!=1 || made!=1 || dead!=1 || buffers!=2 || bufferDead!=2 || allocations!=releases)return 1;
                return 0;}
            """,optimization,"CHEK");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RethrowPreservesTheTransferredObjectWithoutAnotherCopyOrDestruction(int optimization)
        => Run(Prelude+"""
            int main(){bool caught=false;
                try{try{throw Make().Error;}catch(Failure* error){if(*error.Value.Data!=42 || dead!=1)return 1;putchar(73);throw;}}
                catch(Failure* error){caught=true;if(*error.Value.Data!=42 || dead!=1)return 2;putchar(79);}
                if(!caught || copies!=1 || holders!=1 || made!=2 || dead!=2 || buffers!=bufferDead || allocations!=releases)return 3;
                return 0;}
            """,optimization,"CHEIOE");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshManagedPayloadRemainsRootedWhenReceiverDestructorCollects(int optimization)
        => Run("""
            using std;
            bool bad=false;int holders=0;
            class Node{public int Value;public Node^? Next;public Node(int value){Value=value;}}
            class Failure:Exception{
                public Node^ Root;
                public Failure(int value){Root=new^ Node(value);Root.Next=Root;}
            }
            struct Holder{
                private Failure source;
                public Holder(){source=new Failure(42);}
                public Failure Fresh()=>new Failure(99);
                ~Holder(){holders++;GC::Collect();if(TracingCollector::ManagedCount!=2)bad=true;}
            }
            Holder Make()=>new Holder();
            int main(){bool caught=false;
                try{throw Make().Fresh();}
                catch(Failure* error){caught=true;if(bad || holders!=1)return 1;GC::Collect();if(TracingCollector::ManagedCount!=1 || error.Root.Value!=99)return 2;}
                if(!caught)return 3;GC::Collect();return (int)TracingCollector::ManagedCount;}
            """,optimization,"",managed:true);

    private static void Run(string program,int optimization,string expected,bool managed=false)
    {
        var sources=new List<SourceFile>();
        if(managed)foreach(string name in new[]{"libc/GC.gf","libc/TracingCollector.gf"})
            sources.Add(new SourceFile("std/"+name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))));
        sources.Add(new SourceFile("throw-temporary.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Throw temporary exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        Assert.Equal(expected,result.StandardOutput);
    }
}
