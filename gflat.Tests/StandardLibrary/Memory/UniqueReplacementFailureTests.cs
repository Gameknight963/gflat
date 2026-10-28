namespace gflat.Tests.Std.Memory;

public sealed class UniqueReplacementFailureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedAllocationAndThrowingConstructionPreserveExistingOwnerAndReleaseFailedStorage(int optimization)
    {
        const string program = """
            using std;using std::Memory;
            extern void*? malloc(nuint bytes);extern void free(void* storage);
            int allocated=0;int released=0;int denied=0;int started=0;int dead=0;
            int originalFrees=0;int failedFrees=0;
            bool failNext=false;bool captureNext=false;
            void*? originalStorage=null;void*? failedStorage=null;
            namespace Memory{
                public replace void*? Allocate(nuint bytes){
                    // Deny exactly one request so creating BadAllocException can
                    // use the normal allocator rather than recursively failing.
                    if(failNext){failNext=false;denied++;return null;}
                    void*? result=malloc(bytes);
                    if(result!=null){
                        allocated++;
                        if(captureNext){captureNext=false;failedStorage=result;}
                    }
                    return result;
                }
                public replace void Free(void* storage)nothrow{
                    released++;
                    if(storage==originalStorage){originalFrees++;originalStorage=null;}
                    if(storage==failedStorage){failedFrees++;failedStorage=null;}
                    free(storage);
                }
            }
            struct Payload{
                public int Number;
                public Payload(int number,bool fail){
                    started++;Number=number;
                    if(fail)throw new Exception("payload construction failed",73);
                }
                ~Payload(){dead++;}
            }
            int main(){
                {
                    Unique<Payload> owner=new(new* Payload(42,false));
                    Payload* original=&owner.Value;originalStorage=(void*)original;
                    int live=allocated-released;
                    failNext=true;bool caught=false;
                    try{owner=new Unique<Payload>(new* Payload(90,false));}
                    catch(ref BadAllocException error){caught=true;}
                    if(!caught||failNext||denied!=1||allocated-released!=live)return 1;
                    if(owner.IsEmpty||&owner.Value!=original||owner.Value.Number!=42)return 2;
                    if(started!=1||dead!=0||originalFrees!=0)return 3;

                    captureNext=true;caught=false;
                    try{owner=new Unique<Payload>(new* Payload(91,true));}
                    catch(ref Exception error){if(error.GetCode()!=73)return 4;caught=true;}
                    if(!caught||captureNext||failedStorage!=null||failedFrees!=1)return 5;
                    if(allocated-released!=live||denied!=1||started!=2||dead!=0||originalFrees!=0)return 6;
                    if(owner.IsEmpty||&owner.Value!=original||owner.Value.Number!=42)return 7;
                    owner.Value.Number=43;if(original.Number!=43)return 8;
                }
                if(dead!=1||originalFrees!=1||failedFrees!=1||allocated!=released)return 9;
                return 0;
            }
            """;
        var sources = new[] { "Memory/Unique.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("unique-replacement-failure.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Unique replacement failure exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
