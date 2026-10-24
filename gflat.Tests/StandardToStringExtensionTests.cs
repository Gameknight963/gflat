using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class StandardToStringExtensionTests
{
    private const string Allocator="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;int denied=0;int failAfter=-1;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(failAfter==0){failAfter=-1;denied++;return null;}if(failAfter>0)failAfter--;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;
    private const string Payload="""
        int owners=0;int formats=0;bool failFormat=false;
        struct Payload:IFormattable{
            private String bytes;
            public Payload(){bytes=new();bytes.Resize(257,'x');Span<char> view=bytes.AsSpan();view[1]='\0';view[2]=(char)(byte)255;owners++;}
            public readonly(char)* Data{immutable get=>bytes.Data;}
            public immutable void Format(ITextWriter* output){formats++;output.Write(bytes.Data,1);output.Write(bytes.Data+1,16);if(failFormat)throw new Exception("formatter",71);output.Write(bytes.Data+17,240);}
            ~Payload(){owners--;}
        }
        bool Matches(ref immutable(String) text){if(text.Length!=257||text.Data[0]!='x'||text.Data[1]!='\0'||(byte)text.Data[2]!=(byte)255)return false;for(nuint i=3;i<257;i++)if(text.Data[i]!='x')return false;return text.Data[257]=='\0';}
        String FromBorrow(ref immutable(Payload) value)=>value.ToString();
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveAndOptionalWideAndFloatingOverloadsReturnExpectedCountedOwningStrings(int optimization)
    {
        long[] inputs=[long.MinValue,int.MinValue,-32769,-129,-1,0,1,127,255,32767,65535,int.MaxValue,long.MaxValue];
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true))foreach(long value in inputs){
            writer.Write(value);ulong bits=unchecked((ulong)value);
            foreach(object projected in new object[]{unchecked((sbyte)value),unchecked((short)value),unchecked((int)value),value,value,
                unchecked((byte)bits),unchecked((ushort)bits),unchecked((uint)bits),bits,bits})
                WriteText(writer,((IFormattable)projected).ToString(null,CultureInfo.InvariantCulture));
        }
        float[] singles=[0f,-123.5f,0.125f,0.1f,1.5f,65536f];double[] doubles=[0d,-123.5d,0.125d,0.1d,1.5d,65536d];
        using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true)){
            foreach(float value in singles)WriteText(writer,value.ToString("G9",CultureInfo.InvariantCulture));
            foreach(double value in doubles)WriteText(writer,value.ToString("G17",CultureInfo.InvariantCulture));
        }
        string hex=Convert.ToHexString(bytes.ToArray());
        Run(Allocator+$$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint at)nothrow{int value=(Digit(text[at])<<4)|Digit(text[at+1]);at+=2;return value;}
            long ReadLong(readonly(char)* text,ref nuint at)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(text,ref at)<<shift);return (long)value;}
            bool Check(ref immutable(String) actual,readonly(char)* data,ref nuint at){nuint count=(nuint)ReadByte(data,ref at);if(actual.Length!=count)return false;for(nuint i=0;i<count;i++)if((byte)actual.Data[i]!=(byte)ReadByte(data,ref at))return false;return actual.Data[count]=='\0';}
            int main(){if(sizeof(nint)!=8)return 1;readonly(char)* data="{{{hex}}}";nuint at=0;
                {
                    for(int row=0;row<{{{inputs.Length}}};row++){
                        long value=ReadLong(data,ref at);sbyte a=(sbyte)value;short b=(short)value;int c=(int)value;nint d=(nint)value;
                        byte e=(byte)value;ushort f=(ushort)value;uint g=(uint)value;ulong h=(ulong)value;nuint i=(nuint)value;String text=new();
                        text=a.ToString();if(!Check(ref text,data,ref at))return 2;text=b.ToString();if(!Check(ref text,data,ref at))return 3;
                        text=c.ToString();if(!Check(ref text,data,ref at))return 4;text=value.ToString();if(!Check(ref text,data,ref at))return 5;
                        text=d.ToString();if(!Check(ref text,data,ref at))return 6;text=e.ToString();if(!Check(ref text,data,ref at))return 7;
                        text=f.ToString();if(!Check(ref text,data,ref at))return 8;text=g.ToString();if(!Check(ref text,data,ref at))return 9;
                        text=h.ToString();if(!Check(ref text,data,ref at))return 10;text=i.ToString();if(!Check(ref text,data,ref at))return 11;
                    }
                    float[6] singles=[0.0f,-123.5f,0.125f,0.1f,1.5f,65536.0f];double[6] doubles=[0.0,-123.5,0.125,0.1,1.5,65536.0];
                    for(nuint i=0;i<6;i++){float value=singles[i];String text=value.ToString();if(!Check(ref text,data,ref at))return 12;}
                    for(nuint i=0;i<6;i++){double value=doubles[i];String text=value.ToString();if(!Check(ref text,data,ref at))return 13;}
                    bool yes=true;bool no=false;String truth=yes.ToString();String falsity=no.ToString();if(!truth.Equals("true")||!falsity.Equals("false"))return 14;
                    char zero='\0';char high=(char)(byte)255;String nul=zero.ToString();String binary=high.ToString();
                    if(nul.Length!=1||nul.Data[0]!='\0'||nul.Data[1]!='\0'||binary.Length!=1||(byte)binary.Data[0]!=(byte)255||binary.Data[1]!='\0')return 15;
                    extralong maximum=((extralong)1<<126)-1;maximum=maximum*2+1;extralong minimum=-maximum-1;
                    String positive=maximum.ToString();String negative=minimum.ToString();
                    if(!positive.Equals("170141183460469231731687303715884105727")||!negative.Equals("-170141183460469231731687303715884105728"))return 16;
                }
                if(at!={{{hex.Length}}}||allocated!=released)return 17;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedNoncopyableQualifiedAndRefParameterReceiversProduceIndependentResultsAndInstancesWin(int optimization)
        =>Run(Allocator+Payload+"""
            int memberCalls=0;int memberFormats=0;
            struct Existing:IFormattable{
                public int Marker;public Existing(){Marker=17;}
                public immutable String ToString(){memberCalls++;return s"member";}
                public immutable void Format(ITextWriter* output){memberFormats++;output.Write("formatter",9);}
            }
            int main(){
                {
                    Payload source=new();readonly(Payload) readOnly=new();immutable(Payload) frozen=new();readonly(char)* storage=source.Data;
                    int before=formats;
                    String first=source.ToString();String second=readOnly.ToString();String third=frozen.ToString();String borrowed=FromBorrow(ref source);
                    if(formats!=before+4||owners!=3||!Matches(ref first)||!Matches(ref second)||!Matches(ref third)||!Matches(ref borrowed))return 1;
                    if(first.Data==source.Data||first.Data==second.Data||first.Data==third.Data||source.Data!=storage)return 2;
                    first.Clear();first.Append("changed",7);if(!Matches(ref borrowed)||source.Data[0]!='x'||source.Data[1]!='\0'||(byte)source.Data[2]!=(byte)255)return 3;
                    Existing existing=new();String chosen=existing.ToString();if(!chosen.Equals("member")||memberCalls!=1||memberFormats!=0||existing.Marker!=17)return 4;
                    String original=String::From("a\0b",3);String copied=original.ToString();if(copied.Data==original.Data||copied.Length!=3||copied.Data[1]!='\0')return 5;
                    copied.Clear();copied.Append("z",1);if(original.Length!=3||original.Data[0]!='a'||original.Data[1]!='\0'||original.Data[2]!='b')return 6;
                }
                if(owners!=0||allocated!=released)return 7;return 0;
            }
            """,optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FormatterAndEveryMeasuredOneShotAllocationFailureReleasePartialResultsAndPreserveReceiverAndDestination(int optimization)
        =>Run(Allocator+Payload+"""
            int main(){
                {
                    Payload source=new();String destination=s"keep";readonly(char)* oldData=destination.Data;nuint oldCapacity=destination.Capacity;readonly(char)* original=source.Data;
                    int before=attempts;int points=0;int live=allocated-released;
                    {String probe=source.ToString();if(!Matches(ref probe))return 1;points=attempts-before;}
                    if(points==0||allocated-released!=live||owners!=1)return 2;
                    for(int point=0;point<points;point++){
                        live=allocated-released;int deniedBefore=denied;failAfter=point;bool caught=false;
                        try{destination=source.ToString();}catch(ref BadAllocException error){caught=true;}failAfter=-1;
                        if(!caught||denied!=deniedBefore+1||allocated-released!=live||owners!=1)return 3;
                        if(destination.Data!=oldData||destination.Capacity!=oldCapacity||!destination.Equals("keep")||source.Data!=original||source.Data[0]!='x'||source.Data[1]!='\0'||(byte)source.Data[2]!=(byte)255)return 4;
                    }
                    failFormat=true;live=allocated-released;bool caught=false;
                    try{destination=source.ToString();}catch(ref Exception error){if(error.GetCode()!=71)return 5;caught=true;}failFormat=false;
                    if(!caught||allocated-released!=live||owners!=1||destination.Data!=oldData||destination.Capacity!=oldCapacity||!destination.Equals("keep")||source.Data!=original)return 6;
                    destination=source.ToString();if(!Matches(ref destination)||owners!=1)return 7;
                }
                if(owners!=0||allocated!=released)return 8;return 0;
            }
            """,optimization);

    [Fact]
    public void CoreConversionsCanBeSelectedWithoutLibcOrWideFormattingModules()
    {
        string[] names = ["Text.gf", "Exception.gf", "BadAllocException.gf", "IStringConvertable.gf",
            "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "ByteSearch.gf", "String.gf",
            "Formatting.gf", "FormattableString.gf", "PrimitiveString.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "core", name))))
            .Append(new SourceFile("core-conversions.gf", """
                namespace Memory {
                    public void*? Allocate(nuint size) { return null; }
                    public void Free(void* storage) nothrow {}
                }
                struct Label : IFormattable {
                    public immutable void Format(ITextWriter* output) { output.Write("label", 5); }
                }
                std::String ConvertLabel(ref immutable(Label) value) => std::ToString(ref value);
                std::String ConvertInteger(int value) => std::ToString(value);
                """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        Assert.DoesNotMatch(@"(?m)^declare\s+[^\r\n]*@(?:malloc|free|snprintf|printf|fwrite|strlen)\(", ir);
    }

    private static void WriteText(BinaryWriter writer,string text){byte[] bytes=Encoding.ASCII.GetBytes(text);writer.Write((byte)bytes.Length);writer.Write(bytes);}
    private static IEnumerable<SourceFile> Sources(string program)
        =>Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("to-string-extensions.gf",program));
    private static void Run(string program,int optimization)
    {
        var result=CompilerTestHelper.Run(Sources(program),optimization);Assert.True(result.ExitCode==0,$"ToString extensions exit {result.ExitCode}: {result.StandardError}");
    }
}
