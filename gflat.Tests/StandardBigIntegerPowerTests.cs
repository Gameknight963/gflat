using System.Globalization;
using System.Text;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests;

public sealed class StandardBigIntegerPowerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PowersMatchIndependentBigIntegerIncludingFullUIntUnitParity(int optimization)
    {
        var rows=new List<(Integer Value,uint Exponent,Integer Expected)>();
        void Add(Integer value,int exponent)=>rows.Add((value,(uint)exponent,Integer.Pow(value,exponent)));
        for(int value=-5;value<=5;value++)for(int exponent=0;exponent<=12;exponent++)Add(value,exponent);
        foreach(Integer value in new Integer[]{int.MinValue,int.MaxValue,long.MinValue,long.MaxValue})
            foreach(int exponent in new[]{0,1,2,3,7,16,31})Add(value,exponent);
        foreach(int digits in new[]{9,18,72,240})
        {
            Integer value=Integer.Pow(10,digits)+123;
            foreach(int exponent in new[]{0,1,2,5}){Add(value,exponent);Add(-value,exponent);}
        }
        Add(2,1024);Add(-2,1023);Add(10,1000);Add(-10,1001);
        var random=new Random(271828);
        for(int sample=0;sample<36;sample++)
        {
            byte[] bytes=new byte[random.Next(1,97)];random.NextBytes(bytes);Add(new Integer(bytes),random.Next(17));
        }
        foreach(uint exponent in new uint[]{int.MaxValue,(uint)int.MaxValue+1,uint.MaxValue-1,uint.MaxValue})
            foreach(int value in new[]{-1,0,1})
            {
                Integer expected=value==0?Integer.Zero:value<0&&exponent%2!=0?-Integer.One:Integer.One;
                rows.Add((value,exponent,expected));
            }
        await WithDirectory(directory=>
        {
            string manifest=Path.Combine(directory,"powers.bin");
            using(var writer=new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count);
                foreach(var row in rows){WriteText(writer,row.Value);writer.Write(row.Exponent);WriteText(writer,row.Expected);}
            }
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String original=ReadText(&input);uint exponent=FileBinary::ReadUInt32LittleEndian(&input);
                            String expected=ReadText(&input);BigInteger source=BigInteger::Parse(original.Data,original.Length);
                            BigInteger saved=source;nuint hash=source.GetHashCode();immutable(BigInteger)* view=&source;
                            BigInteger result=view.Pow(exponent);String actual=result.ToString();
                            if(!actual.Equals(&expected)||!source.Equals(&saved)||source.GetHashCode()!=hash)return 1;
                            String unchanged=source.ToString();if(!unchanged.Equals(&original))return 2;
                            result=new(73);if(!source.Equals(&saved))return 3;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 4;return 0;
                }
                """,[manifest],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ZeroPositivePowersAllocateNothingAndReturnedOwnersRemainIndependent(int optimization)
        => await WithDirectory(directory=>Run(directory,Counters+"""
            int main(){
                {
                    BigInteger zero=new();uint[4] positive=[1u,2u,2147483648u,4294967295u];
                    for(nuint i=0;i<4;i++){
                        int before=attempts;deny=true;
                        {BigInteger result=zero.Pow(positive[i]);if(result.Sign!=0||attempts!=before||zero.Sign!=0)return 1;}
                        deny=false;
                    }
                    int before=allocated;
                    {BigInteger identity=zero.Pow(0u);int identityAllocations=allocated-before;BigInteger one=new(1);
                        if(!identity.Equals(&one)||identityAllocations==0||zero.Sign!=0)return 2;}
                    BigInteger source=BigInteger::Parse("-1234567890123456789012345678901234567890");
                    BigInteger saved=source;BigInteger result=source.Pow(1u);
                    if(!result.Equals(&saved)||!source.Equals(&saved))return 3;
                    source=new();if(!result.Equals(&saved))return 4;
                    result=new(91);String preserved=saved.ToString();
                    if(!preserved.Equals("-1234567890123456789012345678901234567890"))return 5;
                    // Keep two nonzero results alive after their receiver is destroyed.
                    BigInteger first=new();BigInteger second=new();
                    {BigInteger receiver=new(-1);first=receiver.Pow(4294967295u);second=receiver.Pow(4294967294u);}
                    BigInteger minusOne=new(-1);BigInteger plusOne=new(1);
                    if(!first.Equals(&minusOne)||!second.Equals(&plusOne))return 6;
                }
                if(allocated!=released)return 7;return 0;
            }
            """,[],optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryMeasuredAllocationFailurePreservesReceiverDestinationAndReleasesPartialResults(int optimization)
    {
        await WithDirectory(directory=>
        {
            (string Value,uint Exponent)[] rows=[("123456789012345678901234567",13),("-98765432109876543210",9),
                ("12345678901234567890",1),("1",uint.MaxValue),("-1",uint.MaxValue),("0",0),("0",uint.MaxValue)];
            string manifest=Path.Combine(directory,"failures.bin");
            using(var writer=new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Length);
                foreach(var row in rows){WriteText(writer,Encoding.ASCII.GetBytes(row.Value));writer.Write(row.Exponent);}
            }
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        BigInteger target=BigInteger::Parse("777777777777777777777");BigInteger oldTarget=target;
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);uint exponent=FileBinary::ReadUInt32LittleEndian(&input);
                            BigInteger source=BigInteger::Parse(text.Data,text.Length);BigInteger saved=source;
                            int before=allocated;int points=0;
                            {BigInteger probe=source.Pow(exponent);points=allocated-before;}
                            if(!source.Equals(&saved)||!target.Equals(&oldTarget))return 1;
                            // Discover all real successful allocations without assuming
                            // an implementation's multiplication or growth strategy.
                            for(int point=0;point<points;point++){
                                int live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                try{target=source.Pow(exponent);return 2;}catch(BadAllocException* error){caught=true;}
                                failAfter=-1;
                                // Failed exception-payload calls may use the EH reserve;
                                // live accounting counts only successful app allocations.
                                if(!caught||denied==deniedBefore||allocated-released!=live)return 3;
                                if(!source.Equals(&saved)||!target.Equals(&oldTarget))return 4;
                            }
                            if(source.Sign!=0||exponent==0u){if(points==0)return 5;}
                            else if(points!=0)return 6;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 7;return 0;
                }
                """,[manifest],optimization);
        });
    }

    private const string Counters="""
        using std; using std::Collections;
        extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null)allocated++;return value;
            }
            public replace void Free(void* value)nothrow{released++;free(value);}
        }
        """;
    private const string ReadText="""
        String ReadText(File* input){
            nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;
        }
        """;
    private static void WriteText(BinaryWriter writer,Integer value)
        => WriteText(writer,Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
    private static void WriteText(BinaryWriter writer,byte[] text){writer.Write(text.Length);writer.Write(text);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-pow-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-power-test.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments);Assert.True(result.ExitCode==0,$"BigInteger power exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
