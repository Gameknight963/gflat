using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class StreamBinaryTests
{
    private sealed record Scalar(string Suffix, string Type, int Width, BigInteger Bits);

    private static IEnumerable<Scalar> Scalars()
    {
        foreach (var (suffix, type, width) in new[] {
            ("UInt16", "ushort", 2), ("UInt32", "uint", 4), ("UInt64", "ulong", 8),
            ("Int16", "short", 2), ("Int32", "int", 4), ("Int64", "long", 8), ("ExtraLong", "extralong", 16) })
        {
            BigInteger mask = (BigInteger.One << (width * 8)) - 1;
            foreach (BigInteger bits in new[] { BigInteger.Zero, BigInteger.One, mask, mask >> 1, BigInteger.One << (width * 8 - 1), BigInteger.Parse("81985529216486895", CultureInfo.InvariantCulture) & mask })
                yield return new(suffix, type, width, bits);
        }
        foreach (uint bits in new uint[] { 0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000, 0x3f800000, 0xbf800000, 0x7f7fffff, 0x7f800000, 0xff800000, 0x7fc01234, 0x7f801234, 0xffc05678 })
            yield return new("Float", "float", 4, bits);
        foreach (ulong bits in new ulong[] { 0, 0x8000000000000000, 1, 0x8000000000000001, 0x000fffffffffffff, 0x0010000000000000, 0x3ff0000000000000, 0xbff0000000000000, 0x7fefffffffffffff, 0x7ff0000000000000, 0xfff0000000000000, 0x7ff8000012345678, 0x7ff0000012345678, 0xfff8000056789abc })
            yield return new("Double", "double", 8, bits);
    }

    private static readonly (string Suffix, string Type, int Width)[] Families = [
        ("UInt16", "ushort", 2), ("UInt32", "uint", 4), ("UInt64", "ulong", 8),
        ("Int16", "short", 2), ("Int32", "int", 4), ("Int64", "long", 8),
        ("Float", "float", 4), ("Double", "double", 8), ("ExtraLong", "extralong", 16) ];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostEncodedRecordsRoundTripThroughChunkedBorrowedEndpointsWithoutAllocation(int optimization)
    {
        var bytes = new List<byte>();
        var operations = new StringBuilder();
        int records = 0;
        int readCalls = 0;
        foreach (string order in new[] { "LittleEndian", "BigEndian" })
        foreach (Scalar scalar in Scalars())
        {
            byte[] encoded = new byte[scalar.Width];
            for (int index = 0; index < encoded.Length; index++)
                encoded[order == "LittleEndian" ? index : encoded.Length - 1 - index] = (byte)((scalar.Bits >> (index * 8)) & 255);
            bytes.AddRange(encoded);
            ulong low = (ulong)(scalar.Bits & ulong.MaxValue);
            ulong high = (ulong)(scalar.Bits >> 64);
            string assertion = scalar.Suffix switch {
                "Float" => $"if((value).reinterpret<uint>()!={low}u)return 11;",
                "Double" => $"if((value).reinterpret<ulong>()!={low}ul)return 12;",
                "ExtraLong" => $"if((ulong)value!={low}ul||(ulong)(value>>64)!={high}ul)return 13;",
                _ => $"if(value!=({scalar.Type}){low}ul)return 14;"
            };
            operations.AppendLine($$"""
                { {{scalar.Type}} value=std::IO::Read{{scalar.Suffix}}{{order}}(input);
                  {{assertion}}
                  sink.ExpectedWidth={{scalar.Width}};std::IO::Write{{scalar.Suffix}}{{order}}(output,value); }
                """);
            records++;
            readCalls += (scalar.Width + 1) / 2;
        }
        Run($$"""
            using std;using std::IO;
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){attempts++;return null;}
                public replace void Free(void* address)nothrow{}
            }
            struct Source:IByteReader{
                private readonly(char)* data;private nuint length;
                public nuint Position;public nuint Calls;
                public Source(readonly(char)* storage,nuint count){data=storage;length=count;Position=0;Calls=0;}
                public nuint Read(char* output,nuint count){
                    Calls++;nuint take=length-Position;if(take>count)take=count;if(take>2)take=2;
                    for(nuint i=0;i<take;i++)output[i]=data[Position+i];Position+=take;return take;
                }
            }
            struct Sink:ITextWriter{
                private char* data;public nuint Position;public nuint Calls;public nuint ExpectedWidth;
                public Sink(char* storage){data=storage;Position=0;Calls=0;ExpectedWidth=0;}
                public void Write(readonly(char)* input,nuint count){
                    if(count!=ExpectedWidth)throw new IOException("Wrong fixed-width sink count");
                    Calls++;for(nuint i=0;i<count;i++)data[Position+i]=input[i];Position+=count;
                }
            }
            int main(){
                char[{{bytes.Count + 2}}] data;char[{{bytes.Count}}] expected=[{{string.Join(",", bytes.Select(value => "(char)" + value))}}];
                data[0]='Q';data[{{bytes.Count + 1}}]='R';
                Source source=new(expected,{{bytes.Count}});Sink sink=new(&data[1]);IByteReader* input=&source;ITextWriter* output=&sink;
                {{operations}}
                if(source.Position!={{bytes.Count}}||source.Calls!={{readCalls}}||sink.Position!={{bytes.Count}}||sink.Calls!={{records}})return 21;
                if(data[0]!='Q'||data[{{bytes.Count + 1}}]!='R')return 22;
                for(nuint i=0;i<{{bytes.Count}};i++)if(data[i+1]!=expected[i])return 23;
                if(attempts!=0)return 24;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EachWidthRejectsEveryTruncatedPrefixAndPropagatesSourceFailureWithoutRetry(int optimization)
    {
        var operations = new StringBuilder();
        foreach (string order in new[] { "LittleEndian", "BigEndian" })
        foreach (var (suffix, _, width) in Families)
            operations.AppendLine($$"""
                for(nuint length=0;length<{{width}};length++){
                    Source source=new(length,false);bool caught=false;
                    try{std::IO::Read{{suffix}}{{order}}(&source);}catch(ref EndOfStreamException error){caught=true;}
                    if(!caught||source.Position!=length||source.Calls!=(length+1)/2+1)return 31;
                }
                {Source source=new({{width}},true);bool caught=false;
                 try{std::IO::Read{{suffix}}{{order}}(&source);}
                 catch(ref EndOfStreamException error){return 32;}
                 catch(ref IOException error){caught=error.GetCode()==71;}
                 if(!caught||source.Calls!=1||source.Position!=1)return 33;}
                """);
        Run($$"""
            using std;using std::IO;
            class ReadFailure:IOException{
                public ReadFailure():base("sentinel read failure"){code=71;}
            }
            struct Source:IByteReader{
                private nuint length;private bool fail;public nuint Position;public nuint Calls;
                public Source(nuint count,bool error){length=count;fail=error;Position=0;Calls=0;}
                public nuint Read(char* output,nuint count){
                    Calls++;
                    if(fail){output[0]='X';Position++;throw new ReadFailure();}
                    nuint take=length-Position;if(take>count)take=count;if(take>2)take=2;
                    for(nuint i=0;i<take;i++)output[i]=(char)(17+Position+i);Position+=take;return take;
                }
            }
            int main(){ {{operations}} return 0; }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryWriterUsesOneExactWidthCallAndPropagatesPartialSinkFailure(int optimization)
    {
        var operations = new StringBuilder();
        foreach (string order in new[] { "LittleEndian", "BigEndian" })
        foreach (var (suffix, type, width) in Families)
            operations.AppendLine($$"""
                {Sink sink=new(&data[1],{{width}},false);std::IO::Write{{suffix}}{{order}}(&sink,({{type}})1);
                 if(sink.Calls!=1||sink.Count!={{width}}||data[0]!='Q'||data[17]!='R')return 41;}
                {Sink sink=new(&data[1],{{width}},true);bool caught=false;
                 try{std::IO::Write{{suffix}}{{order}}(&sink,({{type}})1);}
                 catch(ref IOException error){caught=error.GetCode()==72;}
                 if(!caught||sink.Calls!=1||sink.Count!=1||data[0]!='Q'||data[17]!='R')return 42;}
                """);
        Run($$"""
            using std;using std::IO;
            class WriteFailure:IOException{
                public WriteFailure():base("sentinel write failure"){code=72;}
            }
            struct Sink:ITextWriter{
                private char* data;private nuint width;private bool fail;public nuint Count;public nuint Calls;
                public Sink(char* storage,nuint size,bool error){data=storage;width=size;fail=error;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    Calls++;if(count!=width)throw new IOException("Wrong fixed-width count");
                    if(fail){data[0]=input[0];Count=1;throw new WriteFailure();}
                    for(nuint i=0;i<count;i++)data[i]=input[i];Count=count;
                }
            }
            int main(){char[18] data;data[0]='Q';data[17]='R'; {{operations}} return 0;}
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        string[] names = [ "core/Exception.gf", "core/ITextWriter.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
            "core/IO/byte_reader.gf", "core/Binary/BinaryPrimitives.gf", "core/IO/byte_reader_binary.gf",
            "core/IO/text_writer_binary.gf", "libc/Memory/allocation.gf" ];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("stream-binary-tests.gf", program));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
