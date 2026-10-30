using System.Text;

namespace gflat.Tests;

public sealed class StandardDefaultAlgorithmContractsTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Memory; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? result=malloc(count);if(result!=null)allocated++;return result;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OrderingMatchesIntegerAndFloatingOraclesWithoutAllocation(int optimization)
    {
        var body=new StringBuilder();var inputs=Inputs();
        for(int row=0;row<inputs.Count;row++){
            int[] a=inputs[row],b=inputs[(row+1)%inputs.Count];
            int min=a.Length==0?0:Enumerable.Range(0,a.Length).OrderBy(i=>a[i]).First();
            int max=a.Length==0?0:Enumerable.Range(0,a.Length).OrderByDescending(i=>a[i]).First();
            int comparison=0;for(int i=0;i<Math.Min(a.Length,b.Length);i++){comparison=a[i].CompareTo(b[i]);if(comparison!=0)break;}
            if(comparison==0)comparison=a.Length.CompareTo(b.Length);
            body.Append(Block(a,b,$"if(MinIndex(first.AsImmutable())!={min}||MaxIndex(first.AsImmutable())!={max}||SequenceCompare(first.AsImmutable(),second.AsImmutable())!={comparison})return 1;"));
        }
        body.Append("""
            {ulong[5] bits=[9221120237041090561ul,0ul,9223372036854775808ul,18442240474082181120ul,9218868437227405312ul];
             double[5] data;for(nuint i=0;i<5;i++)data[i]=(bits[i]).reinterpret<double>();Span<double> values=new(data,5);
             if(MinIndex(values.AsImmutable())!=0||MaxIndex(values.AsImmutable())!=4)return 2;
             Span<double> zero=values.Slice(1,2);if(MinIndex(zero.AsImmutable())!=0||MaxIndex(zero.AsImmutable())!=0)return 3;
             ulong otherBits=18444492273895866370ul;double otherNaN=(otherBits).reinterpret<double>();double[2] other=[otherNaN,-0.0];double[2] first=[data[0],0.0];
             Span<double> left=new(first,2);Span<double> right=new(other,2);if(SequenceCompare(left.AsImmutable(),right.AsImmutable())!=0)return 4;}
            """);
        Run(Allocator+"int main(){int before=attempts;deny=true;"+body+"deny=false;if(attempts!=before||allocated!=released)return 5;return 0;}",optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EqualityMatchesArrayAndFloatingOraclesWithoutAllocation(int optimization)
    {
        var body=new StringBuilder();var inputs=Inputs();
        for(int row=0;row<inputs.Count;row++){
            int[] a=inputs[row],b=row%2==0?a:inputs[(row+1)%inputs.Count];int key=row%7-3;
            int first=Array.IndexOf(a,key),last=Array.LastIndexOf(a,key);if(first<0)first=a.Length;if(last<0)last=a.Length;
            string checks=$"int key={key};if(SequenceEqual(first.AsImmutable(),second.AsImmutable())!={Bool(a.SequenceEqual(b))}||IndexOf(first.AsImmutable(),&key)!={first}||LastIndexOf(first.AsImmutable(),&key)!={last}||Contains(first.AsImmutable(),&key)!={Bool(a.Contains(key))})return 1;";
            body.Append(Block(a,b,checks));
        }
        body.Append("""
            {ulong aBits=9221120237041090561ul;ulong bBits=18444492273895866370ul;double aNaN=(aBits).reinterpret<double>();double bNaN=(bBits).reinterpret<double>();
             double[4] a=[aNaN,-0.0,3.0,bNaN];double[4] b=[bNaN,0.0,3.0,aNaN];Span<double> first=new(a,4);Span<double> second=new(b,4);
             if(!SequenceEqual(first.AsImmutable(),second.AsImmutable())||IndexOf(first.AsImmutable(),&bNaN)!=0||LastIndexOf(first.AsImmutable(),&bNaN)!=3||!Contains(first.AsImmutable(),&bNaN))return 2;
             double zero=0.0;double missing=-2.0;if(IndexOf(first.AsImmutable(),&zero)!=1||LastIndexOf(first.AsImmutable(),&zero)!=1||Contains(first.AsImmutable(),&missing)||IndexOf(first.AsImmutable(),&missing)!=4)return 3;}
            """);
        Run(Allocator+"int main(){int before=attempts;deny=true;"+body+"deny=false;if(attempts!=before||allocated!=released)return 4;return 0;}",optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OrderingBorrowsOwnersPreservesMagnitudeAndPropagatesOriginalFailures(int optimization)
        =>Run(OwnerSupport+"""
            int main(){
                {Vector<Owner> a=new();Vector<Owner> b=new();a.Reserve(4);b.Reserve(4);int[4] keys=[2,5,5,1];
                 for(int i=0;i<4;i++){a.Add(new Owner(i,keys[(nuint)i]));int key=keys[(nuint)i];if(i==1)key=9;b.Add(new Owner(i+10,key));}
                 nuint[8] tokens;for(nuint i=0;i<4;i++){tokens[i]=(a[i].Token.Data).reinterpret<nuint>();tokens[i+4]=(b[i].Token.Data).reinterpret<nuint>();}
                 int before=attempts;deny=true;calls=0;if(MinIndex(a.AsSpan().AsImmutable())!=3||calls!=3)return 1;
                 calls=0;if(MaxIndex(a.AsSpan().AsImmutable())!=1||calls!=3)return 2;
                 calls=0;if(SequenceCompare(a.AsSpan().AsImmutable(),b.AsSpan().AsImmutable())!=-4||calls!=2)return 3;
                 calls=0;if(SequenceCompare(b.AsSpan().AsImmutable(),a.AsSpan().AsImmutable())!=4||calls!=2)return 4;
                 if(SequenceCompare(a.AsSpan().Slice(0,1).AsImmutable(),b.AsSpan().Slice(0,2).AsImmutable())!=-1||SequenceCompare(b.AsSpan().Slice(0,2).AsImmutable(),a.AsSpan().Slice(0,1).AsImmutable())!=1)return 5;
                 Span<Owner> empty=new();calls=0;if(MinIndex(empty.AsImmutable())!=0||MaxIndex(empty.AsImmutable())!=0||SequenceCompare(empty.AsImmutable(),empty.AsImmutable())!=0||calls!=0)return 6;
                 deny=false;if(attempts!=before)return 7;
                 for(int mode=0;mode<3;mode++){bool caught=false;fail=true;calls=0;int result=99;
                    try{if(mode==0)result=(int)MinIndex(a.AsSpan().AsImmutable());else if(mode==1)result=(int)MaxIndex(a.AsSpan().AsImmutable());else result=SequenceCompare(a.AsSpan().AsImmutable(),b.AsSpan().AsImmutable());}
                    catch(ref Exception error){if(error.GetCode()!=77||!CString::Equals(error.GetMessage(),"original comparison"))return 8;caught=true;}fail=false;
                    if(!caught||calls!=1||result!=99||made-dead!=8)return 9;
                    for(nuint i=0;i<4;i++)if(a[i].Id!=(int)i||a[i].Key!=keys[i]||(a[i].Token.Data).reinterpret<nuint>()!=tokens[i]||b[i].Id!=(int)i+10||(b[i].Token.Data).reinterpret<nuint>()!=tokens[i+4])return 10;
                 }
                }if(made!=dead||allocated!=released)return 11;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EqualityBorrowsKeysAndOwnersShortCircuitsAndPropagatesOriginalFailures(int optimization)
        =>Run(OwnerSupport+"""
            int main(){
                {Vector<Owner> a=new();Vector<Owner> b=new();a.Reserve(4);b.Reserve(4);int[4] keys=[1,2,2,4];
                 for(int i=0;i<4;i++){a.Add(new Owner(i,keys[(nuint)i]));int key=keys[(nuint)i];if(i==2)key=9;b.Add(new Owner(i+10,key));}Owner key=new(99,2);
                 nuint[8] tokens;for(nuint i=0;i<4;i++){tokens[i]=(a[i].Token.Data).reinterpret<nuint>();tokens[i+4]=(b[i].Token.Data).reinterpret<nuint>();}nuint keyToken=(key.Token.Data).reinterpret<nuint>();
                 int before=attempts;deny=true;calls=0;if(IndexOf(a.AsSpan().AsImmutable(),&key)!=1||calls!=2||trace[0]!=0||trace[1]!=1)return 1;
                 calls=0;if(LastIndexOf(a.AsSpan().AsImmutable(),&key)!=2||calls!=2||trace[0]!=3||trace[1]!=2)return 2;
                 calls=0;if(!Contains(a.AsSpan().AsImmutable(),&key)||calls!=2)return 3;
                 calls=0;if(SequenceEqual(a.AsSpan().AsImmutable(),b.AsSpan().AsImmutable())||calls!=3)return 4;
                 calls=0;if(!SequenceEqual(a.AsSpan().AsImmutable(),a.AsSpan().AsImmutable())||calls!=4)return 5;
                 calls=0;if(SequenceEqual(a.AsSpan().AsImmutable(),b.AsSpan().Slice(0,3).AsImmutable())||calls!=0)return 6;
                 Span<Owner> empty=new();calls=0;if(!SequenceEqual(empty.AsImmutable(),empty.AsImmutable())||IndexOf(empty.AsImmutable(),&key)!=0||LastIndexOf(empty.AsImmutable(),&key)!=0||Contains(empty.AsImmutable(),&key)||calls!=0)return 7;
                 deny=false;if(attempts!=before)return 8;
                 for(int mode=0;mode<4;mode++){bool caught=false;fail=true;calls=0;int result=99;
                    try{if(mode==0)result=(int)IndexOf(a.AsSpan().AsImmutable(),&key);else if(mode==1)result=(int)LastIndexOf(a.AsSpan().AsImmutable(),&key);else if(mode==2){if(Contains(a.AsSpan().AsImmutable(),&key))result=1;}else{if(SequenceEqual(a.AsSpan().AsImmutable(),b.AsSpan().AsImmutable()))result=1;}}
                    catch(ref Exception error){if(error.GetCode()!=77||!CString::Equals(error.GetMessage(),"original comparison"))return 9;caught=true;}fail=false;
                    if(!caught||calls!=1||result!=99||made-dead!=9||key.Id!=99||key.Key!=2||(key.Token.Data).reinterpret<nuint>()!=keyToken)return 10;
                    for(nuint i=0;i<4;i++)if(a[i].Id!=(int)i||a[i].Key!=keys[i]||(a[i].Token.Data).reinterpret<nuint>()!=tokens[i]||b[i].Id!=(int)i+10||(b[i].Token.Data).reinterpret<nuint>()!=tokens[i+4])return 11;
                 }
                }if(made!=dead||allocated!=released)return 12;return 0;
            }
            """,optimization);

    private const string OwnerSupport=Allocator+"""
        int made=0;int dead=0;int calls=0;bool fail=false;int[8] trace;
        struct Owner:IComparable<Owner>,IEquatable<Owner>{
            public int Id;public int Key;public String Token;
            public Owner(int id,int key){Id=id;Key=key;Token=s"owner";made++;}
            public immutable int CompareTo(immutable(Owner)* other){calls++;if(fail)throw new Exception("original comparison",77);return Key-other.Key;}
            public immutable bool Equals(immutable(Owner)* other){trace[(nuint)calls]=Id;calls++;if(fail)throw new Exception("original comparison",77);return Key==other.Key;}
            ~Owner(){dead++;}
        }
        """;

    private static List<int[]> Inputs(){List<int[]> inputs=[[],[0],[3,1,3,1],[int.MinValue,int.MaxValue,0,-1]];var random=new Random(28371);for(int i=0;i<20;i++)inputs.Add(Enumerable.Range(0,random.Next(0,17)).Select(_=>random.Next(-4,5)).ToArray());return inputs;}
    private static string Bool(bool value)=>value?"true":"false";
    private static string Block(int[] a,int[] b,string checks){var code=new StringBuilder("{int[16] a;int[16] b;");for(int i=0;i<a.Length;i++)code.Append($"a[{i}]={a[i]};");for(int i=0;i<b.Length;i++)code.Append($"b[{i}]={b[i]};");return code+$"Span<int> first=new(a,{a.Length});Span<int> second=new(b,{b.Length});"+checks+"}";}
    private static void Run(string program,int optimization){var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories).Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("default-algorithm-contracts.gf",program));var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"Default algorithm contracts exit {result.ExitCode}: {result.StandardError}");}
}
