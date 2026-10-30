using System.Globalization;
using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class StreamVarIntTests
{
    private sealed record Family(string Name, string Type, bool Signed, bool Wide);
    private static readonly Family[] Families = [new("VarUInt32", "uint", false, false), new("VarUInt64", "ulong", false, true),
        new("ZigZagInt32", "int", true, false), new("ZigZagInt64", "long", true, true)];

    private static byte[] Encode(ulong value)
    {
        var result = new List<byte>();
        do
        {
            byte next = (byte)(value & 127);
            value >>= 7;
            if (value != 0) next |= 128;
            result.Add(next);
        } while (value != 0);
        return result.ToArray();
    }

    private static ulong ZigZag(long value)
        => value >= 0 ? (ulong)value * 2 : (ulong)(-(value + 1)) * 2 + 1;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentHostCanonicalAndZigZagCorpusExercisesAllEightOperationsWithoutAllocation(int optimization)
    {
        var packet = new List<byte>();
        var operations = new StringBuilder();
        int records = 0;
        foreach (Family family in Families)
        {
            int width = family.Wide ? 64 : 32;
            ulong mask = family.Wide ? ulong.MaxValue : uint.MaxValue;
            var bits = new SortedSet<ulong> { 0, 1, 2, 127, 128, 129, 300, mask, mask >> 1, 1UL << (width - 1) };
            for (int shift = 7; shift < width; shift += 7)
            {
                ulong boundary = 1UL << shift;
                bits.Add(boundary - 1); bits.Add(boundary); bits.Add((boundary + 1) & mask);
            }
            var random = new Random(family.Wide ? 191791 : 191792);
            for (int index = 0; index < 32; index++)
            {
                byte[] raw = new byte[8]; random.NextBytes(raw); bits.Add(BitConverter.ToUInt64(raw) & mask);
            }
            var lengths = new List<int>();
            foreach (ulong value in bits)
            {
                ulong encoded = value;
                if (family.Signed) encoded = ZigZag(family.Wide ? unchecked((long)value) : unchecked((int)value));
                byte[] bytes = Encode(encoded); lengths.Add(bytes.Length); packet.AddRange(bytes); records++;
            }
            string integer = family.Wide ? "ulong" : "uint", suffix = family.Wide ? "ul" : "u";
            operations.AppendLine($$"""
                { {{integer}}[{{bits.Count}}] expected=[{{string.Join(",", bits.Select(value => value.ToString(CultureInfo.InvariantCulture) + suffix))}}];
                  nuint[{{lengths.Count}}] lengths=[{{string.Join(",", lengths)}}];
                  for(nuint index=0;index<{{bits.Count}};index++){
                    {{family.Type}} value=Standard::IO::Read{{family.Name}}(input);
                    if(({{integer}})value!=expected[index])return 11;
                    sink.Expected=lengths[index];Standard::IO::Write{{family.Name}}(output,value);
                    position+=lengths[index];if(source.Position!=position||sink.Position!=position)return 12;
                  }
                }
                """);
        }
        Run($$"""
            using Standard;using Standard::IO;
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){attempts++;return null;}
                public replace void Free(void* storage)nothrow{}
            }
            struct Source:IByteReader{
                private readonly(char)* bytes;private nuint length;public nuint Position;public nuint Calls;
                public Source(readonly(char)* input,nuint count){bytes=input;length=count;Position=0;Calls=0;}
                public nuint Read(char* output,nuint count){
                    Calls++;if(count!=1)throw new IOException("VarInt read-ahead");
                    if(Position==length)return 0;output[0]=bytes[Position];Position++;return 1;
                }
            }
            struct Sink:ITextWriter{
                private char* bytes;public nuint Position;public nuint Calls;public nuint Expected;
                public Sink(char* output){bytes=output;Position=0;Calls=0;Expected=0;}
                public void Write(readonly(char)* input,nuint count){
                    if(count!=Expected||count==0||count>10)throw new IOException("Invalid VarInt write size");
                    Calls++;for(nuint i=0;i<count;i++)bytes[Position+i]=input[i];Position+=count;
                }
            }
            int main(){
                char[{{packet.Count}}] expected=[{{string.Join(",", packet.Select(value => "(char)" + value))}}];
                char[{{packet.Count + 2}}] actual;actual[0]='Q';actual[{{packet.Count + 1}}]='R';
                Source source=new(expected,{{packet.Count}});Sink sink=new(&actual[1]);IByteReader* input=&source;ITextWriter* output=&sink;nuint position=0;
                {{operations}}
                if(source.Position!={{packet.Count}}||source.Calls!={{packet.Count}}||sink.Calls!={{records}})return 21;
                if(actual[0]!='Q'||actual[{{packet.Count + 1}}]!='R')return 22;
                for(nuint i=0;i<{{packet.Count}};i++)if(actual[i+1]!=expected[i])return 23;
                if(attempts!=0)return 24;return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidEncodingsStopBeforeFollowingFieldAndTruncationDiffersFromSourceErrors(int optimization)
    {
        var operations = new StringBuilder();
        foreach (Family family in Families)
        {
            int maximum = family.Wide ? 10 : 5;
            byte finalOverflow = family.Wide ? (byte)2 : (byte)16;
            List<byte[]> invalid = [[128, 0], [129, 0], [255, 0]];
            foreach (byte tail in new byte[] { finalOverflow, 128, 255 })
            {
                byte[] bytes = Enumerable.Repeat((byte)128, maximum).ToArray(); bytes[^1] = tail; invalid.Add(bytes);
            }
            foreach (byte[] bytes in invalid)
            foreach (byte marker in new byte[] { 90, 128 })
            {
                byte[] input = [..bytes, marker];
                operations.AppendLine($$"""
                    {char[{{input.Length}}] bytes=[{{string.Join(",", input.Select(value => "(char)" + value))}}];
                     Source source=new(bytes,{{input.Length}},0);bool caught=false;
                     try{Standard::IO::Read{{family.Name}}(&source);}
                     catch(ref EndOfStreamException error){return 31;}
                     catch(ref IOException error){caught=true;}
                     if(!caught||source.Position!={{bytes.Length}}||source.Calls!={{bytes.Length}})return 32;
                     char next='\0';if(source.Read(&next,1)!=1||(byte)next!=(byte){{marker}})return 33;
                     char[{{input.Length}}] original=[{{string.Join(",", input.Select(value => "(char)" + value))}}];
                     for(nuint i=0;i<{{input.Length}};i++)if(bytes[i]!=original[i])return 34;
                    }
                    """);
            }
            operations.AppendLine($$"""
                for(nuint length=0;length<{{maximum}};length++){
                    char[10] bytes;for(nuint i=0;i<10;i++)bytes[i]=(char)128;
                    Source source=new(bytes,length,0);bool caught=false;
                    try{Standard::IO::Read{{family.Name}}(&source);}catch(ref EndOfStreamException error){caught=true;}
                    if(!caught||source.Position!=length||source.Calls!=length+1)return 35;
                }
                {char[10] bytes;for(nuint i=0;i<10;i++)bytes[i]=(char)128;
                 Source source=new(bytes,10,3);bool caught=false;
                 try{Standard::IO::Read{{family.Name}}(&source);}
                 catch(ref EndOfStreamException error){return 36;}
                 catch(ref IOException error){caught=error.GetCode()==73;}
                 if(!caught||source.Calls!=3||source.Position!=3)return 37;}
                """);
        }
        Run($$"""
            using Standard;using Standard::IO;
            class ReadFailure:IOException{
                public ReadFailure():base("VarInt source failure"){code=73;}
            }
            struct Source:IByteReader{
                private readonly(char)* bytes;private nuint length;private nuint failAt;public nuint Position;public nuint Calls;
                public Source(readonly(char)* input,nuint count,nuint errorCall){bytes=input;length=count;failAt=errorCall;Position=0;Calls=0;}
                public nuint Read(char* output,nuint count){
                    Calls++;if(count!=1)throw new IOException("VarInt read-ahead");
                    if(failAt!=0&&Calls==failAt){output[0]='X';Position++;throw new ReadFailure();}
                    if(Position==length)return 0;output[0]=bytes[Position];Position++;return 1;
                }
            }
            int main(){ {{operations}} return 0;}
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryWriterMakesOneCanonicalCallAndPropagatesPartialSinkFailureWithoutRetry(int optimization)
    {
        var operations = new StringBuilder();
        foreach (Family family in Families)
        {
            ulong raw = family.Wide ? ulong.MaxValue : uint.MaxValue;
            ulong encoded = family.Signed ? 1UL : raw;
            byte[] expected = Encode(encoded);
            operations.AppendLine($$"""
                {Sink sink=new(&bytes[1],false);Standard::IO::Write{{family.Name}}(&sink,({{family.Type}}){{raw}}ul);
                 char[{{expected.Length}}] expected=[{{string.Join(",", expected.Select(value => "(char)" + value))}}];
                 if(sink.Calls!=1||sink.Count!={{expected.Length}})return 41;
                 for(nuint i=0;i<{{expected.Length}};i++)if(bytes[i+1]!=expected[i])return 42;
                 if(bytes[0]!='Q'||bytes[11]!='R')return 43;}
                {Sink sink=new(&bytes[1],true);bool caught=false;
                 try{Standard::IO::Write{{family.Name}}(&sink,({{family.Type}}){{raw}}ul);}
                 catch(ref IOException error){caught=error.GetCode()==74;}
                 if(!caught||sink.Calls!=1||sink.Count!=1||bytes[0]!='Q'||bytes[11]!='R')return 44;}
                """);
        }
        Run($$"""
            using Standard;using Standard::IO;
            class WriteFailure:IOException{
                public WriteFailure():base("VarInt sink failure"){code=74;}
            }
            struct Sink:ITextWriter{
                private char* bytes;private bool fail;public nuint Count;public nuint Calls;
                public Sink(char* output,bool error){bytes=output;fail=error;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    Calls++;if(count==0||count>10)throw new IOException("Invalid VarInt write count");
                    if(fail){bytes[0]=input[0];Count=1;throw new WriteFailure();}
                    for(nuint i=0;i<count;i++)bytes[i]=input[i];Count=count;
                }
            }
            int main(){char[12] bytes;bytes[0]='Q';bytes[11]='R'; {{operations}} return 0;}
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        string[] names = ["core/Exception.gf", "core/ITextWriter.gf", "core/IO/IByteReader.gf", "core/IO/IOException.gf",
            "core/IO/EndOfStreamException.gf", "core/IO/byte_reader.gf", "core/Binary/VarInt.gf",
            "core/IO/byte_reader_var_int.gf", "core/IO/text_writer_var_int.gf", "libc/Memory/allocation.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("stream-varint-tests.gf", program));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
