using System.Text;

namespace gflat.Tests.Std.Collections;

public sealed class StablePartitionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallMaskMatchesIndependentStableHostOrderAndOriginalPredicateVisits(int optimization)
    {
        var records = new StringBuilder();
        int rows = 0;
        for (int length = 0; length <= 8; length++)
        for (int mask = 0; mask < 1 << length; mask++)
        {
            int[] ids = Enumerable.Range(0, length).ToArray();
            bool Selected(int id) => (mask & (1 << id)) != 0;
            int[] expected = ids.Where(Selected).Concat(ids.Where(id => !Selected(id))).ToArray();
            records.Append((char)('0' + length));
            records.Append((char)('0' + ids.Count(Selected)));
            foreach (int id in ids) records.Append(Selected(id) ? '1' : '0');
            foreach (int id in expected) records.Append((char)('0' + id));
            rows++;
        }
        Run(Allocator + $$"""
            int calls=0;int mask=0;bool invalid=false;
            immutable(int)* origin=(immutable(int)*)1;
            bool Selected(immutable(int)* element){
                int expected=calls;calls++;
                if(element!=origin+expected||*element!=expected){invalid=true;return false;}
                return (mask&(1<<expected))!=0;
            }
            int main(){
                readonly(char)* records="{{records}}";nuint at=0;
                for(int row=0;row<{{rows}};row++){
                    nuint count=(nuint)(records[at]-'0');at++;
                    nuint expectedBoundary=(nuint)(records[at]-'0');at++;
                    mask=0;for(nuint i=0;i<count;i++){if(records[at+i]=='1')mask=mask|(1<<(int)i);}at+=count;
                    int[10] data;for(nuint i=0;i<10;i++)data[i]=91;
                    for(nuint i=0;i<count;i++)data[i+1]=(int)i;
                    Span<int> all=new(data,10);origin=&data[1];calls=0;invalid=false;
                    int before=attempts;deny=true;
                    nuint boundary=StablePartition<int>(all.Slice(1,count),Selected);
                    deny=false;
                    if(boundary!=expectedBoundary||calls!=(int)count||invalid||attempts!=before)return 1;
                    for(nuint i=0;i<count;i++)if(data[i+1]!=(int)(records[at+i]-'0'))return 2;
                    for(nuint i=count+1;i<10;i++)if(data[i]!=91)return 3;
                    if(data[0]!=91)return 4;at+=count;
                }
                if(at!={{records.Length}}||allocated!=released)return 5;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersKeepIdentityAndEveryPredicateFailureLeavesAValidPermutation(int optimization)
        => Run(Allocator + """
            int made=0;int dead=0;int calls=0;int failAt=-1;
            immutable(Owner)* origin=(immutable(Owner)*)1;
            struct Owner{
                public int Id;public String Token;
                public Owner(int id){Id=id;Token=s"owner";made++;}
                ~Owner(){dead++;}
            }
            bool Selected(immutable(Owner)* element){
                int position=calls;calls++;
                if(element!=origin+position||element.Id!=position)throw new Exception("not borrowed in original order",99);
                if(position==failAt)throw new Exception("original stable predicate",77);
                return element.Id%3!=0;
            }
            int main(){
                int[8] expected=[1,2,4,5,7,0,3,6];
                for(int failure=-1;failure<8;failure++){
                    {
                        int deadBefore=dead;
                        Vector<Owner> owners=new();owners.Reserve(10);owners.Add(new Owner(8));
                        for(int id=0;id<8;id++)owners.Add(new Owner(id));owners.Add(new Owner(9));
                        nuint[10] tokens;
                        for(nuint i=0;i<10;i++)tokens[(nuint)owners[i].Id]=(nuint)owners[i].Token.Data;
                        origin=&owners[1];nuint capacity=owners.Capacity;calls=0;failAt=failure;
                        nuint boundary=99;bool caught=false;int before=attempts;
                        if(failure<0){
                            deny=true;boundary=StablePartition<Owner>(owners.AsSpan().Slice(1,8),Selected);deny=false;
                            if(boundary!=5||calls!=8||attempts!=before)return 1;
                            for(nuint i=0;i<8;i++)if(owners[i+1].Id!=expected[i])return 2;
                        }
                        else{
                            try{boundary=StablePartition<Owner>(owners.AsSpan().Slice(1,8),Selected);}
                            catch(ref Exception error){caught=error.GetCode()==77&&CString::Equals(error.GetMessage(),"original stable predicate");}
                            if(!caught||boundary!=99||calls!=failure+1)return 3;
                        }
                        if(owners.Count!=10||owners.Capacity!=capacity||dead!=deadBefore||made-dead!=10)return 4;
                        if(owners[0].Id!=8||owners[9].Id!=9)return 5;
                        int seen=0;
                        for(nuint i=0;i<10;i++){
                            int id=owners[i].Id;if(id<0||id>=10||(seen&(1<<id))!=0)return 6;
                            seen=seen|(1<<id);
                            if((nuint)owners[i].Token.Data!=tokens[(nuint)id]||owners[i].Token.Length!=5)return 7;
                        }
                        if(seen!=1023)return 8;
                    }
                    if(made!=dead||allocated!=released)return 9;
                }
                return 0;
            }
            """, optimization);

    private const string Allocator = """
        using std;using std::Text;using std::Memory;using std::Collections;
        extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(deny)return null;void*? storage=malloc(count);if(storage!=null)allocated++;return storage;
            }
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("stable-partition.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
