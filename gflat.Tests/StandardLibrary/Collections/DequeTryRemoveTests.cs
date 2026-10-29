namespace gflat.Tests.Std.Collections;

public sealed class DequeTryRemoveTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocations=0;int releases=0;int attempts=0;bool deny=false;bool capture=false;void*? captured=null;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(deny)return null;void*? result=malloc(count);
                if(result!=null){allocations++;if(capture)captured=result;}return result;
            }
            public replace void Free(void* storage)nothrow{releases++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReservedWrappedRandomTraceMatchesLinkedListWithoutAllocation(int optimization)
    {
        var model=new LinkedList<int>();var records=new List<int[]>();var random=new Random(772841);
        void Record(int operation,int input=0){
            bool success=false;int output=-9999999;
            if(operation==0)model.AddFirst(input);
            else if(operation==1)model.AddLast(input);
            else if(operation is 2 or 3){
                if(model.Count!=0){success=true;output=operation==2?model.First!.Value:model.Last!.Value;if(operation==2)model.RemoveFirst();else model.RemoveLast();}
            }else model.Clear();
            records.Add([operation,input,success?1:0,output,model.Count,..model]);
        }
        for(int value=0;value<8;value++)Record(1,value);
        for(int sample=0;sample<6;sample++)Record(2);
        for(int value=8;value<14;value++)Record(1,value);
        for(int sample=0;sample<800;sample++){
            int operation=random.Next(10);if(operation>4)operation%=4;
            if(model.Count==8&&operation<2)operation=2+random.Next(2);
            Record(operation,random.Next(-1000,1001));
        }
        string path=Path.Combine(Path.GetTempPath(),"gflat_deque_try_"+Guid.NewGuid().ToString("N")+".bin");
        try{
            using(var writer=new BinaryWriter(File.Create(path)))foreach(int[] record in records)foreach(int field in record)writer.Write(field);
            Run(Allocator+$$"""
                extern void*? fopen(readonly(char)* path,readonly(char)* mode);
                extern nuint fread(void* output,nuint size,nuint count,void* file);
                extern int fclose(void* file);extern int ferror(void* file);
                int main(){
                    void*? file=fopen({{Literal(path)}},"rb");if(file==null)return 1;defer{fclose(file!);}
                    {
                        Deque<int> values=new();values.Reserve(8);int allocated=attempts;deny=true;
                        int[5] header;int[8] expected;
                        for(int sample=0;sample<{{records.Count}};sample++){
                            if(fread((void*)&header[0],4,5,file!)!=5)return 2;
                            int output=-9999999;bool success=false;
                            if(header[0]==0)values.AddFirst(header[1]);
                            else if(header[0]==1)values.AddLast(header[1]);
                            else if(header[0]==2)success=values.TryRemoveFirst(ref output);
                            else if(header[0]==3)success=values.TryRemoveLast(ref output);
                            else values.Clear();
                            if(success!=(header[2]!=0)||output!=header[3]||values.Count!=(nuint)header[4]||values.Capacity!=8||values.IsEmpty!=(header[4]==0))return 3;
                            if(header[4]<0||header[4]>8||fread((void*)&expected[0],4,(nuint)header[4],file!)!=(nuint)header[4])return 4;
                            var iterator=values.GetEnumerator();
                            for(nuint i=0;i<values.Count;i++)if(values[i]!=expected[i]||!iterator.MoveNext()||iterator.Current!=expected[i])return 5;
                            if(iterator.MoveNext()||attempts!=allocated)return 6;
                            if(values.Count!=0&&(values.Front!=expected[0]||values.Back!=expected[values.Count-1]))return 7;
                        }
                        if(ferror(file!)!=0)return 8;
                    }
                    if(allocations!=releases)return 9;return 0;
                }
                """,optimization);
        }finally{File.Delete(path);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasedOutputsAnywhereInTheReservedRingAreRejectedBeforeMutation(int optimization)
        =>Run(Allocator+"""
            bool Matches(Deque<int>* values,int* storage,readonly(int)* original){
                if(values.Count!=3||values.Capacity!=8||values.Front!=6||values.Back!=8)return false;
                for(nuint i=0;i<3;i++)if((*values)[i]!=(int)i+6)return false;
                for(nuint i=0;i<8;i++)if(storage[i]!=original[i])return false;
                return true;
            }
            int main(){
                {
                    Deque<int> values=new();capture=true;values.Reserve(8);capture=false;
                    int* storage=(int*)captured!;
                    for(int value=0;value<8;value++)values.AddLast(value);
                    for(int sample=0;sample<6;sample++)values.RemoveFirst();values.AddLast(8);
                    int[8] original;for(nuint i=0;i<8;i++)original[i]=storage[i];
                    int* front=&values.Front;int* back=&values.Back;
                    for(nuint slot=0;slot<8;slot++)for(int end=0;end<2;end++){
                        bool caught=false;
                        try{if(end==0)values.TryRemoveFirst(ref storage[slot]);else values.TryRemoveLast(ref storage[slot]);}
                        catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Deque output must not refer to its storage"))return 1;caught=true;}
                        if(!caught||!Matches(&values,storage,&original[0])||&values.Front!=front||&values.Back!=back)return 2;
                    }
                    var iterator=values.GetEnumerator();iterator.MoveNext();bool caught=false;
                    try{values.TryRemoveLast(ref iterator.Current);}catch{caught=true;}
                    if(!caught||!Matches(&values,storage,&original[0]))return 3;
                    caught=false;try{values.TryRemoveFirst(ref values.Back);}catch{caught=true;}
                    if(!caught||!Matches(&values,storage,&original[0]))return 4;
                    int output=-99;if(!values.TryRemoveFirst(ref output)||output!=6||!values.TryRemoveLast(ref output)||output!=8)return 5;
                    if(!values.TryRemoveFirst(ref output)||output!=7||values.TryRemoveFirst(ref output)||values.TryRemoveLast(ref output)||output!=7)return 6;
                }
                if(allocations!=releases)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersMoveIntoTheOutputAndReleaseItsPreviousOwnerOnce(int optimization)
        =>Run(Allocator+"""
            int made=0;int dead=0;int[100] destroyed;int[4] order;
            struct Owner{
                public int Id;public String Token;
                public Owner(int id){Id=id;Token=s"owner";made++;}
                ~Owner(){if(dead<4)order[(nuint)dead]=Id;dead++;destroyed[(nuint)Id]++;}
            }
            int main(){
                for(nuint i=0;i<100;i++)destroyed[i]=0;
                {
                    Deque<Owner> values=new();values.Reserve(8);
                    values.AddLast(new Owner(1));values.AddLast(new Owner(2));values.AddLast(new Owner(3));Owner output=new(99);
                    bool caught=false;try{values.TryRemoveFirst(ref values[1]);}catch{caught=true;}
                    if(!caught||values.Count!=3||dead!=0||values.Front.Id!=1||values.Back.Id!=3)return 1;
                    caught=false;try{values.TryRemoveLast(ref values.Front);}catch{caught=true;}
                    if(!caught||values.Count!=3||dead!=0)return 2;
                    int baseline=attempts;deny=true;
                    if(!values.TryRemoveFirst(ref output)||output.Id!=1||output.Token.Length!=5||destroyed[99]!=1||dead!=1)return 3;
                    if(!values.TryRemoveLast(ref output)||output.Id!=3||destroyed[1]!=1||dead!=2)return 4;
                    if(!values.TryRemoveFirst(ref output)||output.Id!=2||destroyed[3]!=1||dead!=3)return 5;
                    readonly(char)* token=output.Token.Data;
                    if(values.TryRemoveFirst(ref output)||values.TryRemoveLast(ref output)||output.Id!=2||output.Token.Data!=token||dead!=3||destroyed[2]!=0)return 6;
                    if(attempts!=baseline||values.Count!=0||values.Capacity!=8)return 7;
                }
                if(made!=4||dead!=4||destroyed[2]!=1||order[0]!=99||order[1]!=1||order[2]!=3||order[3]!=2||allocations!=releases)return 8;
                return 0;
            }
            """,optimization);

    private static string Literal(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    private static void Run(string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("deque-try-remove.gf",source));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"Deque TryRemove exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
