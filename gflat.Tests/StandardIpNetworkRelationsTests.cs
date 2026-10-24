using Integer=System.Numerics.BigInteger;
using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardIpNetworkRelationsTests
{
    private sealed record Network(byte[] Bytes,int Prefix)
    {
        public int Width=>Bytes.Length*8;
        public (Integer First,Integer Last) Interval {
            get {
                Integer size=Integer.One<<(Width-Prefix);
                Integer first=new Integer(Bytes,isUnsigned:true,isBigEndian:true)/size*size;
                return(first,first+size-1);
            }
        }
    }

    private static IEnumerable<(Network First,Network Second)> Cases()
    {
        var random=new Random(709381);
        foreach(int count in new[]{4,16}) {
            int width=count*8;
            for(int prefix=0;prefix<=width;prefix++) {
                byte[] raw=new byte[count];random.NextBytes(raw);
                var network=new Network(raw,prefix);
                yield return(network,network);
                if(prefix<width) {
                    Integer childAddress=network.Interval.First+(Integer.One<<(width-prefix-1));
                    yield return(network,new Network(Bytes(childAddress,count),prefix+1));
                }
                if(prefix!=0) {
                    // Flip a prefix bit: neighboring, equally sized disjoint intervals.
                    Integer other=network.Interval.First^(Integer.One<<(width-prefix));
                    yield return(network,new Network(Bytes(other,count),prefix));
                }
                byte[] otherFamily=new byte[count==4?16:4];
                yield return(network,new Network(otherFamily,0));
            }
        }
        byte[] v4=[192,0,2,129];byte[] mapped=[0,0,0,0,0,0,0,0,0,0,255,255,192,0,2,129];
        foreach(int prefix in new[]{0,1,24,32})
        foreach(int mappedPrefix in new[]{0,64,95,96,97,120,128}) {
            yield return(new Network(v4,prefix),new Network(mapped,mappedPrefix));
            yield return(new Network(mapped,mappedPrefix),new Network(mapped,128));
        }
        for(int sample=0;sample<96;sample++) {
            int firstCount=random.Next(2)==0?4:16,secondCount=random.Next(2)==0?4:16;
            byte[] a=new byte[firstCount],b=new byte[secondCount];random.NextBytes(a);random.NextBytes(b);
            yield return(new Network(a,random.Next(firstCount*8+1)),new Network(b,random.Next(secondCount*8+1)));
        }
    }

    private static byte[] Bytes(Integer value,int count)
    {
        byte[] bytes=value.ToByteArray(isUnsigned:true,isBigEndian:true);
        byte[] result=new byte[count];bytes.CopyTo(result,count-bytes.Length);return result;
    }

    // Independent inclusive interval oracle: no reuse of the product's prefix-containment branches.
    private static int Flags(Network a,Network b)
    {
        if(a.Width!=b.Width)return 0;
        var x=a.Interval;var y=b.Interval;
        return(x.First<=y.First&&x.Last>=y.Last?1:0)
            |(y.First<=x.First&&y.Last>=x.Last?2:0)
            |(x.First<=y.Last&&y.First<=x.Last?4:0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllPrefixesAndSeededIntervalsMatchIndependentOracleWithStrictFamiliesAndNoAllocation(int optimization)
    {
        var rows=Cases().ToArray();var manifest=new List<byte>();
        foreach(var row in rows) {
            manifest.Add((byte)row.First.Bytes.Length);manifest.Add((byte)row.First.Prefix);manifest.AddRange(row.First.Bytes);
            manifest.Add((byte)row.Second.Bytes.Length);manifest.Add((byte)row.Second.Prefix);manifest.AddRange(row.Second.Bytes);
            manifest.Add((byte)Flags(row.First,row.Second));
        }
        string text=Convert.ToHexString(manifest.ToArray());
        Run($$"""
            using std;
            int attempts=0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { attempts++;return null; }
                public replace void Free(void* storage) nothrow {}
            }
            int Digit(char value) nothrow { if(value<='9')return value-'0';return value-'A'+10; }
            int Read(readonly(char)* text,ref nuint position) nothrow {
                int value=(Digit(text[position])<<4)|Digit(text[position+1]);position+=2;return value;
            }
            bool Observe(immutable(IpNetwork)* a,immutable(IpNetwork)* b) nothrow => a.Contains(b)&&a.Overlaps(b);
            bool ObserveReadOnly(readonly(IpNetwork)* a,immutable(IpNetwork)* b) nothrow => a.Contains(b)&&a.Overlaps(b);
            int main() {
                readonly(char)* text="{{text}}";nuint position=0;byte[16] a;byte[16] b;
                for(int row=0;row<{{rows.Length}};row++) {
                    int aCount=Read(text,ref position);int aPrefix=Read(text,ref position);
                    for(int index=0;index<aCount;index++)a[index]=(byte)Read(text,ref position);
                    int bCount=Read(text,ref position);int bPrefix=Read(text,ref position);
                    for(int index=0;index<bCount;index++)b[index]=(byte)Read(text,ref position);
                    int expected=Read(text,ref position);
                    IpAddress firstAddress=IpAddress::FromBytes(a,(nuint)aCount);IpAddress secondAddress=IpAddress::FromBytes(b,(nuint)bCount);
                    IpNetwork first=IpNetwork::FromAddress(firstAddress,aPrefix);IpNetwork second=IpNetwork::FromAddress(secondAddress,bPrefix);
                    IpNetwork savedFirst=first;IpNetwork savedSecond=second;
                    immutable(IpNetwork)* firstView=&first;immutable(IpNetwork)* secondView=&second;readonly(IpNetwork)* firstReadOnly=&first;
                    bool contains=firstView.Contains(secondView);bool contained=secondView.Contains(firstView);
                    bool overlaps=firstView.Overlaps(secondView);bool reverseOverlap=secondView.Overlaps(firstView);
                    if(contains!=((expected&1)!=0)||contained!=((expected&2)!=0)||overlaps!=((expected&4)!=0)||overlaps!=reverseOverlap)return 1;
                    if(Observe(firstView,secondView)!=(contains&&overlaps)||ObserveReadOnly(firstReadOnly,secondView)!=(contains&&overlaps))return 2;
                    if(!firstView.Contains(firstView)||!firstView.Overlaps(firstView)||!secondView.Contains(secondView)||!secondView.Overlaps(secondView))return 3;
                    if(!first.Equals(&savedFirst)||!second.Equals(&savedSecond))return 4;
                    // The existing address overload must remain available beside the network overload.
                    IpAddress ownBase=first.NetworkAddress;if(!firstView.Contains(&ownBase))return 5;
                }
                if(position!={{text.Length}}||attempts!=0)return 6;return 0;
            }
            """,optimization);
    }

    [Fact]
    public void ExistingAndNewContainmentAndProtectedWrappersRemainNothrowWithoutExceptionBoundaries()
    {
        string ir=Compiler.Emit(Sources("""
            bool Observe(immutable(std::IpNetwork)* a,immutable(std::IpNetwork)* b) nothrow => a.Contains(b)&&a.Overlaps(b);
            bool ObserveReadOnly(readonly(std::IpNetwork)* a,immutable(std::IpNetwork)* b) nothrow => a.Contains(b)&&a.Overlaps(b);
            int main() { return 0; }
            """));
        var members=Regex.Matches(ir,@"(?ms)^define [^\r\n]*\$IpNetwork\$(Contains|Overlaps)(?:\$[^\r\n(]*)?\([^\r\n]*\{\r?\n.*?^\}");
        Assert.Equal(3,members.Count);
        var wrappers=Regex.Matches(ir,@"(?ms)^define [^\r\n]*\$(Observe|ObserveReadOnly)\([^\r\n]*\{\r?\n.*?^\}");
        Assert.Equal(2,wrappers.Count);
        foreach(Match body in members.Cast<Match>().Concat(wrappers.Cast<Match>())) {
            Assert.Contains("nounwind",body.Value.Split('\n')[0]);
            Assert.DoesNotContain("invoke",body.Value);Assert.DoesNotContain("__gflat_eh_",body.Value);
            Assert.DoesNotContain("$Memory$Allocate",body.Value);
        }
    }

    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("network-relations.gf",program));
    private static void Run(string program,int optimization) {
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"IP network relations exit {result.ExitCode}: {result.StandardError}");
    }
}
