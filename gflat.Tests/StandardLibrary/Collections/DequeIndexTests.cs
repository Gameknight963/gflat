using System.Text;

namespace gflat.Tests.Std.Collections;

public sealed class DequeIndexTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny){denied++;return null;}if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededListTraceCoversWrappedMiddleMovesGrowthEndpointsAndCapacityChanges(int optimization)
    {
        var model=new List<int>();var rows=new List<(int Code,int Index,int Value,int Removed,int[] Expected)>();
        void Step(int code,int index=0,int value=0){int removed=0;switch(code){
            case 0:model.Insert(index,value);break;case 1:removed=model[index];model.RemoveAt(index);break;
            case 2:model.Insert(0,value);break;case 3:model.Add(value);break;
            case 4:removed=model[0];model.RemoveAt(0);break;case 5:removed=model[^1];model.RemoveAt(model.Count-1);break;
            case 8:model.Clear();break;}
            rows.Add((code,index,value,removed,model.ToArray()));}
        Step(6,8);for(int i=0;i<8;i++)Step(3,value:i);for(int i=0;i<5;i++)Step(4);for(int i=8;i<11;i++)Step(3,value:i);
        Step(0,3,100);Step(0,2,101);Step(0,4,102);Step(1,3);Step(0,0,-1);Step(0,model.Count,-2);Step(1,0);Step(1,model.Count-1);
        var random=new Random(318059);for(int step=0;step<220;step++){
            int operation=random.Next(100);
            if(model.Count==0||operation<44&&model.Count<45)Step(0,random.Next(model.Count+1),random.Next(-1000,1001));
            else if(operation<76)Step(1,random.Next(model.Count));
            else if(operation<86)Step(6,model.Count+random.Next(1,13));
            else if(operation<91)Step(2,value:random.Next(-1000,1001));
            else if(operation<96)Step(7);else Step(8);
        }
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var row in rows){
            writer.Write((byte)row.Code);writer.Write((ushort)row.Index);writer.Write(row.Value);writer.Write(row.Removed);writer.Write((ushort)row.Expected.Length);foreach(int value in row.Expected)writer.Write(value);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            nuint ReadSize(readonly(char)* data,ref nuint at)nothrow{int low=ReadByte(data,ref at);return (nuint)(low|(ReadByte(data,ref at)<<8));}
            int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;
                {
                    Deque<int> values=new();
                    for(int row=0;row<{{{rows.Count}}};row++){
                        int code=ReadByte(data,ref at);nuint index=ReadSize(data,ref at);int value=ReadInt(data,ref at);int expectedRemoved=ReadInt(data,ref at);int removed=0;
                        if(code==0)values.Insert(index,value);else if(code==1)removed=values.RemoveAt(index);
                        else if(code==2)values.AddFirst(value);else if(code==3)values.AddLast(value);
                        else if(code==4)removed=values.RemoveFirst();else if(code==5)removed=values.RemoveLast();
                        else if(code==6)values.Reserve(index);else if(code==7)values.ShrinkToFit();else values.Clear();
                        if((code==1||code==4||code==5)&&removed!=expectedRemoved)return 1;
                        if(code==6&&values.Capacity<index)return 2;if(code==7&&values.Capacity!=values.Count)return 3;
                        nuint count=ReadSize(data,ref at);if(values.Count!=count||values.Capacity<count||values.IsEmpty!=(count==0))return 4;
                        immutable(Deque<int>)* view=&values;var iterator=values.GetEnumerator();
                        for(nuint i=0;i<count;i++){int expected=ReadInt(data,ref at);if((*view)[i]!=expected||!iterator.MoveNext()||iterator.Current!=expected)return 5;if(i==0&&view.Front!=expected)return 6;if(i==count-1&&view.Back!=expected)return 7;}
                        if(iterator.MoveNext())return 8;
                    }
                }
                if(at!={{{hex.Length}}}||allocated!=released)return 9;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersShiftWithoutCopiesOrDestructionAndRemovedOwnersTransferExactlyOnce(int optimization)
        =>Run(Allocator+"""
            int made=0;int dead=0;int[32] destruction;
            struct Owner{
                public int Id;private String token;public Owner(int id){Id=id;token=s"owner";made++;}
                ~Owner(){destruction[(nuint)dead]=Id;dead++;}
            }
            int main(){
                {
                    Deque<Owner> values=new();values.Reserve(8);for(int i=0;i<8;i++)values.AddLast(new Owner(i));
                    for(int i=0;i<5;i++)values.RemoveFirst();for(int i=8;i<11;i++)values.AddLast(new Owner(i));
                    Owner incoming=new(99);int before=attempts;int beforeDead=dead;deny=true;values.Insert(3,move incoming);deny=false;
                    if(attempts!=before||dead!=beforeDead||made!=12||values.Count!=7)return 1;
                    int[7] expected=[5,6,7,99,8,9,10];for(nuint i=0;i<7;i++)if(values[i].Id!=expected[i])return 2;
                    before=attempts;deny=true;
                    {Owner removed=values.RemoveAt(4);if(removed.Id!=8||dead!=beforeDead||values.Count!=6)return 3;}
                    if(dead!=beforeDead+1||destruction[(nuint)dead-1]!=8)return 4;
                    values.RemoveAt(2);if(dead!=beforeDead+2||destruction[(nuint)dead-1]!=7)return 5;
                    deny=false;if(attempts!=before)return 6;
                    Owner first=new(-1);Owner last=new(-2);before=attempts;beforeDead=dead;deny=true;
                    values.Insert(0,move first);values.Insert(values.Count,move last);
                    if(dead!=beforeDead||attempts!=before||values.Front.Id!=-1||values.Back.Id!=-2)return 7;
                    values.RemoveAt(0);values.RemoveAt(values.Count-1);deny=false;
                    if(dead!=beforeDead+2||destruction[(nuint)dead-2]!=-1||destruction[(nuint)dead-1]!=-2)return 8;
                    int[5] remaining=[5,6,99,9,10];for(nuint i=0;i<5;i++)if(values[i].Id!=remaining[i])return 9;
                }
                if(made!=dead||allocated!=released)return 10;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedGrowthAndArgumentCopiesPreserveWrappedStorageAndSuccessfulSameSourceCopiesAreStaged(int optimization)
        =>Run(Allocator+"""
            int liveOwners=0;int copies=0;bool failCopy=false;
            struct Owner{
                public int Id;public String Token;public Owner(int id){Id=id;Token=s"owner";liveOwners++;}
                public copy Owner(immutable(Owner)* other){Id=other.Id;Token=other.Token;if(failCopy)throw new Exception("copy",71);liveOwners++;copies++;}
                ~Owner(){liveOwners--;}
            }
            int main(){
                nuint[3] indices=[0,2,4];
                for(nuint sample=0;sample<3;sample++){
                    {
                        Deque<Owner> values=new();values.Reserve(4);for(int i=1;i<=4;i++)values.AddLast(new Owner(i));
                        values.RemoveFirst();values.RemoveFirst();values.AddLast(new Owner(5));values.AddLast(new Owner(6));
                        nuint[4] addresses;for(nuint i=0;i<4;i++)addresses[i]=(&values[i]).reinterpret<nuint>();nuint capacity=values.Capacity;int baseline=allocated-released;
                        failCopy=true;bool caught=false;
                        try{values.Insert(indices[sample],values[3]);}catch(ref Exception error){if(error.GetCode()!=71)return 1;caught=true;}failCopy=false;
                        if(!caught||values.Count!=4||values.Capacity!=capacity||liveOwners!=4||allocated-released!=baseline)return 2;
                        for(nuint i=0;i<4;i++)if((&values[i]).reinterpret<nuint>()!=addresses[i]||values[i].Id!=(int)i+3)return 3;
                        Owner incoming=new(7);failAfter=0;caught=false;int deniedBefore=denied;
                        try{values.Insert(indices[sample],move incoming);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                        if(!caught||denied!=deniedBefore+1||values.Count!=4||values.Capacity!=capacity||liveOwners!=4||allocated-released!=baseline)return 4;
                        for(nuint i=0;i<4;i++)if((&values[i]).reinterpret<nuint>()!=addresses[i]||values[i].Id!=(int)i+3)return 5;
                        int beforeCopies=copies;values.Insert(indices[sample],values[3]);
                        if(values.Count!=5||values[indices[sample]].Id!=6||copies!=beforeCopies+1||liveOwners!=5)return 6;
                        nuint originalIndex=3;if(indices[sample]<=3)originalIndex++;
                        if(values[indices[sample]].Token.Data==values[originalIndex].Token.Data)return 7;
                        for(nuint oldIndex=0;oldIndex<4;oldIndex++){nuint position=oldIndex;if(oldIndex>=indices[sample])position++;if(values[position].Id!=(int)oldIndex+3)return 8;}
                        Owner kept=new(88);readonly(char)* keptData=kept.Token.Data;nuint[2] bad=[values.Count,(nuint)0-1];
                        for(nuint i=0;i<2;i++){
                            caught=false;try{kept=values.RemoveAt(bad[i]);}catch(ref Exception error){caught=true;}
                            if(!caught||kept.Id!=88||kept.Token.Data!=keptData||values.Count!=5)return 9;
                        }
                        nuint front=(&values[0]).reinterpret<nuint>();capacity=values.Capacity;nuint[2] badInsert=[values.Count+1,(nuint)0-1];
                        for(nuint i=0;i<2;i++){
                            caught=false;try{values.Insert(badInsert[i],new Owner(99));}catch(ref Exception error){caught=true;}
                            if(!caught||values.Count!=5||values.Capacity!=capacity||(&values[0]).reinterpret<nuint>()!=front||liveOwners!=6)return 10;
                        }
                    }
                    if(liveOwners!=0||allocated!=released)return 11;
                }
                return 0;
            }
            """,optimization);

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("deque-index.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Deque index exit {result.ExitCode}: {result.StandardError}");
    }
}
