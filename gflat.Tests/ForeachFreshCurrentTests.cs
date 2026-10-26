using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ForeachFreshCurrentTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void FreshOwningCurrentTransfersAndEveryLoopExitReleasesOwners(bool method, int optimization)
    {
        string current = method
            ? "public Item Current() => this.Produce();"
            : "public Item Current { get => this.Produce(); }";
        string source = Support.Replace("CURRENT_MEMBER", current, StringComparison.Ordinal) + """
            int Exercise() {
                int sum=0;
                foreach(Item value in Make()) {
                    if(value.Id<0 || value.Id>2 || items!=1 || iterators!=1 || sources!=1) badOrder=true;
                    if(mode==1) continue;
                    if(mode==2) break;
                    if(mode==3) return 43;
                    if(mode==4) throw new Exception("body failure",74);
                    sum+=value.Id;
                }
                return sum;
            }
            int main() {
                for(mode=0;mode<6;mode++) {
                    int madeBefore=made;
                    int deadBefore=dead;
                    int movesBefore=moves;
                    int readsBefore=reads;
                    int factoriesBefore=factories;
                    int result=-1;
                    bool caught=false;
                    try { result=Exercise(); }
                    catch(ref Exception error) {
                        if(mode<4 || error.GetCode()!=70+mode) return 1;
                        if(mode==4 && !CString::Equals(error.GetMessage(),"body failure")) return 2;
                        if(mode==5 && !CString::Equals(error.GetMessage(),"current failure")) return 3;
                        caught=true;
                    }
                    if(caught!=(mode>=4)) return 4;
                    if(mode==0 && result!=3) return 5;
                    if((mode==1 || mode==2) && result!=0) return 6;
                    if(mode==3 && result!=43) return 7;
                    if(mode>=4 && result!=-1) return 8;
                    int expectedItems=1;
                    int expectedMoves=1;
                    int expectedReads=1;
                    if(mode<2) { expectedItems=3; expectedMoves=4; expectedReads=3; }
                    if(mode==5) { expectedMoves=2; expectedReads=2; }
                    if(made-madeBefore!=expectedItems || dead-deadBefore!=expectedItems) return 9;
                    if(moves-movesBefore!=expectedMoves || reads-readsBefore!=expectedReads) return 10;
                    if(factories-factoriesBefore!=1 || badOrder || items!=0 || iterators!=0 || sources!=0) return 11;
                    if(allocated!=freed) return 12;
                }
                return 0;
            }
            """;
        SourceFile[] sources =
        [
            new("CString.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/CString.gf"))),
            new("foreach-fresh-current.gf", source)
        ];
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Fresh foreach exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("public Item Current;")]
    [InlineData("public ref Item Current { get => stored; }")]
    [InlineData("public ref immutable(Item) Current { immutable get => stored; }")]
    public void BorrowedNoncopyableCurrentStillCannotInitializeTheIterationValue(string current)
    {
        bool field = current == "public Item Current;";
        string source = """
            struct Item { public Item(){} ~Item(){} }
            struct Iterator {
                STORAGE_MEMBER
                public Iterator(){INITIALIZE_FIELD}
                public bool MoveNext()=>false;
                CURRENT_MEMBER
            }
            struct Source { public Iterator GetEnumerator()=>new Iterator(); }
            int main(){Source source=new();foreach(Item value in source){}return 0;}
            """.Replace("CURRENT_MEMBER", current, StringComparison.Ordinal)
            .Replace("STORAGE_MEMBER", field ? "" : "private Item stored;", StringComparison.Ordinal)
            .Replace("INITIALIZE_FIELD", field ? "Current=new Item();" : "stored=new Item();", StringComparison.Ordinal);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Cannot copy value of type 'Item'", error.Message);
    }

    [Fact]
    public void ArrayIterationRetainsItsExplicitCopyabilityDiagnostic()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Item { ~Item(){} }
            int main(){Item[1] values=default(Item[1]);foreach(Item value in values){}return 0;}
            """));
        Assert.Contains("Foreach iteration variable 'value' requires a copyable element type", error.Message);
    }

    private const string Support = """
        using std; using std::Text;
        extern void*? malloc(nuint count);
        extern void free(void* storage);
        int allocated=0;
        int freed=0;
        int mode=0;
        int items=0;
        int sources=0;
        int iterators=0;
        int made=0;
        int dead=0;
        int moves=0;
        int reads=0;
        int factories=0;
        bool badOrder=false;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                void*? result=malloc(count);
                if(result!=null) allocated++;
                return result;
            }
            public replace void Free(void* storage) nothrow {freed++;free(storage);}
        }
        struct Item {
            public int Id;
            private void* token;
            public Item(int id) {Id=id;token=Memory::Allocate(1)!;items++;made++;}
            ~Item() {
                if(iterators!=1 || sources!=1 || items!=1) badOrder=true;
                items--;dead++;Memory::Free(token);
            }
        }
        struct Iterator {
            private void* token;
            private int index;
            public Iterator() {token=Memory::Allocate(1)!;index=-1;iterators++;}
            public bool MoveNext() {
                if(items!=0 || sources!=1 || iterators!=1) badOrder=true;
                moves++;index++;return index<3;
            }
            private Item Produce() {
                if(items!=0 || sources!=1 || iterators!=1) badOrder=true;
                reads++;
                if(mode==5 && index==1) throw new Exception("current failure",75);
                return new Item(index);
            }
            CURRENT_MEMBER
            ~Iterator() {
                if(items!=0 || sources!=1 || iterators!=1) badOrder=true;
                iterators--;Memory::Free(token);
            }
        }
        struct Source {
            private void* token;
            public Source(){token=Memory::Allocate(1)!;sources++;}
            public Iterator GetEnumerator()=>new Iterator();
            ~Source(){if(items!=0 || iterators!=0 || sources!=1) badOrder=true;sources--;Memory::Free(token);}
        }
        Source Make(){factories++;return new Source();}
        """;
}
