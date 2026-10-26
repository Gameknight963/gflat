using System.Globalization;
using System.Text;
using Integer=System.Numerics.BigInteger;

namespace gflat.Tests;

public sealed class StandardBigIntegerSignTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AbsAndNegateMatchDotNetAndReturnIndependentOwnersFromEveryReceiverView(int optimization)
    {
        var values=new List<Integer>{0,1,-1,long.MinValue,long.MaxValue,(Integer)long.MaxValue+1,-(Integer)long.MaxValue-2};
        foreach(int digits in new[]{9,18,36,360,3600}){
            Integer power=Integer.Pow(10,digits);values.AddRange([power-1,power,power+1,-power-1,-power,-power+1]);
        }
        var random=new Random(207319);
        for(int sample=0;sample<64;sample++){byte[] bytes=new byte[random.Next(1,257)];random.NextBytes(bytes);values.Add(new Integer(bytes));}
        await WithDirectory(directory=>{
            string path=Path.Combine(directory,"signs.bin");WriteManifest(path,values);
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);String absText=ReadText(&input);String negText=ReadText(&input);
                            captured=null;capture=true;BigInteger source=BigInteger::Parse(text.Data,text.Length);capture=false;void*? originalStorage=captured;
                            BigInteger saved=source;immutable(BigInteger)* immutableView=&source;readonly(BigInteger)* readonlyView=&source;
                            captured=null;capture=true;BigInteger absolute=source.Abs();capture=false;void*? absStorage=captured;
                            captured=null;capture=true;BigInteger negative=immutableView.Negate();capture=false;void*? negStorage=captured;
                            if(!TextEquals(&absolute,&absText)||!TextEquals(&negative,&negText)||!source.Equals(&saved))return 1;
                            if(source.Sign!=0&&(absStorage==null||negStorage==null||absStorage==originalStorage||negStorage==originalStorage||absStorage==negStorage))return 2;
                            if(source.Sign==0&&(absolute.Sign!=0||negative.Sign!=0))return 3;
                            BigInteger twice=negative.Negate();BigInteger readAbs=readonlyView.Abs();BigInteger readNeg=readonlyView.Negate();
                            if(!twice.Equals(&source)||!readAbs.Equals(&absolute)||!readNeg.Equals(&negative))return 4;
                            source=new(73);if(!TextEquals(&absolute,&absText)||!TextEquals(&negative,&negText))return 5;
                            absolute=new(99);negative=new(-77);long exact=0;if(!source.TryToLong(ref exact)||exact!=73||!TextEquals(&saved,&text))return 6;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 7;return 0;
                }
                """,[path],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ZeroResultsAllocateNothingAndEveryMeasuredCopyFailurePreservesInputAndDestinationStorage(int optimization)
        =>await WithDirectory(directory=>{
            string path=Path.Combine(directory,"failures.bin");WriteManifest(path,new Integer[]{1,-1,long.MinValue,Integer.Pow(10,72)+17,-Integer.Pow(10,3600)-123});
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        BigInteger zero=new();
                        BigInteger divisor=BigInteger::Parse("1000000000000000001");BigInteger factor=BigInteger::Parse("1000000001");BigInteger exact=divisor.Multiply(&factor);
                        BigInteger retained=new(999);BigInteger quotient=exact.DivRem(&divisor,ref retained);if(retained.Sign!=0)return 1;
                        int before=attempts;deny=true;
                        {BigInteger a=zero.Abs();BigInteger b=zero.Negate();BigInteger c=retained.Abs();BigInteger d=retained.Negate();
                         if(a.Sign!=0||b.Sign!=0||c.Sign!=0||d.Sign!=0||attempts!=before)return 2;}
                        deny=false;
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);String unusedAbs=ReadText(&input);String unusedNeg=ReadText(&input);
                            captured=null;capture=true;BigInteger source=BigInteger::Parse(text.Data,text.Length);capture=false;void*? sourceStorage=captured;BigInteger saved=source;
                            captured=null;capture=true;BigInteger target=new(777);capture=false;void*? targetStorage=captured;
                            immutable(BigInteger)* view=&source;
                            for(int operation=0;operation<2;operation++){
                                int initial=allocated;int points=0;
                                {BigInteger measured=new();if(operation==0)measured=view.Abs();else measured=view.Negate();points=allocated-initial;}
                                if(points==0||!source.Equals(&saved))return 3;
                                for(int point=0;point<points;point++){
                                    watchSource=sourceStorage;watchTarget=targetStorage;watchedFrees=0;int balance=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                    try{if(operation==0)target=view.Abs();else target=view.Negate();return 4;}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                                    long targetValue=0;
                                    if(!caught||denied==deniedBefore||allocated-released!=balance||watchedFrees!=0||!source.Equals(&saved)||!target.TryToLong(ref targetValue)||targetValue!=777)return 5;
                                    watchSource=null;watchTarget=null;
                                }
                            }
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 6;return 0;
                }
                """,[path],optimization);
        });

    private const string Counters="""
        using std; using std::Memory; using std::Numerics; using std::Collections;extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;bool deny=false;
        bool capture=false;void*? captured=null;void*? watchSource=null;void*? watchTarget=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny||failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null){allocated++;if(capture)captured=value;}return value;}
            public replace void Free(void* value)nothrow{if(value==watchSource||value==watchTarget)watchedFrees++;released++;free(value);}
        }
        bool TextEquals(immutable(BigInteger)* value,immutable(String)* expected){String actual=value.ToString();return actual.Equals(expected);}
        """;
    private const string ReadText="""
        String ReadText(File* input){nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;}
        """;
    private static void WriteManifest(string path,IEnumerable<Integer> input)
    {
        var values=input.ToArray();using var writer=new BinaryWriter(File.Create(path));writer.Write(values.Length);
        foreach(Integer value in values){WriteText(writer,value);WriteText(writer,Integer.Abs(value));WriteText(writer,Integer.Negate(value));}
    }
    private static void WriteText(BinaryWriter writer,Integer value){byte[] bytes=Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));writer.Write(bytes.Length);writer.Write(bytes);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-sign-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string source,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-sign.gf",source));
        string input=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");File.WriteAllText(input,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(output,arguments,30000);Assert.True(result.ExitCode==0,$"BigInteger sign exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
