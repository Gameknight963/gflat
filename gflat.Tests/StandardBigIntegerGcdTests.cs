using System.Globalization;
using System.Text;
using Integer=System.Numerics.BigInteger;

namespace gflat.Tests;

public sealed class StandardBigIntegerGcdTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SignedGcdsMatchDotNetDivideBothInputsAndOwnIndependentResults(int optimization)
    {
        var rows=new List<(Integer Left,Integer Right)>();
        void Signs(Integer first,Integer second){foreach(int a in new[]{1,-1})foreach(int b in new[]{1,-1})rows.Add((first*a,second*b));}
        foreach(var pair in new (Integer,Integer)[]{(0,0),(0,1),(1,0),(1,1),(17,17),(17,31),(84,126),
            (long.MinValue,long.MaxValue),(long.MinValue,0),(1_000_000_000,999_999_999),(999_999_999,999_999_999)})Signs(pair.Item1,pair.Item2);
        foreach(int digits in new[]{9,18,90,720,3600}){
            Integer factor=Integer.Pow(10,digits)+123;Signs(factor*84,factor*126);Signs(factor,17);Signs(factor,factor);
        }
        foreach(int count in new[]{80,240,600}){var pair=Fibonacci(count);Signs(pair.Left,pair.Right);}
        var random=new Random(613209);
        for(int sample=0;sample<32;sample++){
            byte[] a=new byte[random.Next(1,65)],b=new byte[random.Next(1,65)];random.NextBytes(a);random.NextBytes(b);
            rows.Add((new Integer(a),new Integer(b)));
        }
        await WithDirectory(directory=>{
            string path=Path.Combine(directory,"gcd.bin");WriteManifest(path,rows);
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String firstText=ReadText(&input);String secondText=ReadText(&input);String expected=ReadText(&input);
                            BigInteger first=BigInteger::Parse(firstText.Data,firstText.Length);BigInteger second=BigInteger::Parse(secondText.Data,secondText.Length);
                            BigInteger savedFirst=first;BigInteger savedSecond=second;immutable(BigInteger)* firstView=&first;immutable(BigInteger)* secondView=&second;
                            nuint firstHash=first.GetHashCode();nuint secondHash=second.GetHashCode();
                            BigInteger result=BigInteger::GreatestCommonDivisor(firstView,secondView);
                            BigInteger swapped=BigInteger::GreatestCommonDivisor(&second,&first);
                            if(!TextEquals(&result,&expected)||!swapped.Equals(&result)||result.Sign<0)return 1;
                            if(!first.Equals(&savedFirst)||!second.Equals(&savedSecond)||first.GetHashCode()!=firstHash||second.GetHashCode()!=secondHash)return 2;
                            if(expected.Length==1&&expected[0]=='0'&&result.Sign!=0)return 3;
                            if(result.Sign!=0){BigInteger a=first.Remainder(&result);BigInteger b=second.Remainder(&result);if(a.Sign!=0||b.Sign!=0)return 4;}
                            BigInteger self=BigInteger::GreatestCommonDivisor(firstView,firstView);BigInteger absolute=first.Abs();if(!self.Equals(&absolute))return 5;
                            BigInteger survivor=BigInteger::GreatestCommonDivisor(&first,&second);
                            result=new(91);if(!first.Equals(&savedFirst)||!second.Equals(&savedSecond))return 6;
                            first=new(17);second=new(19);if(!TextEquals(&survivor,&expected))return 7;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 8;return 0;
                }
                """,[path],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ZeroGcdAllocatesNothingAndEveryMeasuredFailurePreservesInputsAndOldDestinationStorage(int optimization)
        =>await WithDirectory(directory=>{
            Integer huge=Integer.Pow(10,360)+123;var fib=Fibonacci(45);
            (Integer Left,Integer Right)[] pairs=[(0,0),(0,-huge),(-huge,0),(84,126),(huge*84,-huge*126),fib];
            string path=Path.Combine(directory,"gcd-failures.bin");WriteManifest(path,pairs);
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        BigInteger zero=new();BigInteger otherZero=new();BigInteger old=new(777);int before=attempts;deny=true;
                        {BigInteger result=BigInteger::GreatestCommonDivisor(&zero,&otherZero);BigInteger self=BigInteger::GreatestCommonDivisor(&zero,&zero);
                         old=BigInteger::GreatestCommonDivisor(&zero,&otherZero);if(result.Sign!=0||self.Sign!=0||old.Sign!=0||attempts!=before)return 1;}
                        deny=false;
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String firstText=ReadText(&input);String secondText=ReadText(&input);String expected=ReadText(&input);
                            captured=null;capture=true;BigInteger first=BigInteger::Parse(firstText.Data,firstText.Length);capture=false;void*? firstStorage=captured;
                            captured=null;capture=true;BigInteger second=BigInteger::Parse(secondText.Data,secondText.Length);capture=false;void*? secondStorage=captured;
                            BigInteger savedFirst=first;BigInteger savedSecond=second;
                            captured=null;capture=true;BigInteger target=new(777);capture=false;void*? targetStorage=captured;
                            for(int same=0;same<2;same++){
                                immutable(BigInteger)* right=&second;if(same==1)right=&first;
                                int initial=allocated;int points=0;
                                {BigInteger measured=BigInteger::GreatestCommonDivisor(&first,right);points=allocated-initial;
                                 if(measured.Sign!=0&&points==0)return 2;}
                                // Includes the final real allocation after many iterations
                                // on adjacent Fibonacci inputs; no algorithm count assumed.
                                for(int point=0;point<points;point++){
                                    watchFirst=firstStorage;watchSecond=secondStorage;watchTarget=targetStorage;watchedFrees=0;
                                    int balance=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                    try{target=BigInteger::GreatestCommonDivisor(&first,right);return 3;}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                                    long targetValue=0;
                                    if(!caught||denied==deniedBefore||allocated-released!=balance||watchedFrees!=0||!first.Equals(&savedFirst)||!second.Equals(&savedSecond)||!target.TryToLong(ref targetValue)||targetValue!=777)return 4;
                                    watchFirst=null;watchSecond=null;watchTarget=null;
                                }
                            }
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 5;return 0;
                }
                """,[path],optimization);
        });

    private static (Integer Left,Integer Right) Fibonacci(int count)
    {
        Integer first=0,second=1;for(int i=0;i<count;i++)(first,second)=(second,first+second);return(first,second);
    }
    private const string Counters="""
        using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        bool capture=false;void*? captured=null;void*? watchFirst=null;void*? watchSecond=null;void*? watchTarget=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null){allocated++;if(capture)captured=value;}return value;}
            public replace void Free(void* value)nothrow{if(value==watchFirst||value==watchSecond||value==watchTarget)watchedFrees++;released++;free(value);}
        }
        bool TextEquals(immutable(BigInteger)* value,immutable(String)* expected){String actual=value.ToString();return actual.Equals(expected);}
        """;
    private const string ReadText="""
        String ReadText(File* input){nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;}
        """;
    private static void WriteManifest(string path,IEnumerable<(Integer Left,Integer Right)> input)
    {
        var rows=input.ToArray();using var writer=new BinaryWriter(File.Create(path));writer.Write(rows.Length);
        foreach(var row in rows){WriteText(writer,row.Left);WriteText(writer,row.Right);WriteText(writer,Integer.GreatestCommonDivisor(row.Left,row.Right));}
    }
    private static void WriteText(BinaryWriter writer,Integer value){byte[] text=Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));writer.Write(text.Length);writer.Write(text);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-gcd-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string source,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-gcd.gf",source));
        string input=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");File.WriteAllText(input,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(output,arguments,30000);Assert.True(result.ExitCode==0,$"BigInteger GCD exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
