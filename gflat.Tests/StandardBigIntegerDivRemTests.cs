using System.Globalization;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests;

public sealed class StandardBigIntegerDivRemTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedQuotientsAndRemaindersMatchIndependentBigInteger(int optimization)
        => WithDirectory(directory=>{
            List<Integer> inputs=[0,1,-1,long.MinValue,long.MaxValue];
            foreach(int digits in new[]{9,18,27,36,72,3600}){
                Integer power=Integer.Pow(10,digits);
                inputs.AddRange([power-1,power,power+1,-power-1,-power,-power+1]);
            }
            int[] divisors=[1,-1,2,-2,7,-7,999999999,-999999999,1000000000,-1000000000,int.MinValue,int.MaxValue];
            var rows=new List<(Integer Value,int Divisor)>();
            foreach(Integer value in inputs)foreach(int divisor in divisors)rows.Add((value,divisor));
            var random=new Random(681931);
            for(int i=0;i<160;i++){
                byte[] bytes=new byte[random.Next(1,257)];random.NextBytes(bytes);Integer value=new(bytes);
                int divisor=(int)random.NextInt64(int.MinValue,(long)int.MaxValue+1);if(divisor==0)divisor=1;
                rows.Add((value,divisor));
            }
            string path=Path.Combine(directory,"division.bin");
            using(var writer=new BinaryWriter(File.Create(path))){
                writer.Write(rows.Count);
                foreach(var row in rows){
                    Integer quotient=Integer.DivRem(row.Value,row.Divisor,out Integer remainder);
                    Assert.Equal(row.Value,quotient*row.Divisor+remainder);
                    Assert.True(Integer.Abs(remainder)<Integer.Abs(new Integer(row.Divisor)));
                    WriteText(writer,row.Value);writer.Write(row.Divisor);WriteText(writer,quotient);writer.Write((int)remainder);
                }
            }
            Run(directory,"""
                using std; using std::Collections;
                String ReadText(File* input){
                    nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);
                    String value=new();value.Resize(count);Span<char> bytes=value.AsSpan();
                    input.ReadExactly(bytes.Data!,count);return move value;
                }
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                    for(int row=0;row<rows;row++){
                        String sourceText=ReadText(&input);int divisor=FileBinary::ReadInt32LittleEndian(&input);
                        String expectedText=ReadText(&input);int wanted=FileBinary::ReadInt32LittleEndian(&input);
                        BigInteger value=BigInteger::Parse(sourceText.Data,sourceText.Length);nuint hash=value.GetHashCode();
                        immutable(BigInteger)* view=&value;int remainder=123;
                        BigInteger quotient=view.DivRem(divisor,ref remainder);
                        String actual=quotient.ToString();if(!actual.Equals(&expectedText)||remainder!=wanted)return 1;
                        String unchanged=value.ToString();if(!unchanged.Equals(&sourceText)||value.GetHashCode()!=hash)return 2;
                        if(expectedText.Equals("0")&&quotient.Sign!=0)return 3;
                    }
                    input.Close();return 0;
                }
                """,[path],optimization);
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QuotientOwnersAreIndependentAndRepeatedDivisionConvertsRadices(int optimization)
        => WithDirectory(directory=>Run(directory,Counters+"""
            String ToRadix(immutable(BigInteger)* input,int radix){
                readonly(char)* digits="0123456789abcdefghijklmnopqrstuvwxyz";BigInteger current=*input;
                bool negative=current.Sign<0;String output=new();
                while(current.Sign!=0){int rest=0;current=current.DivRem(radix,ref rest);if(rest<0)rest=-rest;output.Append(digits[rest]);}
                if(output.IsEmpty)output.Append('0');if(negative)output.Append('-');Reverse<char>(output.AsSpan());return move output;
            }
            int main(){
                {
                    BigInteger input=BigInteger::Parse("18446744073709551616");BigInteger saved=input;
                    int beforeTarget=allocated;BigInteger target=new(777);int targetAllocations=allocated-beforeTarget;
                    int remainder=42;int releasedBefore=released;
                    target=input.DivRem(16,ref remainder);
                    if(released-releasedBefore!=targetAllocations)return 1;
                    if(remainder!=0||!TextEquals(&target,"1152921504606846976"))return 1;
                    target=new(5);if(!input.Equals(&saved)||!TextEquals(&target,"5"))return 2;
                    int again=-1;immutable(BigInteger)* deep=&input;readonly(BigInteger)* shallow=&input;
                    BigInteger first=deep.DivRem(-7,ref remainder);BigInteger second=shallow.DivRem(-7,ref again);
                    if(remainder!=again||!first.Equals(&second)||!input.Equals(&saved))return 3;
                    String hex=ToRadix(&input,16);if(!hex.Equals("10000000000000000"))return 4;
                    BigInteger negative=BigInteger::Parse("-18446744073709551616");String base36=ToRadix(&negative,36);
                    if(!base36.Equals("-3w5e11264sgsg"))return 5;
                    BigInteger zero=new();int before=allocated;deny=true;remainder=99;
                    {BigInteger result=zero.DivRem(-2147483648,ref remainder);if(result.Sign!=0||remainder!=0||allocated!=before)return 6;}
                    deny=false;
                    BigInteger tiny=new(1);{BigInteger result=tiny.DivRem(2,ref remainder);if(result.Sign!=0||remainder!=1)return 7;}
                }
                if(allocated!=released)return 8;return 0;
            }
            """,[],optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DivisionErrorsPreserveReceiverRemainderDestinationAndCleanPartialResults(int optimization)
        => WithDirectory(directory=>Run(directory,Counters+"""
            int main(){
                {
                    BigInteger source=BigInteger::Parse("-123456789012345678901234567890123456789012345678901234567890");
                    BigInteger saved=source;BigInteger target=new(777);int remainder=47;
                    int before=allocated;int points=0;
                    {BigInteger proof=source.DivRem(7,ref remainder);points=allocated-before;}
                    if(points==0)return 1;
                    for(int point=0;point<points;point++){
                        int live=allocated-released;remainder=47;failAfter=point;
                        try{target=source.DivRem(7,ref remainder);return 2;}catch(ref BadAllocException error){}
                        failAfter=-1;
                        if(remainder!=47||allocated-released!=live||!source.Equals(&saved)||!TextEquals(&target,"777"))return 3;
                    }
                    // Measure ordinary Exception storage separately; it is not quotient storage.
                    before=allocated;try{throw new Exception("control");}catch(ref Exception error){}
                    int exceptionAllocations=allocated-before;before=allocated;
                    bool caught=false;remainder=47;
                    try{target=source.DivRem(0,ref remainder);}catch(ref Exception error){caught=true;}
                    if(allocated-before!=exceptionAllocations)return 5;
                    if(!caught||remainder!=47||!source.Equals(&saved)||!TextEquals(&target,"777"))return 4;
                }
                if(allocated!=released)return 6;return 0;
            }
            """,[],optimization));

    private const string Counters="""
        using std; using std::Collections;
        extern void*? malloc(nuint size);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int failAfter=-1;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint size){
                attempts++;if(deny||failAfter==0)return null;if(failAfter>0)failAfter--;
                void*? value=malloc(size);if(value!=null)allocated++;return value;
            }
            public replace void Free(void* value) nothrow{released++;free(value);}
        }
        bool TextEquals(immutable(BigInteger)* input,readonly(char)* expected){String text=input.ToString();return text.Equals(expected);}
        """;

    private static void WriteText(BinaryWriter writer,Integer value)
    {
        byte[] text=System.Text.Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));writer.Write(text.Length);writer.Write(text);
    }
    private static void WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-divrem-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{Directory.Delete(directory,true);}
    }
    private static void Run(string directory,string program,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-divrem.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(executable,arguments,30000);Assert.True(result.ExitCode==0,$"BigInteger DivRem exit{result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
