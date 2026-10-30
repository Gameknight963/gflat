using System.Globalization;
using Integer=System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerLcmTests
{
    private static Integer Lcm(Integer a,Integer b)=>a.IsZero||b.IsZero?Integer.Zero:Integer.Abs(a/Integer.GreatestCommonDivisor(a,b)*b);
    private static string Text(Integer value)=>value.ToString(CultureInfo.InvariantCulture);
    private static string Array(IEnumerable<Integer> values)=>string.Join(',',values.Select(value=>'"'+Text(value)+'"'));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostOracleSignsLargeFactorsPowersAndAliasedOperandsPreserveIndependentOwners(int optimization)
    {
        var rows=new List<(Integer A,Integer B)>();
        void Signs(Integer a,Integer b) { foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})rows.Add((a*x,b*y)); }
        foreach(var pair in new (Integer,Integer)[]{(0,0),(0,17),(17,0),(1,1),(1,31),(84,126),(17,31),
            (long.MinValue,long.MaxValue),(1_000_000_000,999_999_999)})Signs(pair.Item1,pair.Item2);
        Integer common=Integer.Pow(10,180)+123;Signs(common*84,common*126);Signs(common,common);
        Signs(Integer.Pow(2,257),Integer.Pow(2,181));Signs(Integer.Pow(3,90),Integer.Pow(5,70));
        var random=new Random(530281);
        for(int sample=0;sample<16;sample++) {
            byte[] a=new byte[random.Next(1,25)],b=new byte[random.Next(1,25)];random.NextBytes(a);random.NextBytes(b);
            rows.Add((new Integer(a),new Integer(b)));
        }
        Run(Counters+$$"""
            int main() {
                readonly(char)*[{{rows.Count}}] firstText=[{{Array(rows.Select(row=>row.A))}}];
                readonly(char)*[{{rows.Count}}] secondText=[{{Array(rows.Select(row=>row.B))}}];
                readonly(char)*[{{rows.Count}}] expectedText=[{{Array(rows.Select(row=>Lcm(row.A,row.B)))}}];
                for(nuint row=0;row<{{rows.Count}};row++) {
                    {
                        BigInteger first=BigInteger::Parse(firstText[row]);BigInteger second=BigInteger::Parse(secondText[row]);
                        BigInteger expected=BigInteger::Parse(expectedText[row]);BigInteger savedFirst=first;BigInteger savedSecond=second;
                        nuint firstHash=first.GetHashCode();nuint secondHash=second.GetHashCode();
                        immutable(BigInteger)* firstView=&first;immutable(BigInteger)* secondView=&second;
                        BigInteger result=BigInteger::LeastCommonMultiple(firstView,secondView);
                        BigInteger swapped=BigInteger::LeastCommonMultiple(secondView,firstView);
                        if(result.Sign<0||!result.Equals(&expected)||!swapped.Equals(&expected))return 1;
                        if(!first.Equals(&savedFirst)||!second.Equals(&savedSecond)||first.GetHashCode()!=firstHash||second.GetHashCode()!=secondHash)return 2;
                        if(first.Sign!=0) { BigInteger remainder=result.Remainder(&first);if(remainder.Sign!=0)return 3; }
                        if(second.Sign!=0) { BigInteger remainder=result.Remainder(&second);if(remainder.Sign!=0)return 4; }
                        BigInteger self=BigInteger::LeastCommonMultiple(firstView,firstView);BigInteger absolute=first.Abs();
                        if(!self.Equals(&absolute))return 5;
                        BigInteger copy=result;BigInteger moved=move copy;copy=new();result=new(91);
                        if(!moved.Equals(&expected)||!first.Equals(&savedFirst)||!second.Equals(&savedSecond))return 6;
                        first=BigInteger::LeastCommonMultiple(&first,&second);
                        if(!first.Equals(&expected)||!second.Equals(&savedSecond))return 7;
                        first=savedFirst;
                        second=BigInteger::LeastCommonMultiple(&first,&second);
                        if(!second.Equals(&expected)||!first.Equals(&savedFirst))return 8;
                        first=new(17);second=new(19);if(!moved.Equals(&expected))return 9;
                    }
                    if(allocated!=released)return 10;
                }
                return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EitherZeroAllocatesNothingAndEveryMeasuredFailurePreservesInputAndDestinationStorage(int optimization)
    {
        Integer factor=Integer.Pow(10,90)+123;
        (Integer A,Integer B)[] rows=[(84,-126),(factor*84,-factor*126),(6765,10946),
            (Integer.Pow(10,90)+123,Integer.Pow(10,45)+17)];
        Run(Counters+$$"""
            int main() {
                {
                    BigInteger zero=new();BigInteger nonzero=new(-777);BigInteger target=new(888);
                    int before=attempts;deny=true;
                    {
                        BigInteger a=BigInteger::LeastCommonMultiple(&zero,&nonzero);
                        BigInteger b=BigInteger::LeastCommonMultiple(&nonzero,&zero);
                        BigInteger c=BigInteger::LeastCommonMultiple(&zero,&zero);
                        target=BigInteger::LeastCommonMultiple(&zero,&nonzero);
                        if(a.Sign!=0||b.Sign!=0||c.Sign!=0||target.Sign!=0||attempts!=before||nonzero.ToLong()!=-777)return 1;
                    }
                    deny=false;
                }
                readonly(char)*[{{rows.Length}}] firstText=[{{Array(rows.Select(row=>row.A))}}];
                readonly(char)*[{{rows.Length}}] secondText=[{{Array(rows.Select(row=>row.B))}}];
                readonly(char)*[{{rows.Length}}] expectedText=[{{Array(rows.Select(row=>Lcm(row.A,row.B)))}}];
                for(nuint row=0;row<{{rows.Length}};row++) {
                    {
                        captured=null;capture=true;BigInteger first=BigInteger::Parse(firstText[row]);capture=false;void*? firstStorage=captured;
                        captured=null;capture=true;BigInteger second=BigInteger::Parse(secondText[row]);capture=false;void*? secondStorage=captured;
                        BigInteger savedFirst=first;BigInteger savedSecond=second;BigInteger expected=BigInteger::Parse(expectedText[row]);
                        captured=null;capture=true;BigInteger target=new(777);capture=false;void*? targetStorage=captured;
                        for(int same=0;same<2;same++) {
                            immutable(BigInteger)* right=&second;if(same==1)right=&first;
                            int initial=allocated;int points=0;
                            {
                                BigInteger measured=BigInteger::LeastCommonMultiple(&first,right);points=allocated-initial;
                                if(points==0||measured.Sign<=0)return 2;
                                if(same==0&&!measured.Equals(&expected))return 3;
                            }
                            for(int destination=0;destination<3;destination++) {
                                BigInteger* output=&target;if(destination==1)output=&first;else if(destination==2)output=&second;
                                for(int point=0;point<points;point++) {
                                    watchFirst=firstStorage;watchSecond=secondStorage;watchTarget=targetStorage;watchedFrees=0;
                                    int balance=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                    try { *output=BigInteger::LeastCommonMultiple(&first,right);return 4; }
                                    catch(ref BadAllocException error) { caught=true; }
                                    failAfter=-1;
                                    long targetValue=0;
                                    if(!caught||denied==deniedBefore||allocated-released!=balance||watchedFrees!=0)return 5;
                                    if(!first.Equals(&savedFirst)||!second.Equals(&savedSecond)||!target.TryToLong(ref targetValue)||targetValue!=777)return 6;
                                    watchFirst=null;watchSecond=null;watchTarget=null;
                                }
                            }
                            BigInteger retry=BigInteger::LeastCommonMultiple(&first,right);
                            if(same==0&&!retry.Equals(&expected))return 7;
                        }
                    }
                    if(allocated!=released)return 8;
                }
                if(allocated!=released)return 9;return 0;
            }
            """,optimization);
    }

    private const string Counters="""
        using Standard; using Standard::Numerics;extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        bool capture=false;void*? captured=null;void*? watchFirst=null;void*? watchSecond=null;void*? watchTarget=null;int watchedFrees=0;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null){allocated++;if(capture)captured=value;}return value;
            }
            public replace void Free(void* value)nothrow {
                if(value==watchFirst||value==watchSecond||value==watchTarget)watchedFrees++;released++;free(value);
            }
        }
        """;
    private static void Run(string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-lcm.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);
        Assert.True(result.ExitCode==0,$"BigInteger LCM exit {result.ExitCode}: {result.StandardError}");
    }
}
