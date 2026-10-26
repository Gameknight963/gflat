using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardUniqueReleaseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReleaseKeepsPointerIdentityWithoutAllocationDeletionOrOwnerConsumption(int optimization)
        => Run(Counters+"""
            struct Payload{public int Number;public Payload(int value){Number=value;}~Payload(){dead++;}}
            Payload*? Take(ref Unique<Payload> owner) nothrow=>owner.Release();
            int main(){
                Payload*? raw=null;
                {
                    Unique<Payload> empty=new();deny=true;int before=attempts;
                    if(empty.Release()!=null||empty.Release()!=null||!empty.IsEmpty||attempts!=before)return 1;
                    deny=false;
                    Payload* original=new* Payload(42);watched=(void*)original;
                    Unique<Payload> owner=new(original);before=attempts;deny=true;
                    raw=Take(ref owner);
                    if(raw!=original||!owner.IsEmpty||owner.Release()!=null||attempts!=before)return 2;
                    if(dead!=0||watchedFrees!=0)return 3;
                }
                if(dead!=0||watchedFrees!=0)return 4;
                deny=false;if(raw!.Number!=42)return 5;raw!.Number=43;
                delete raw!;
                if(dead!=1||watchedFrees!=1||allocated!=released)return 6;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReadoptionRetainsDynamicDestructionAndExceptionCleanupExactlyOnce(int optimization)
        => Run(Counters+"""
            class Base{public int Value;public Base(){Value=42;}~Base(){baseDead++;}}
            class Derived:Base{~Derived(){derivedDead++;}}
            struct Payload{~Payload(){dead++;}}
            int main(){
                {
                    Base* original=new* Derived();watched=(void*)original;
                    Unique<Base> first=new(original);Base*? raw=first.Release();
                    if(raw!=original||!first.IsEmpty)return 1;
                    Unique<Base> second=new(raw!);
                    if(&second.Value!=original||second.Value.Value!=42)return 2;
                }
                if(baseDead!=1||derivedDead!=1||watchedFrees!=1)return 3;
                bool caught=false;
                try{
                    Unique<Payload> first=new(new* Payload());watched=(void*)&first.Value;
                    Payload*? raw=first.Release();Unique<Payload> second=new(raw!);
                    if(!first.IsEmpty||&second.Value!=raw!)return 4;
                    throw new Exception("after ownership handoff",47);
                }catch(ref Exception error){caught=true;if(error.GetCode()!=47)return 5;}
                // Watch only pointee frees; exception payload storage is counted separately.
                if(!caught||dead!=1||watchedFrees!=2||allocated!=released)return 6;
                return 0;
            }
            """,optimization);

    [Theory]
    [InlineData("readonly")]
    [InlineData("immutable")]
    public void ReleaseRequiresMutableOwner(string qualifier)
        => Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources($$"""
            using std; using std::Memory;
            void Invalid({{qualifier}}(Unique<int>)* owner){owner.Release();}
            """)));

    private const string Counters="""
        using std; using std::Memory;
        extern void*? malloc(nuint size);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int dead=0;int baseDead=0;int derivedDead=0;
        void*? watched=null;int watchedFrees=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint size){attempts++;if(deny)return null;void*? p=malloc(size);if(p!=null)allocated++;return p;}
            public replace void Free(void* storage) nothrow{released++;if(storage==watched){watchedFrees++;watched=null;}free(storage);}
        }
        """;
    private static IEnumerable<SourceFile> Sources(string source)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("unique-release.gf",source));
    private static void Run(string source,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(source),optimization);
        Assert.True(result.ExitCode==0,$"Unique release exit{result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
