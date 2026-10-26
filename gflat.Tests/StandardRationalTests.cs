using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class StandardRationalTests
{
    private const string Allocator="""
        using std; using std::Text; using std::Memory; using std::Numerics; using std::Collections;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalFractionArithmeticOrderPropertiesAndFormattingMatchIndependentBigIntegerOracles(int optimization)
    {
        List<(BigInteger N,BigInteger D)> inputs=[(0,1),(0,-17),(1,2),(-1,2),(2,-6),(-12,-18),(long.MinValue,-1),
            (long.MinValue,long.MinValue),(1,long.MinValue),(5,1),(-7,3)];
        var random=new Random(643751);for(int i=0;i<8;i++){
            byte[] top=new byte[12],bottom=new byte[9];random.NextBytes(top);random.NextBytes(bottom);
            BigInteger denominator=new(bottom);if(denominator.IsZero)denominator=1;inputs.Add((new BigInteger(top),denominator));
        }
        BigInteger common=BigInteger.Pow(10,120)+19;inputs.Add((common*123,-common*456));inputs.Add((-common*25,common*5));
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))for(int row=0;row<inputs.Count;row++){
            var original=inputs[row];var other=inputs[(row+3)%inputs.Count];var a=Canonical(original.N,original.D);var b=Canonical(other.N,other.D);
            WriteText(writer,original.N);WriteText(writer,original.D);WriteText(writer,other.N);WriteText(writer,other.D);
            writer.Write((byte)(Math.Sign((a.N*b.D).CompareTo(b.N*a.D))+1));writer.Write((byte)(b.N.IsZero?0:1));
            Fraction[] expected=[a,b,Canonical(a.N*b.D+b.N*a.D,a.D*b.D),Canonical(a.N*b.D-b.N*a.D,a.D*b.D),
                Canonical(a.N*b.N,a.D*b.D),Canonical(-a.N,a.D),b.N.IsZero?Canonical(0,1):Canonical(a.N*b.D,a.D*b.N)];
            foreach(var value in expected){WriteText(writer,value.N);WriteText(writer,value.D);WriteText(writer,value.Text);}
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
            String ReadText(readonly(char)* text,ref nuint at){int low=ReadByte(text,ref at);nuint count=(nuint)(low|(ReadByte(text,ref at)<<8));String result=new();result.Resize(count);Span<char> output=result.AsSpan();for(nuint i=0;i<count;i++)output[i]=(char)ReadByte(text,ref at);return move result;}
            int main(){readonly(char)* data="{{{hex}}}";nuint at=0;
                for(int row=0;row<{{{inputs.Count}}};row++){
                    {
                        String an=ReadText(data,ref at);String ad=ReadText(data,ref at);String bn=ReadText(data,ref at);String bd=ReadText(data,ref at);
                        BigInteger first=BigInteger::Parse(an.Data,an.Length);BigInteger second=BigInteger::Parse(ad.Data,ad.Length);
                        Rational left=new(move first,move second);
                        first=BigInteger::Parse(bn.Data,bn.Length);second=BigInteger::Parse(bd.Data,bd.Length);Rational right=new(move first,move second);
                        Rational savedLeft=left;Rational savedRight=right;int expectedComparison=ReadByte(data,ref at)-1;bool canDivide=ReadByte(data,ref at)!=0;
                        int comparison=left.CompareTo(&right);int sign=0;if(comparison<0)sign=-1;else if(comparison>0)sign=1;if(sign!=expectedComparison)return 1;
                        for(int operation=0;operation<7;operation++){
                            String topText=ReadText(data,ref at);String bottomText=ReadText(data,ref at);String spelling=ReadText(data,ref at);
                            Rational result=new(17l);bool caught=false;
                            try{
                                if(operation==0)result=left;else if(operation==1)result=right;else if(operation==2)result=left.Add(&right);
                                else if(operation==3)result=left.Subtract(&right);else if(operation==4)result=left.Multiply(&right);
                                else if(operation==5)result=left.Negate();else result=left.Divide(&right);
                            }catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Rational divisor is zero"))return 2;caught=true;}
                            if(operation==6&&!canDivide){Rational kept=new(17l);if(!caught||!result.Equals(&kept))return 3;}
                            else{
                                if(caught)return 4;BigInteger top=result.Numerator;BigInteger bottom=result.Denominator;
                                BigInteger expectedTop=BigInteger::Parse(topText.Data,topText.Length);BigInteger expectedBottom=BigInteger::Parse(bottomText.Data,bottomText.Length);
                                if(!top.Equals(&expectedTop)||!bottom.Equals(&expectedBottom)||bottom.Sign<=0)return 5;
                                String formatted=result.ToString();if(!formatted.Equals(&spelling))return 6;
                                Rational rebuilt=new(move expectedTop,move expectedBottom);if(!result.Equals(&rebuilt)||result.GetHashCode()!=rebuilt.GetHashCode())return 7;
                            }
                            if(!left.Equals(&savedLeft)||!right.Equals(&savedRight))return 8;
                        }
                    }
                    if(allocated!=released)return 9;
                }
                if(at!={{{hex.Length}}})return 10;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstructorsDeepCopiesValuePropertiesSelfOperandsAndHashKeysPreserveCanonicalOwners(int optimization)
        =>Run(Allocator+"""
            int main(){
                {
                    int before=allocated;Rational zero=new();if(allocated==before)return 1;
                    Rational expectedZero=new(0l,-17l);if(!zero.Equals(&expectedZero)||zero.GetHashCode()!=expectedZero.GetHashCode())return 2;
                    BigInteger one=zero.Denominator;if(one.ToLong()!=1l)return 3;
                    Rational source=new(-12l,-18l);Rational expected=new(2l,3l);Rational copy=source;
                    BigInteger top=source.Numerator;BigInteger bottom=source.Denominator;top=new(77l);bottom=new(88l);
                    if(!source.Equals(&expected))return 4;source=new(99l);if(!copy.Equals(&expected))return 5;
                    immutable(Rational)* view=&copy;Rational sum=view.Add(view);Rational difference=view.Subtract(view);Rational product=view.Multiply(view);Rational quotient=view.Divide(view);
                    Rational expectedSum=new(4l,3l);Rational expectedProduct=new(4l,9l);Rational expectedOne=new(1l);
                    if(!sum.Equals(&expectedSum)||!difference.Equals(&zero)||!product.Equals(&expectedProduct)||!quotient.Equals(&expectedOne)||!copy.Equals(&expected))return 6;
                    Rational minimum=new((-9223372036854775807l-1l),-1l);BigInteger large=minimum.Numerator;BigInteger expectedLarge=BigInteger::Parse("9223372036854775808");if(!large.Equals(&expectedLarge))return 7;
                    Rational unit=new((-9223372036854775807l-1l),(-9223372036854775807l-1l));if(!unit.Equals(&expectedOne))return 8;
                    Rational tiny=new(1l,(-9223372036854775807l-1l));String tinyText=tiny.ToString();if(!tinyText.Equals("-1/9223372036854775808"))return 9;
                    BigInteger rawTop=new(10l);BigInteger rawBottom=new(-15l);Rational borrowed=new(rawTop,rawBottom);rawTop=new(111l);rawBottom=new(222l);
                    Rational negative=new(-2l,3l);if(!borrowed.Equals(&negative))return 10;
                    HashMap<Rational,int> map=new();map.Add(copy,42);copy=new(0l);Rational equivalent=new(200l,300l);if(map[&equivalent]!=42)return 11;
                    bool caught=false;Rational kept=new(17l);
                    try{kept=new Rational(1l,0l);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Rational denominator is zero"))return 12;caught=true;}
                    Rational seventeen=new(17l);if(!caught||!kept.Equals(&seventeen))return 13;
                    caught=false;try{kept=new Rational(new BigInteger(1l),new BigInteger());}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Rational denominator is zero"))return 14;caught=true;}
                    if(!caught||!kept.Equals(&seventeen))return 15;
                }
                if(allocated!=released)return 16;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryMeasuredConstructionCopyAndArithmeticFailureAndWriterErrorCleanUpAndPreserveOperandsAndTarget(int optimization)
        =>Run(Allocator+"""
            Rational Exercise(immutable(Rational)* first,immutable(Rational)* second,int operation){
                if(operation==0){BigInteger top=BigInteger::Parse("12345678901234567890");BigInteger bottom=BigInteger::Parse("-24691357802469135780");return new Rational(move top,move bottom);}
                if(operation==1)return *first;
                if(operation==2)return first.Add(second);if(operation==3)return first.Subtract(second);
                if(operation==4)return first.Multiply(second);if(operation==5)return first.Divide(second);
                if(operation==6)return first.Negate();if(operation==7)return new Rational((-9223372036854775807l-1l),-1l);return new Rational();
            }
            struct Writer:ITextWriter{
                private int failAt;public int Calls;public Writer(int position){failAt=position;Calls=0;}
                public void Write(readonly(char)* text,nuint count){Calls++;if(Calls==failAt)throw new Exception("writer",73);}
            }
            int main(){
                {
                    Rational first=new(7l,15l);Rational second=new(-11l,21l);Rational savedFirst=first;Rational savedSecond=second;
                    Rational target=new(17l,19l);Rational savedTarget=target;
                    for(int operation=0;operation<9;operation++){
                        int before=attempts;int live=allocated-released;int points=0;
                        {Rational probe=Exercise(&first,&second,operation);points=attempts-before;}
                        if(points==0||allocated-released!=live)return 1;
                        for(int point=0;point<points;point++){
                            int deniedBefore=denied;live=allocated-released;failAfter=point;bool caught=false;
                            try{target=Exercise(&first,&second,operation);}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                            if(!caught||denied!=deniedBefore+1||allocated-released!=live)return 2;
                            if(!target.Equals(&savedTarget)||!first.Equals(&savedFirst)||!second.Equals(&savedSecond))return 3;
                        }
                    }
                    for(int failure=1;failure<=3;failure++){
                        Writer writer=new(failure);int live=allocated-released;bool caught=false;
                        try{first.Format(&writer);}catch(ref Exception error){if(error.GetCode()!=73)return 4;caught=true;}
                        if(!caught||writer.Calls!=failure||allocated-released!=live||!first.Equals(&savedFirst))return 5;
                    }
                    Rational zero=new();bool caught=false;int live=allocated-released;
                    try{target=first.Divide(&zero);}catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Rational divisor is zero"))return 6;caught=true;}
                    if(!caught||allocated-released!=live||!target.Equals(&savedTarget)||!first.Equals(&savedFirst))return 7;
                }
                if(allocated!=released)return 8;return 0;
            }
            """,optimization);

    private readonly record struct Fraction(BigInteger N,BigInteger D)
    {
        public string Text=>D.IsOne?N.ToString(CultureInfo.InvariantCulture):N.ToString(CultureInfo.InvariantCulture)+"/"+D.ToString(CultureInfo.InvariantCulture);
    }
    private static Fraction Canonical(BigInteger numerator,BigInteger denominator)
    {
        if(denominator.Sign<0){numerator=-numerator;denominator=-denominator;}
        BigInteger gcd=BigInteger.GreatestCommonDivisor(numerator,denominator);return new(numerator/gcd,denominator/gcd);
    }
    private static void WriteText(BinaryWriter writer,BigInteger value)=>WriteText(writer,value.ToString(CultureInfo.InvariantCulture));
    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.ASCII.GetBytes(text);writer.Write((ushort)bytes.Length);writer.Write(bytes);}
    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("rational.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"Rational exit {result.ExitCode}: {result.StandardError}");
    }
}
