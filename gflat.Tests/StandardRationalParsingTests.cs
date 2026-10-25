using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class StandardRationalParsingTests
{
    private const string Support="""
        using std; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;
        bool capture=false;void*? captured=null;nuint capturedBytes=0;void*? watched=null;void*? retired=null;int watchedFrees=0;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? result=malloc(count);if(result!=null){allocated++;if(capture&&captured==null){captured=result;capturedBytes=count;}}return result;}
            public replace void Free(void* storage)nothrow{
                released++;
                if(storage==watched){watchedFrees++;retired=storage;char* bytes=(char*)storage;for(nuint i=0;i<4;i++)if(bytes[i]!='\0')bytes[i]='!';}
                else free(storage);
            }
        }
        int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
        int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
        String ReadText(readonly(char)* text,ref nuint at){int low=ReadByte(text,ref at);nuint count=(nuint)(low|(ReadByte(text,ref at)<<8));String result=new();result.Resize(count);Span<char> output=result.AsSpan();for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(text,ref at);return move result;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedCStringAndThrowingParsersMatchIndependentCanonicalFractionOracles(int optimization)
    {
        List<string> inputs=["0","-000","+000","1","-1","+00123","-0006/+0008","-12/-18","1/-9223372036854775808",
            "-9223372036854775808/-1","0/-999","0001/0001",new string('0',90)+"1/-"+new string('0',80)+"2"];
        BigInteger factor=BigInteger.Pow(10,450)+19;inputs.Add(Text(factor*84)+"/"+Text(-factor*126));
        var random=new Random(731957);for(int i=0;i<16;i++){
            byte[] top=new byte[12],bottom=new byte[10];random.NextBytes(top);random.NextBytes(bottom);BigInteger denominator=new(bottom);if(denominator.IsZero)denominator=1;
            inputs.Add(Text(new BigInteger(top))+"/"+Text(denominator));
        }
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(string input in inputs){
            var value=ReadValid(input);WriteText(writer,input);WriteText(writer,value.N);WriteText(writer,value.D);WriteText(writer,value.Text);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{inputs.Count}}};row++){
                    {
                        String input=ReadText(packet,ref at);String untouched=input;String topText=ReadText(packet,ref at);String bottomText=ReadText(packet,ref at);String spelling=ReadText(packet,ref at);
                        BigInteger top=BigInteger::Parse(topText.Data,topText.Length);BigInteger bottom=BigInteger::Parse(bottomText.Data,bottomText.Length);Rational expected=new(move top,move bottom);
                        Rational counted=new(17l,19l);Rational cstring=new(23l,29l);
                        if(!Rational::TryParse(input.Data,input.Length,ref counted)||!Rational::TryParse(input.Data,ref cstring)||!counted.Equals(&expected)||!cstring.Equals(&expected))return 1;
                        Rational parsed=Rational::Parse(input.Data,input.Length);Rational parsedCString=Rational::Parse(input.Data);
                        if(!parsed.Equals(&expected)||!parsedCString.Equals(&expected)||counted.GetHashCode()!=expected.GetHashCode())return 2;
                        String actual=parsed.ToString();if(!actual.Equals(&spelling)||!input.Equals(&untouched))return 3;
                        char[2048] nonterminated;if(input.Length>=2048)return 4;for(nuint i=0;i<input.Length;i++)nonterminated[i]=input.Data[i];nonterminated[input.Length]='X';
                        if(!Rational::TryParse(nonterminated,input.Length,ref counted)||!counted.Equals(&expected))return 5;
                    }
                    if(allocated!=released)return 6;
                }
                if(at!={{{hex.Length}}})return 7;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedCountedBytesResetOnlyOnFalseAndOwnedLimbInputStaysReadableUntilCommit(int optimization)
    {
        string[] invalid=["","+","-","/","1/","/2","1//2","1/2/3","1.0","1/2.0"," 1","1 ","1 /2","1/ 2","1_0","0x10","++1","1/-","1/+","1/0","0/-000","1/+000",
            "\u0661/2","1/\u00ff","1/2\0suffix","+000\0/0","1\0/2","\0"];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(string input in invalid){
            string prefix=input.Split('\0')[0];bool valid=input.Contains('\0')&&(prefix is "1/2" or "+000" or "1");
            WriteText(writer,input);writer.Write((byte)(valid?1:0));var value=valid?ReadValid(prefix):Canonical(0,1);WriteText(writer,value.N);WriteText(writer,value.D);
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Support+$$$"""
            int main(){readonly(char)* packet="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{invalid.Length}}};row++){
                    {
                        String input=ReadText(packet,ref at);String untouched=input;bool cstringValid=ReadByte(packet,ref at)!=0;String topText=ReadText(packet,ref at);String bottomText=ReadText(packet,ref at);
                        BigInteger top=BigInteger::Parse(topText.Data,topText.Length);BigInteger bottom=BigInteger::Parse(bottomText.Data,bottomText.Length);Rational cstringExpected=new(move top,move bottom);
                        Rational zero=new();Rational value=new(17l,19l);Rational saved=value;
                        if(Rational::TryParse(input.Data,input.Length,ref value)||!value.Equals(&zero))return 1;
                        value=saved;if(Rational::TryParse(input.Data,ref value)!=cstringValid||!value.Equals(&cstringExpected)||!input.Equals(&untouched))return 2;
                        value=saved;bool caught=false;try{value=Rational::Parse(input.Data,input.Length);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid rational"))return 3;caught=true;}
                        if(!caught||!value.Equals(&saved))return 4;
                        value=saved;caught=false;try{value=Rational::Parse(input.Data);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid rational"))return 5;caught=true;}
                        if(caught==cstringValid)return 6;if(cstringValid){if(!value.Equals(&cstringExpected))return 7;}else if(!value.Equals(&saved))return 8;
                    }
                    if(allocated!=released)return 9;
                }
                {
                    readonly(char)*? absent=null;Rational target=new(17l,19l);Rational zero=new();if(Rational::TryParse(absent!,0,ref target)||!target.Equals(&zero))return 10;
                }
                // Capture public allocator output, not Rational's private field layout.
                // Four ASCII bytes are a valid nonzero base1e9 limb in either byte order.
                for(int kind=0;kind<4;kind++){
                    {
                        captured=null;capture=true;Rational owner=new(1l);capture=false;if(captured==null||capturedBytes<4)return 11;
                        char* text=(char*)captured!;text[0]='1';text[1]='2';text[2]='/';text[3]='3';
                        if(kind==1){text[1]='/';text[2]='2';text[3]='\0';}if(kind==2){text[1]='/';text[2]='x';text[3]='3';}
                        Rational saved=owner;watched=captured;watchedFrees=0;retired=null;bool success=false;
                        if(kind==3){int live=allocated-released;failAfter=0;bool caught=false;try{Rational::TryParse(text,4,ref owner);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                            if(!caught||watchedFrees!=0||!owner.Equals(&saved)||allocated-released!=live)return 12;}
                        else{
                            if(kind==1)success=Rational::TryParse(text,ref owner);else success=Rational::TryParse(text,4,ref owner);
                            if(success!=(kind!=2)||watchedFrees!=1)return 13;
                        }
                        watched=null;if(retired!=null){free(retired!);retired=null;}
                        if(kind==0){Rational expected=new(4l);if(!owner.Equals(&expected))return 14;}
                        if(kind==1){Rational expected=new(1l,2l);if(!owner.Equals(&expected))return 15;}
                        if(kind==2){Rational expected=new();if(!owner.Equals(&expected))return 16;}
                    }
                    if(allocated!=released)return 17;
                }
                if(at!={{{hex.Length}}})return 18;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredValidAndInvalidParseAllocationFailurePreservesOldResultAndInputAndReleasesScratch(int optimization)
        =>Run(Support+"""
            int main(){
                readonly(char)*[6] texts=["12345678901234567890/-24691357802469135780","000","bad","1/0","","12345678901234567890/invalid"];
                bool[6] valid=[true,true,false,false,false,false];
                {
                    Rational target=new(17l,19l);Rational saved=target;
                    for(nuint row=0;row<6;row++){
                        String text=String::From(texts[row]);String untouched=text;readonly(char)* original=text.Data;nuint capacity=text.Capacity;
                        Rational expected=new();if(row==0)expected=new(-1l,2l);
                        for(int kind=0;kind<2;kind++){
                            int before=0;int points=0;int live=allocated-released;
                            {Rational probe=new(73l);before=attempts;bool success=false;if(kind==0)success=Rational::TryParse(text.Data,text.Length,ref probe);else success=Rational::TryParse(text.Data,ref probe);points=attempts-before;if(success!=valid[row]||!probe.Equals(&expected))return 1;}
                            if(points==0||allocated-released!=live)return 2;
                            for(int point=0;point<points;point++){
                                live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                                try{if(kind==0)Rational::TryParse(text.Data,text.Length,ref target);else Rational::TryParse(text.Data,ref target);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                                if(!caught||denied!=deniedBefore+1||allocated-released!=live||!target.Equals(&saved))return 3;
                                if(text.Data!=original||text.Capacity!=capacity||!text.Equals(&untouched))return 4;
                            }
                            bool success=false;if(kind==0)success=Rational::TryParse(text.Data,text.Length,ref target);else success=Rational::TryParse(text.Data,ref target);
                            if(success!=valid[row]||!target.Equals(&expected)||!text.Equals(&untouched))return 5;target=saved;
                        }
                    }
                }
                if(allocated!=released)return 6;return 0;
            }
            """,optimization);

    private readonly record struct Fraction(BigInteger N,BigInteger D){public string Text=>D.IsOne?N.ToString(CultureInfo.InvariantCulture):N.ToString(CultureInfo.InvariantCulture)+"/"+D.ToString(CultureInfo.InvariantCulture);}
    private static Fraction Canonical(BigInteger numerator,BigInteger denominator){if(denominator.Sign<0){numerator=-numerator;denominator=-denominator;}BigInteger gcd=BigInteger.GreatestCommonDivisor(numerator,denominator);return new(numerator/gcd,denominator/gcd);}
    private static Fraction ReadValid(string text){string[] parts=text.Split('/');return Canonical(BigInteger.Parse(parts[0],CultureInfo.InvariantCulture),parts.Length==1?BigInteger.One:BigInteger.Parse(parts[1],CultureInfo.InvariantCulture));}
    private static string Text(BigInteger value)=>value.ToString(CultureInfo.InvariantCulture);
    private static void WriteText(BinaryWriter writer,BigInteger value)=>WriteText(writer,Text(value));
    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.UTF8.GetBytes(text);writer.Write((ushort)bytes.Length);writer.Write(bytes);}
    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("rational-parsing.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Rational parsing exit {result.ExitCode}: {result.StandardError}");
    }
}
