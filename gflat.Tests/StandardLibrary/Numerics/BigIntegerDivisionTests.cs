using System.Globalization;
using System.Text;
using Integer=System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

// Branch witnesses are algebraic/host oracles, independent of the implementation.
public sealed class BigIntegerDivisionTests
{
    private static readonly Integer B=1_000_000_000;
    private static (Integer Left,Integer Right)[] Branches()=>[
        // Subtraction underflow/add-back: u=v*(B+1)-1.
        ((B/2*B*B+1)*(B+1)-1,B/2*B*B+1),
        // qhat clamp at B: u=2*v*B-1.
        (2*(B/2*B+1)*B-1,B/2*B+1),
        // Two preliminary decrements, independently derived by math reviewer:
        // q=B-3, r=4B-3; normalized v=[B-1,B/2], u=[0,B/2,B/2-1].
        (Integer.Parse("499999999500000000000000000",CultureInfo.InvariantCulture),Integer.Parse("500000000999999999",CultureInfo.InvariantCulture)),
        // One preliminary decrement; exact q=499999999, r=1499999999.
        (Integer.Parse("250000000000000000000000000",CultureInfo.InvariantCulture),Integer.Parse("500000000999999999",CultureInfo.InvariantCulture)),
        // d=B/2 and normalized remainder B, exercising denormalization carry.
        (Integer.Parse("1000000000000000001",CultureInfo.InvariantCulture),Integer.Parse("1000000001",CultureInfo.InvariantCulture)),
        // Small top limb requires normalization scale, sparse internal digits.
        (B*B*B*B+B*B+17,B*B+3),
        ((B-1)*B*B*B+(B-1)*B+9,(B-1)*B+1)
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task GeneralDivisorsMatchDotNetAndReconstructEverySignedInput(int optimization)
    {
        var cases=new List<(Integer Left,Integer Right)>();
        void Signs(Integer left,Integer right){foreach(int a in new[]{1,-1})foreach(int b in new[]{1,-1})cases.Add((left*a,right*b));}
        foreach(var item in Branches())Signs(item.Left,item.Right);
        foreach(Integer divisor in new Integer[]{1,2,7,999_999_999,B,B+1,int.MaxValue,(Integer)int.MaxValue+1}){
            Signs(0,divisor);Signs(divisor-1,divisor);Signs(divisor,divisor);Signs(divisor+1,divisor);
            Signs(Integer.Pow(10,90)+123456789,divisor);
        }
        foreach(int digits in new[]{18,72,360,1500,4000}){
            Integer value=Integer.Pow(10,digits)+Integer.Pow(10,digits/2)+17;
            Signs(value,Integer.Pow(10,Math.Max(9,digits/2))+3);
            Signs(value,value);Signs(value+1,value);
        }
        var random=new Random(719283);
        Integer RandomInteger(int digits){char[] text=new char[digits];text[0]=(char)('1'+random.Next(9));for(int i=1;i<digits;i++)text[i]=(char)('0'+random.Next(10));return Integer.Parse(new string(text),CultureInfo.InvariantCulture);}
        for(int sample=0;sample<96;sample++){
            Integer left=RandomInteger(random.Next(1,601)),right=RandomInteger(random.Next(1,401));
            if(sample%2!=0)left=-left;if(sample%3==0)right=-right;cases.Add((left,right));
        }
        await WithDirectory(directory=>{
            string path=Path.Combine(directory,"oracle.bin");WriteManifest(path,cases);
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String aText=ReadText(&input);String bText=ReadText(&input);String qText=ReadText(&input);String rText=ReadText(&input);
                            BigInteger a=BigInteger::Parse(aText.Data,aText.Length);BigInteger b=BigInteger::Parse(bText.Data,bText.Length);BigInteger savedA=a;BigInteger savedB=b;
                            BigInteger remainder=new(333);immutable(BigInteger)* view=&a;BigInteger quotient=view.DivRem(&b,ref remainder);
                            if(!TextEquals(&quotient,&qText)||!TextEquals(&remainder,&rText)||!a.Equals(&savedA)||!b.Equals(&savedB))return 1;
                            BigInteger product=quotient.Multiply(&b);BigInteger restored=product.Add(&remainder);if(!restored.Equals(&a))return 2;
                            BigInteger minusOne=new(-1);BigInteger absR=remainder;BigInteger absB=b;
                            if(absR.Sign<0)absR=remainder.Multiply(&minusOne);if(absB.Sign<0)absB=b.Multiply(&minusOne);
                            if(absR.CompareTo(&absB)>=0||(remainder.Sign!=0&&remainder.Sign!=a.Sign))return 3;
                            if(rText.Length==1&&rText[0]=='0'&&remainder.Sign!=0)return 4;
                            BigInteger divided=view.Divide(&b);BigInteger residual=view.Remainder(&b);
                            if(!divided.Equals(&quotient)||!residual.Equals(&remainder))return 5;
                            long scalar=0;if(b.TryToLong(ref scalar)&&scalar>=-2147483648l&&scalar<=2147483647l){
                                int oldR=999;BigInteger oldQ=a.DivRem((int)scalar,ref oldR);BigInteger oldRBig=new((long)oldR);
                                if(!oldQ.Equals(&quotient)||!oldRBig.Equals(&remainder))return 6;
                            }
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
    public async Task RemainderMayAliasEitherOperandAndSelfDivisionKeepsTheReturnedOwnerIndependent(int optimization)
        =>await WithDirectory(directory=>{
            string path=Path.Combine(directory,"aliases.bin");WriteManifest(path,AliasCases());
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String aText=ReadText(&input);String bText=ReadText(&input);String qText=ReadText(&input);String rText=ReadText(&input);
                            BigInteger originalA=BigInteger::Parse(aText.Data,aText.Length);BigInteger originalB=BigInteger::Parse(bText.Data,bText.Length);
                            BigInteger expectedQ=BigInteger::Parse(qText.Data,qText.Length);BigInteger expectedR=BigInteger::Parse(rText.Data,rText.Length);
                            for(int kind=0;kind<5;kind++){
                                if(kind>=3&&originalA.Sign==0)continue;
                                BigInteger a=originalA;BigInteger b=originalB;BigInteger output=new(333);BigInteger qExpected=expectedQ;BigInteger rExpected=expectedR;
                                BigInteger* divisor=&b;BigInteger* destination=&output;
                                if(kind==1||kind==4)destination=&a;else if(kind==2)destination=&b;
                                if(kind>=3){divisor=&a;qExpected=new(1);rExpected=new();}
                                BigInteger quotient=a.DivRem(divisor,ref *destination);
                                if(!quotient.Equals(&qExpected)||!destination.Equals(&rExpected))return 1;
                                if(kind!=1&&kind!=4&&!a.Equals(&originalA))return 2;
                                if(kind!=2&&!b.Equals(&originalB))return 3;
                                // Replacing the quotient must not release either input or
                                // the freshly committed remainder's independent storage.
                                quotient=new(999);if(!destination.Equals(&rExpected))return 4;
                                *destination=new(-777);long exact=0;if(!quotient.TryToLong(ref exact)||exact!=999)return 5;
                            }
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 6;return 0;
                }
                """,[path],optimization);
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryMeasuredAllocationFailurePreservesOperandAndOutputValuesAndStorage(int optimization)
        =>await WithDirectory(directory=>{
            string path=Path.Combine(directory,"failures.bin");WriteManifest(path,AliasCases());
            Run(directory,Counters+ReadText+"""
                BigInteger Call(BigInteger* left,BigInteger* right,BigInteger* output,int kind){
                    if(kind==1)return left.DivRem(right,ref *left);
                    if(kind==2)return left.DivRem(right,ref *right);
                    if(kind==3)return left.DivRem(left,ref *output);
                    if(kind==4)return left.DivRem(left,ref *left);
                    if(kind==5)return left.Divide(right);
                    if(kind==6)return left.Remainder(right);
                    return left.DivRem(right,ref *output);
                }
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);
                        for(int row=0;row<rows;row++){
                            String aText=ReadText(&input);String bText=ReadText(&input);String discardQ=ReadText(&input);String discardR=ReadText(&input);
                            BigInteger originalA=BigInteger::Parse(aText.Data,aText.Length);BigInteger originalB=BigInteger::Parse(bText.Data,bText.Length);
                            BigInteger oldOutput=new(333);BigInteger oldTarget=new(777);
                            for(int kind=0;kind<7;kind++){
                                if((kind==3||kind==4)&&originalA.Sign==0)continue;int points=0;
                                {BigInteger a=originalA;BigInteger b=originalB;BigInteger output=oldOutput;int before=allocated;BigInteger q=Call(&a,&b,&output,kind);points=allocated-before;}
                                for(int point=0;point<points;point++){
                                    captured=null;capture=true;BigInteger a=originalA;capture=false;watchA=captured;
                                    captured=null;capture=true;BigInteger b=originalB;capture=false;watchB=captured;
                                    captured=null;capture=true;BigInteger output=oldOutput;capture=false;watchR=captured;
                                    captured=null;capture=true;BigInteger target=oldTarget;capture=false;watchQ=captured;
                                    watchedFrees=0;int balance=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                    try{target=Call(&a,&b,&output,kind);return 1;}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                                    if(!caught||denied==deniedBefore||allocated-released!=balance||watchedFrees!=0)return 2;
                                    if(!a.Equals(&originalA)||!b.Equals(&originalB)||!output.Equals(&oldOutput)||!target.Equals(&oldTarget))return 3;
                                    watchA=null;watchB=null;watchR=null;watchQ=null;
                                }
                            }
                        }
                        input.Close();
                        BigInteger a=BigInteger::Parse("123456789012345678901234567890");BigInteger saved=a;BigInteger zero=new();BigInteger output=new(333);BigInteger target=new(777);
                        int before=allocated;try{throw new Exception("control");}catch{}int exceptionAllocations=allocated-before;
                        for(int kind=0;kind<5;kind++){
                            before=allocated;bool caught=false;
                            try{if(kind==0)target=a.DivRem(&zero,ref output);else if(kind==1)target=a.DivRem(&zero,ref a);else if(kind==2)target=zero.DivRem(&zero,ref zero);else if(kind==3)target=a.Divide(&zero);else target=a.Remainder(&zero);}
                            catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"BigInteger divisor is zero"))return 4;caught=true;}
                            long outValue=0;long qValue=0;
                            if(!caught||allocated-before!=exceptionAllocations||!a.Equals(&saved)||zero.Sign!=0||!output.TryToLong(ref outValue)||outValue!=333||!target.TryToLong(ref qValue)||qValue!=777)return 5;
                        }
                    }
                    if(allocated!=released)return 6;return 0;
                }
                """,[path],optimization);
        });

    private static IEnumerable<(Integer Left,Integer Right)> AliasCases()
        =>new[]{(Integer.Zero,B*B+17),((Integer)17,B*B+17),(-(Integer.Pow(10,50)+123),(Integer)17),
            (Integer.Pow(10,36)+7,Integer.Pow(10,36)+7),
            (-(Integer.Pow(10,90)+12345),Integer.Pow(10,36)+17)}.Concat(Branches());
    private const string Counters="""
        using std; using std::Text; using std::IO; using std::Memory; using std::Numerics; using std::Collections;extern void*? malloc(nuint count);extern void free(void* value);
        int allocated=0;int released=0;int attempts=0;int denied=0;int failAfter=-1;
        bool capture=false;void*? captured=null;void*? watchA=null;void*? watchB=null;void*? watchR=null;void*? watchQ=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){
                attempts++;if(failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(count);if(value!=null){allocated++;if(capture)captured=value;}return value;
            }
            public replace void Free(void* value)nothrow{
                if(value==watchA||value==watchB||value==watchR||value==watchQ)watchedFrees++;
                released++;free(value);
            }
        }
        bool TextEquals(immutable(BigInteger)* value,immutable(String)* expected){String actual=value.ToString();return actual.Equals(expected);}
        """;
    private const string ReadText="""
        String ReadText(File* input){nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;}
        """;
    private static void WriteManifest(string path,IEnumerable<(Integer Left,Integer Right)> values)
    {
        var rows=values.ToArray();using var writer=new BinaryWriter(File.Create(path));writer.Write(rows.Length);
        foreach(var row in rows){Integer quotient=Integer.DivRem(row.Left,row.Right,out Integer remainder);WriteText(writer,row.Left);WriteText(writer,row.Right);WriteText(writer,quotient);WriteText(writer,remainder);}
    }
    private static void WriteText(BinaryWriter writer,Integer value){byte[] bytes=Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));writer.Write(bytes.Length);writer.Write(bytes);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-bigint-division-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string source,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("bigint-division.gf",source));
        string input=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");File.WriteAllText(input,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(output,arguments,30000);Assert.True(result.ExitCode==0,$"BigInteger division exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
