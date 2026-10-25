using System.Numerics;

namespace gflat.Tests;

public sealed class StandardCheckedCommonMultipleTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "Numerics/Math.gf", "CString.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("checked-lcm.gf",code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CheckedUnsignedWidthsMatchBigIntegerAndPreserveAssignmentsOnOverflow(int optimization)
    {
        List<(ulong A,ulong B)> pairs =
        [
            (0,0),(0,ulong.MaxValue),(ulong.MaxValue,0),(1,ulong.MaxValue),
            (ulong.MaxValue,ulong.MaxValue),(ulong.MaxValue,2),(ulong.MaxValue-1,ulong.MaxValue),
            (6,8),(12,18),(1ul<<63,1ul<<62),(1ul<<63,2),
            (uint.MaxValue,uint.MaxValue-1),(uint.MaxValue,2)
        ];
        var random = new Random(467193);byte[] bytes = new byte[16];
        for(int sample=0;sample<12;sample++)
        {
            random.NextBytes(bytes);
            pairs.Add((BitConverter.ToUInt64(bytes,0),BitConverter.ToUInt64(bytes,8)));
            ulong shared=(ulong)random.Next(1,65536);
            pairs.Add((shared*(ulong)random.Next(1,65536),shared*(ulong)random.Next(1,65536)));
        }
        ulong nativeMaximum=IntPtr.Size==8?ulong.MaxValue:uint.MaxValue;
        using var packet=new MemoryStream();
        using(var writer=new BinaryWriter(packet,System.Text.Encoding.UTF8,leaveOpen:true))
        foreach(var pair in pairs)
        {
            writer.Write(pair.A);writer.Write(pair.B);
            WriteExpected(writer,(uint)pair.A,(uint)pair.B,uint.MaxValue);
            WriteExpected(writer,pair.A,pair.B,ulong.MaxValue);
            WriteExpected(writer,pair.A&nativeMaximum,pair.B&nativeMaximum,nativeMaximum);
        }
        string hex=Convert.ToHexString(packet.ToArray());
        string checks=string.Join(Environment.NewLine,new[] { "uint", "ulong", "nuint" }.Select(type=>$$$"""
            bool Check{{{type}}}({{{type}}} a,{{{type}}} b,{{{type}}} expected,bool fits) {
                {{{type}}} savedA=a;{{{type}}} savedB=b;{{{type}}} result=({{{type}}})17;
                bool caught=false;int before=attempts;deny=fits;
                try{result=Math::LeastCommonMultiple(a,b);}
                catch(ref Exception error) {
                    if(fits||!CString::Equals(error.GetMessage(),"Least common multiple overflow")){deny=false;return false;}
                    caught=true;
                }
                deny=false;
                if(a!=savedA||b!=savedB)return false;
                if(fits)return !caught&&result==expected&&attempts==before;
                return caught&&result==({{{type}}})17;
            }
            """));
        string program=Memory+checks+$$$"""
            nuint ReadByte(readonly(char)* text,ref nuint offset) {
                nuint value=0;
                for(int i=0;i<2;i++) {
                    char digit=text[offset];offset++;
                    nuint part=(nuint)((int)digit-(int)'0');
                    if(digit>='A')part=(nuint)((int)digit-(int)'A'+10);
                    value=value*16+part;
                }
                return value;
            }
            ulong ReadWord(readonly(char)* text,ref nuint offset) {
                ulong value=0ul;
                for(int i=0;i<8;i++)value=value|((ulong)ReadByte(text,ref offset)<<(i*8));
                return value;
            }
            int main() {
                if((nuint)sizeof(nuint)!=(nuint){{{IntPtr.Size}}})return 1;
                readonly(char)* packet="{{{hex}}}";nuint offset=0;
                for(int row=0;row<{{{pairs.Count}}};row++) {
                    ulong a=ReadWord(packet,ref offset);ulong b=ReadWord(packet,ref offset);
                    uint narrow=(uint)ReadWord(packet,ref offset);bool fits=ReadByte(packet,ref offset)!=0;
                    if(!Checkuint((uint)a,(uint)b,narrow,fits))return 2;
                    ulong wide=ReadWord(packet,ref offset);fits=ReadByte(packet,ref offset)!=0;
                    if(!Checkulong(a,b,wide,fits))return 3;
                    nuint native=(nuint)ReadWord(packet,ref offset);fits=ReadByte(packet,ref offset)!=0;
                    if(!Checknuint((nuint)a,(nuint)b,native,fits))return 4;
                }
                if(offset!={{{hex.Length}}}||allocated!=released)return 5;
                return 0;
            }
            """;
        var result=CompilerTestHelper.Run(Sources(program),optimization);
        Assert.True(result.ExitCode==0,$"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static void WriteExpected(BinaryWriter writer,ulong a,ulong b,ulong maximum)
    {
        BigInteger gcd=BigInteger.GreatestCommonDivisor(a,b);
        BigInteger exact=a==0||b==0?BigInteger.Zero:(BigInteger)a/gcd*b;
        bool fits=exact<=maximum;
        writer.Write(fits?(ulong)exact:0ul);writer.Write(fits?(byte)1:(byte)0);
    }

    private const string Memory="""
        using std;
        bool deny=false;int attempts=0;int allocated=0;int released=0;
        extern void*? malloc(nuint count);extern void free(void* value);
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;if(deny)return null;
                void*? value=malloc(count);if(value!=null)allocated++;return value;
            }
            public replace void Free(void* value)nothrow{released++;free(value);}
        }
        """;
}
