namespace gflat.Tests;

public sealed class StandardStringShrinkTests
{
    private const string Allocator = """
        using std;
        extern void*? malloc(nuint bytes);
        extern void free(void* storage);
        int allocations=0;int releases=0;int attempts=0;bool deny=false;
        nuint lastSize=0;nuint watched=0;int watchedReleases=0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;if(deny)return null;
                void*? result=malloc(bytes);
                if(result!=null){allocations++;lastSize=bytes;}
                return result;
            }
            public replace void Free(void* storage) nothrow {
                if((nuint)storage==watched)watchedReleases++;
                releases++;free(storage);
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactCapacityPreservesEveryCountedByteAndTightShrinksAreNoOps(int optimization)
        => Run(Allocator + """
            int main() {
                for(nuint length=1;length<=257;length++) {
                    String text=new();text.Resize(length);
                    for(nuint i=0;i<length;i++)text[i]=(char)((i*37+length)%256);
                    text.Reserve(length+97);
                    int before=allocations;int freed=releases;
                    watched=(nuint)text.Data;watchedReleases=0;
                    text.ShrinkToFit();
                    if(text.Length!=length||text.Capacity!=length||lastSize!=length+1)return 1;
                    if(allocations!=before+1||releases!=freed+1||watchedReleases!=1)return 2;
                    watched=0;
                    for(nuint i=0;i<length;i++)
                        if(text.Data[i]!=(char)((i*37+length)%256))return 3;
                    if(text.Data[length]!='\0')return 4;
                    readonly(char)* address=text.Data;int priorAttempts=attempts;
                    deny=true;text.ShrinkToFit();deny=false;
                    if(text.Data!=address||attempts!=priorAttempts||allocations!=before+1||releases!=freed+1)return 5;
                }
                if(allocations!=releases)return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyShrinkReleasesRetainedStorageWithoutAllocatingAndCanGrowAgain(int optimization)
        => Run(Allocator + """
            int main() {
                {
                    String text=new();readonly(char)* initial=text.Data;
                    deny=true;text.ShrinkToFit();deny=false;
                    if(text.Data!=initial||text.Length!=0||text.Capacity!=0||attempts!=0||releases!=0)return 1;
                    text.Reserve(4096);text.Append("payload");text.Clear();
                    if(text.Capacity!=4096)return 2;
                    int before=attempts;int freed=releases;
                    watched=(nuint)text.Data;watchedReleases=0;
                    deny=true;text.ShrinkToFit();deny=false;
                    if(attempts!=before||releases!=freed+1||watchedReleases!=1)return 3;
                    watched=0;
                    if(text.Length!=0||text.Capacity!=0||text.Data[0]!='\0')return 4;
                    text.ShrinkToFit();
                    if(attempts!=before||releases!=freed+1)return 5;
                    text.Append('x');
                    if(text.Length!=1||text.Data[0]!='x'||text.Data[1]!='\0')return 6;
                    text.Reserve(128);text.Resize(0);
                    deny=true;text.ShrinkToFit();deny=false;
                    if(text.Capacity!=0||text.Data[0]!='\0')return 7;
                }
                if(allocations!=releases)return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailurePreservesHeaderBytesAndOwnershipUntilSuccessfulRetry(int optimization)
        => Run(Allocator + """
            int main() {
                {
                    String text=String::From("a\0b",3);
                    text.Reserve(64);text.Resize(64,(char)255);text.Resize(3);
                    char[65] snapshot;
                    for(nuint i=0;i<=64;i++)snapshot[i]=text.Data[i];
                    readonly(char)* address=text.Data;nuint length=text.Length;nuint capacity=text.Capacity;
                    int before=allocations;int freed=releases;
                    watched=(nuint)address;watchedReleases=0;
                    bool caught=false;deny=true;
                    try{text.ShrinkToFit();}catch(ref BadAllocException error){caught=true;}
                    deny=false;
                    if(!caught||text.Data!=address||text.Length!=length||text.Capacity!=capacity)return 1;
                    if(allocations!=before||releases!=freed||watchedReleases!=0)return 2;
                    for(nuint i=0;i<=capacity;i++)if(text.Data[i]!=snapshot[i])return 3;
                    // The old allocation remains owned and writable after failure.
                    text[2]='z';text[2]='b';
                    text.ShrinkToFit();
                    if(allocations!=before+1||releases!=freed+1||watchedReleases!=1||lastSize!=4)return 4;
                    watched=0;
                    if(text.Length!=3||text.Capacity!=3||text.Data[0]!='a'||text.Data[1]!='\0'||text.Data[2]!='b'||text.Data[3]!='\0')return 5;
                    String copy=text;String moved=move text;
                    moved[0]='x';
                    if(copy.Data[0]!='a'||moved.Data[0]!='x'||copy.Length!=3||moved.Length!=3)return 6;
                }
                if(allocations!=releases)return 7;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var result=CompilerTestHelper.Run(InterpolationTests.Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"String shrink exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
