using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace gflat.Tests.Std;

public sealed class UuidByteTests
{
    private const string Counters="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? p=malloc(count);if(p!=null)allocated++;return p;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GuidBigEndianAndBinaryPrimitiveOraclesMatchCanonicalTextAllBytesAndEveryCapacity(int optimization)
    {
        var rows=new List<byte[]> {new byte[16],Enumerable.Repeat((byte)255,16).ToArray(),Convert.FromHexString("00112233445566778899AABBCCDDEEFF")};
        for(int block=0;block<16;block++)rows.Add(Enumerable.Range(block*16,16).Select(value=>(byte)value).ToArray());
        var random=new Random(927531);for(int sample=0;sample<32;sample++){byte[] bytes=new byte[16];random.NextBytes(bytes);rows.Add(bytes);}
        using var data=new MemoryStream();
        using(var writer=new BinaryWriter(data,Encoding.UTF8,leaveOpen:true))foreach(byte[] bytes in rows){
            var value=new Guid(bytes,bigEndian:true);byte[] expected=new byte[16];
            Assert.True(value.TryWriteBytes(expected,bigEndian:true,out int written));Assert.Equal(16,written);Assert.Equal(bytes,expected);
            writer.Write(expected);writer.Write(BinaryPrimitives.ReadUInt64BigEndian(expected.AsSpan(0,8)));writer.Write(BinaryPrimitives.ReadUInt64BigEndian(expected.AsSpan(8,8)));
            writer.Write(Encoding.ASCII.GetBytes(value.ToString("D")));
        }
        string hex=Convert.ToHexString(data.ToArray());
        Run(Counters+$$"""
            int Digit(char value)nothrow{if(value<='9')return value-'0';return value-'A'+10;}
            int ReadByte(readonly(char)* text,ref nuint position)nothrow{int value=(Digit(text[position])<<4)|Digit(text[position+1]);position+=2;return value;}
            ulong ReadULong(readonly(char)* text,ref nuint position)nothrow{ulong value=0ul;for(int shift=0;shift<64;shift+=8)value=value|((ulong)ReadByte(text,ref position)<<shift);return value;}
            int main(){
                readonly(char)* data="{{hex}}";nuint position=0;byte[16] input;char[36] expectedText;
                int requests=attempts;deny=true;
                for(int row=0;row<{{rows.Count}};row++){
                    for(nuint i=0;i<16;i++)input[i]=(byte)ReadByte(data,ref position);byte[16] saved=input;
                    ulong high=ReadULong(data,ref position);ulong low=ReadULong(data,ref position);
                    for(nuint i=0;i<36;i++)expectedText[i]=(char)ReadByte(data,ref position);
                    Uuid value=new(1ul,2ul);if(!Uuid::TryFromBytes(input,16,&value)||value.High!=high||value.Low!=low)return 1;
                    Uuid direct=Uuid::FromBytes(input,16);Uuid parsed=Uuid::Parse(expectedText,36);immutable(Uuid)* view=&value;
                    if(!direct.Equals(view)||!parsed.Equals(view)||parsed.GetHashCode()!=view.GetHashCode()||view.IsNil!=(high==0ul&&low==0ul))return 2;
                    char[36] actualText;nuint written=99;
                    if(!view.TryFormat(actualText,36,ref written)||written!=36)return 3;
                    for(nuint i=0;i<36;i++)if(actualText[i]!=expectedText[i])return 4;
                    for(nuint capacity=0;capacity<=17;capacity++){
                        byte[20] output;for(nuint i=0;i<20;i++)output[i]=(byte)90;written=99;
                        bool success=view.TryWriteBytes(&output[1],capacity,ref written);
                        if(success!=(capacity>=16))return 5;
                        if(!success){if(written!=0)return 6;for(nuint i=0;i<20;i++)if(output[i]!=(byte)90)return 7;}
                        else{
                            if(written!=16||output[0]!=(byte)90)return 8;
                            for(nuint i=0;i<16;i++)if(output[i+1]!=input[i])return 9;
                            for(nuint i=17;i<20;i++)if(output[i]!=(byte)90)return 10;
                            Uuid roundtrip=Uuid::FromBytes(&output[1],16);if(!roundtrip.Equals(view))return 11;
                        }
                    }
                    for(nuint i=0;i<16;i++)if(input[i]!=saved[i])return 12;
                }
                if(position!={{hex.Length}}||attempts!=requests)return 13;deny=false;
                if(allocated!=released)return 14;return 0;
            }
            """,optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrongCountsResetNilBeforeReadingAndBackedOverlapStagesAllBytesWithoutAllocation(int optimization)
        =>Run(Counters+"""
            int main(){
                if(sizeof(Uuid)!=16)return 1;
                Uuid value=new(1ul,2ul);
                for(nuint count=0;count<=20;count++){
                    if(count==16)continue;value=new(1ul,2ul);int requests=attempts;deny=true;
                    if(Uuid::TryFromBytes(((nuint)1).reinterpret<readonly(byte)*>(),count,&value)||!value.IsNil||attempts!=requests)return 2;
                    deny=false;value=new(1ul,2ul);bool caught=false;
                    try{value=Uuid::FromBytes(((nuint)1).reinterpret<readonly(byte)*>(),count);}catch(ref Exception error){caught=true;}
                    if(!caught||value.High!=1ul||value.Low!=2ul)return 3;
                }
                value=new(1ul,2ul);int requests=attempts;deny=true;
                if(Uuid::TryFromBytes(((nuint)1).reinterpret<readonly(byte)*>(),(nuint)0-1,&value)||!value.IsNil)return 4;
                for(nuint capacity=0;capacity<16;capacity++){
                    nuint written=99;if(value.TryWriteBytes(((nuint)1).reinterpret<byte*>(),capacity,ref written)||written!=0)return 5;
                }
                for(nuint offset=0;offset<=32;offset+=4)for(nuint resultIndex=0;resultIndex<3;resultIndex++)for(int valid=0;valid<2;valid++){
                    Uuid[4] aligned;byte* bytes=(&aligned[0]).reinterpret<byte*>();for(nuint i=0;i<64;i++)bytes[i]=(byte)90;
                    for(nuint i=0;i<16;i++)bytes[offset+i]=(byte)(i*17);
                    byte[64] before;for(nuint i=0;i<64;i++)before[i]=bytes[i];
                    nuint count=15;if(valid!=0)count=16;
                    bool success=Uuid::TryFromBytes(bytes+offset,count,&aligned[resultIndex]);
                    if(success!=(valid!=0))return 6;
                    if(valid==0){if(!aligned[resultIndex].IsNil)return 7;}
                    else if(aligned[resultIndex].High!=0x0011223344556677ul||aligned[resultIndex].Low!=0x8899aabbccddeefful)return 8;
                    for(nuint i=0;i<64;i++)if((i<resultIndex*16||i>=resultIndex*16+16)&&bytes[i]!=before[i])return 9;
                }
                for(nuint offset=0;offset<=32;offset+=4)for(nuint capacity=0;capacity<=17;capacity++){
                    Uuid[4] aligned;byte* bytes=(&aligned[0]).reinterpret<byte*>();for(nuint i=0;i<64;i++)bytes[i]=(byte)90;
                    aligned[1]=new Uuid(0x0011223344556677ul,0x8899aabbccddeefful);
                    byte[64] before;for(nuint i=0;i<64;i++)before[i]=bytes[i];nuint written=99;
                    immutable(Uuid)* view=&aligned[1];bool success=view.TryWriteBytes(bytes+offset,capacity,ref written);
                    if(success!=(capacity>=16))return 10;
                    if(!success){if(written!=0||view.High!=0x0011223344556677ul||view.Low!=0x8899aabbccddeefful)return 11;
                        for(nuint i=0;i<64;i++)if(bytes[i]!=before[i])return 12;}
                    else{if(written!=16)return 13;for(nuint i=0;i<64;i++){
                        if(i>=offset&&i<offset+16){if(bytes[i]!=(byte)((i-offset)*17))return 14;}
                        else if(bytes[i]!=before[i])return 15;}}
                }
                if(attempts!=requests)return 16;deny=false;if(allocated!=released)return 17;return 0;
            }
            """,optimization);

    [Fact]
    public void CoreByteBridgeUsesNoStringAndNothrowObserverHasNoExceptionBoundary()
    {
        string[] names=["Uuid.gf","ITextWriter.gf", "IFormattable.gf","IHashable.gf","IEquatable.gf","IComparable.gf","Hashing/primitive_hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("uuid-byte-contract.gf","""
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
                bool Convert(readonly(byte)* input,nuint count,byte* output,nuint capacity,ref nuint written)nothrow{
                    std::Uuid value=new();return std::Uuid::TryFromBytes(input,count,&value)&&value.TryWriteBytes(output,capacity,ref written);
                }
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true});
        string body=Regex.Match(ir,@"(?ms)^define [^\r\n]*@gflat\$Convert\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body);Assert.DoesNotContain("invoke ",body);Assert.DoesNotContain("__gflat_eh_",body);Assert.DoesNotContain("$String",ir);
    }

    private static void Run(string program,int optimization){
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("uuid-bytes.gf",program));
        var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"UUID bytes exit {result.ExitCode}: {result.StandardError}");
    }
}
