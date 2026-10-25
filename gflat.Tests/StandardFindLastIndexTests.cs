using System.Text;

namespace gflat.Tests;

public sealed class StandardFindLastIndexTests
{
    private const string Allocator="""
        using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeededArrayOraclesVisitBackwardsAndStopAtTheFirstReverseMatchWithoutAllocation(int optimization)
    {
        List<int[]> samples=[[],[7],[1],[1,7,14,2],[7,1,2],[1,2,7],[int.MinValue,int.MaxValue,0]];
        var random=new Random(731859);
        for(int row=0;row<40;row++)samples.Add(Enumerable.Range(0,random.Next(0,97)).Select(_=>random.Next(-1000,1001)).ToArray());
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(var input in samples){
            int found=Array.FindLastIndex(input,value=>value%7==0);
            writer.Write((byte)input.Length);writer.Write((byte)(found<0?input.Length:found));
            foreach(int value in input)writer.Write(value);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int calls=0;int[96] visited;
            bool Match(immutable(int)* value){if(calls>=96)throw new Exception("too many predicate calls");visited[(nuint)calls]=*value;calls++;return *value%7==0;}
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* data,ref nuint at)nothrow{int value=(Digit(data[at])<<4)|Digit(data[at+1]);at+=2;return value;}
            int ReadInt(readonly(char)* data,ref nuint at)nothrow{uint value=0u;for(int shift=0;shift<32;shift+=8)value=value|((uint)ReadByte(data,ref at)<<shift);return (int)value;}
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;int before=attempts;deny=true;
                for(int row=0;row<{{{samples.Count}}};row++){
                    nuint count=(nuint)ReadByte(packet,ref at);nuint expected=(nuint)ReadByte(packet,ref at);int[96] data;int[96] saved;
                    for(nuint i=0;i<count;i++){data[i]=ReadInt(packet,ref at);saved[i]=data[i];}
                    Span<int> values=new(data,count);calls=0;
                    nuint result=FindLastIndex<int>(values.AsImmutable(),Match);
                    nuint expectedCalls=count;if(expected<count)expectedCalls=count-expected;
                    if(result!=expected||calls!=(int)expectedCalls)return 1;
                    for(nuint i=0;i<expectedCalls;i++)if(visited[i]!=saved[count-1-i])return 2;
                    for(nuint i=0;i<count;i++)if(data[i]!=saved[i])return 3;
                }
                deny=false;if(at!={{{hex.Length}}}||attempts!=before||allocated!=released)return 4;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedNoncopyableOwnersAndOriginalPredicateFailuresPreserveTheInput(int optimization)
        =>Run(Allocator+"""
            int made=0;int dead=0;int calls=0;int[5] visited;
            struct Owner{
                public int Id;public String Token;
                public Owner(int id){Id=id;Token=s"owner";made++;}
                ~Owner(){dead++;}
            }
            bool Even(immutable(Owner)* value){visited[(nuint)calls]=value.Id;calls++;return value.Id%2==0;}
            bool NeverCalled(immutable(Owner)* value){calls++;throw new Exception("empty input touched",78);}
            bool FailAtThree(immutable(Owner)* value){visited[(nuint)calls]=value.Id;calls++;if(value.Id==3)throw new Exception("original predicate",77);return false;}
            int main(){
                {
                    Vector<Owner> owners=new();for(int id=1;id<=5;id++)owners.Add(new Owner(id));
                    nuint[5] addresses;nuint[5] tokens;for(nuint i=0;i<5;i++){addresses[i]=(nuint)&owners[i];tokens[i]=(nuint)owners[i].Token.Data;}
                    int before=attempts;int live=allocated-released;deny=true;calls=0;
                    if(FindLastIndex<Owner>(owners.AsImmutable(),Even)!=3||calls!=2||visited[0]!=5||visited[1]!=4)return 1;
                    Span<immutable(Owner)> empty=new();calls=0;
                    if(FindLastIndex<Owner>(empty,NeverCalled)!=0||calls!=0)return 2;
                    deny=false;if(attempts!=before||made!=5||dead!=0||allocated-released!=live)return 3;
                    nuint result=99;calls=0;bool caught=false;
                    try{result=FindLastIndex<Owner>(owners.AsImmutable(),FailAtThree);}
                    catch(ref Exception error){if(error.GetCode()!=77||!CString::Equals(error.GetMessage(),"original predicate"))return 4;caught=true;}
                    if(!caught||result!=99||calls!=3||visited[0]!=5||visited[1]!=4||visited[2]!=3)return 5;
                    if(made!=5||dead!=0||owners.Count!=5||allocated-released!=live)return 6;
                    for(nuint i=0;i<5;i++)if((nuint)&owners[i]!=addresses[i]||(nuint)owners[i].Token.Data!=tokens[i]||owners[i].Id!=(int)i+1)return 7;
                }
                if(made!=dead||allocated!=released)return 8;return 0;
            }
            """,optimization);

    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("find-last-index.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"FindLastIndex exit {result.ExitCode}: {result.StandardError}");
    }
}
