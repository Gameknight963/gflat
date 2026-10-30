using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class Base32EncoderTests
{
    private static string Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var text = new StringBuilder();
        for (int start = 0; start < data.Length * 8; start += 5)
        {
            int digit = 0;
            for (int offset = 0; offset < 5; offset++)
            {
                int bit = start + offset;
                digit *= 2;
                if (bit < data.Length * 8) digit += (data[bit / 8] >> (7 - bit % 8)) & 1;
            }
            text.Append(alphabet[digit]);
        }
        while (text.Length % 8 != 0) text.Append('=');
        return text.ToString();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentBitOracleRfcVectorsAndChunkBoundariesPreservePaddingAndGuardsWithoutAllocation(int optimization)
    {
        string[] plain = ["", "f", "fo", "foo", "foob", "fooba", "foobar"];
        string[] rfc = ["", "MY======", "MZXQ====", "MZXW6===", "MZXW6YQ=", "MZXW6YTB", "MZXW6YTBOI======"];
        var cases = plain.Select(Encoding.ASCII.GetBytes).ToList();
        for (int index = 0; index < cases.Count; index++) Assert.Equal(rfc[index], Encode(cases[index]));
        cases.Add(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        var random = new Random(735902);
        foreach (int length in new[] { 4, 5, 6, 9, 10, 11, 154, 155, 156, 159, 160, 161, 164, 165, 166, 319, 320, 321 })
        {
            byte[] data = new byte[length]; random.NextBytes(data); cases.Add(data);
        }
        string input = string.Concat(cases.Select(Convert.ToHexString));
        string expected = string.Concat(cases.Select(Encode));
        Run($$"""
            using Standard;using Standard::Encoding;
            int Digit(char value)nothrow{if(value<='9')return (int)value-48;return (int)value-65+10;}
            struct Sink:ITextWriter{
                private char* data;public nuint Count;public nuint Calls;
                public Sink(char* storage){data=storage;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    if(count==0||count>256||count%8!=0||count>520-Count)throw new Exception("Invalid Base32 block");
                    Calls++;for(nuint i=0;i<count;i++)data[Count+i]=input[i];Count+=count;
                }
            }
            int main(){
                nuint[{{cases.Count}}] lengths=[{{string.Join(",", cases.Select(value => value.Length))}}];
                readonly(char)* input="{{input}}";readonly(char)* oracle="{{expected}}";
                nuint[9] chunks=[1,2,3,4,5,17,159,160,161];nuint sourceOffset=0;nuint oracleOffset=0;
                for(nuint row=0;row<{{cases.Count}};row++){
                    nuint length=lengths[row];nuint encoded=((length+4)/5)*8;
                    for(nuint mode=0;mode<9;mode++){
                        char[321] bytes;for(nuint i=0;i<length;i++)bytes[i]=(char)(Digit(input[sourceOffset+2*i])*16+Digit(input[sourceOffset+2*i+1]));
                        char[522] output;for(nuint i=0;i<522;i++)output[i]='?';Sink sink=new(&output[1]);Base32Encoder writer=new(&sink);
                        writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);nuint position=0;
                        while(position<length){
                            nuint count=length-position;if(count>chunks[mode])count=chunks[mode];
                            writer.Write((readonly(char)*)bytes+position,count);position+=count;writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                        }
                        if(sink.Count!=(length/5)*8)return 1;
                        for(nuint i=0;i<sink.Count;i++)if(output[i+1]=='=')return 2;
                        for(nuint i=0;i<length;i++)bytes[i]='?';
                        writer.Complete();nuint calls=sink.Calls;writer.Complete();
                        if(sink.Calls!=calls||sink.Count!=encoded||output[0]!='?'||output[encoded+1]!='?')return 3;
                        for(nuint i=0;i<encoded;i++)if(output[i+1]!=oracle[oracleOffset+i])return 4;
                    }
                    sourceOffset+=length*2;oracleOffset+=encoded;
                }
                if(attempts!=0)return 5;return 0;
            }
            """, optimization, denyAllocation: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PartialSinkFailuresPreserveOriginalErrorAndPoisonAllReuseWithoutReplay(int optimization)
    {
        string small = Encode(Encoding.ASCII.GetBytes("abcdefghijkl"));
        string large = Encode(Enumerable.Range(0, 166).Select(value => (byte)value).ToArray());
        Run($$"""
            using Standard;using Standard::Encoding;
            class SinkFailure:Exception{
                public SinkFailure():base("original Base32 sink failure",82){}
            }
            struct Sink:ITextWriter{
                private char* output;public int Budget;public nuint Count;public nuint Calls;
                public Sink(char* data,int budget){output=data;Budget=budget;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    Calls++;for(nuint i=0;i<count;i++){
                        if(Budget==0)throw new SinkFailure();output[Count]=input[i];Count++;Budget--;
                    }
                }
            }
            bool Scenario(int budget,bool large){
                char[602] output;for(nuint i=0;i<602;i++)output[i]='?';Sink sink=new(&output[1],budget);Base32Encoder writer=new(&sink);
                int caught=0;readonly(char)* oracle="{{small}}";if(large)oracle="{{large}}";
                try{
                    if(large){char[166] input;for(nuint i=0;i<166;i++)input[i]=(char)i;writer.Write(input,166);}
                    else{writer.Write("a",1);writer.Write("bcdefghij",9);writer.Write("kl",2);}
                    writer.Complete();
                }catch(ref SinkFailure error){if(error.GetCode()!=82)return false;caught++;}catch{return false;}
                if(caught!=1||sink.Count!=(nuint)budget||output[0]!='?'||output[(nuint)budget+1]!='?')return false;
                for(nuint i=0;i<(nuint)budget;i++)if(output[i+1]!=oracle[i])return false;
                nuint calls=sink.Calls;sink.Budget=999;
                try{writer.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(ref Exception error){caught++;}
                try{writer.Write("later",5);}catch(ref Exception error){caught++;}
                try{writer.Complete();}catch(ref Exception error){caught++;}
                Base32Encoder duplicate=writer;try{duplicate.Complete();}catch(ref Exception error){caught++;}
                return caught==5&&sink.Calls==calls&&sink.Count==(nuint)budget;
            }
            int main(){
                for(int budget=0;budget<24;budget++)if(!Scenario(budget,false))return 11;
                int[6] budgets=[0,255,256,263,264,271];for(nuint i=0;i<6;i++)if(!Scenario(budgets[i],true))return 12;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CarryCopiesAndReaderPipelineKeepExplicitCompletionAndBorrowedSinkLifetime(int optimization)
    {
        string[] prefixes = Enumerable.Range(1, 4).Select(count => Encode(Encoding.ASCII.GetBytes("foob"[..count]))).ToArray();
        string pipeline = Encode(Encoding.ASCII.GetBytes("foobar"));
        Run($$"""
            using Standard;using Standard::Encoding;using Standard::IO;using Standard::Binary;
            int destroyed=0;
            struct Sink:ITextWriter{
                private char* output;public nuint Count;public nuint Calls;
                public Sink(char* data){output=data;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){Calls++;for(nuint i=0;i<count;i++)output[Count+i]=input[i];Count+=count;}
                ~Sink(){destroyed++;}
            }
            int main(){
                readonly(char)* tails="{{string.Concat(prefixes)}}";
                for(nuint carry=1;carry<=4;carry++){
                    char[34] output;for(nuint i=0;i<34;i++)output[i]='?';int destroyedBefore=destroyed;
                    {
                        Sink sink=new(&output[1]);int before=attempts;
                        {
                            char[4] input=['f','o','o','b'];Base32Encoder writer=new(&sink);writer.Write(input,carry);
                            Base32Encoder duplicate=writer;for(nuint i=0;i<4;i++)input[i]='?';Base32Encoder moved=move duplicate;
                            readonly(char)* whole="fooba";moved.Write(whole+carry,5-carry);moved.Complete();
                            if(sink.Count!=8)return 21;for(nuint i=0;i<8;i++)if(output[i+1]!="MZXW6YTB"[i])return 22;
                            writer.Complete();if(sink.Count!=16)return 23;
                            for(nuint i=0;i<8;i++)if(output[i+9]!=tails[(carry-1)*8+i])return 24;
                            Base32Encoder completed=moved;nuint calls=sink.Calls;completed.Complete();completed.Complete();
                            if(sink.Calls!=calls||attempts!=before)return 25;
                            int rejected=0;try{completed.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(ref Exception error){rejected++;}
                            if(rejected!=1||sink.Calls!=calls)return 26;
                        }
                        nuint calls=sink.Calls;{Base32Encoder abandoned=new(&sink);abandoned.Write("f",1);}
                        if(sink.Calls!=calls||destroyed!=destroyedBefore||output[0]!='?'||output[17]!='?')return 27;
                    }
                    if(destroyed!=destroyedBefore+1)return 28;
                }
                char[26] encoded;for(nuint i=0;i<26;i++)encoded[i]='?';int baseline=attempts;
                {
                    Sink sink=new(&encoded[1]);Base32Encoder writer=new(&sink);byte[6] bytes=[(byte)'f',(byte)'o',(byte)'o',(byte)'b',(byte)'a',(byte)'r'];
                    BinaryBufferReader reader=new(bytes,6);IByteReader* input=&reader;char[4] scratch;for(nuint i=0;i<4;i++)scratch[i]='!';
                    if(Standard::IO::CopyTo(input,&writer,&scratch[1],2)!=6||reader.Position!=6||sink.Count!=8)return 29;
                    for(nuint i=0;i<8;i++)if(encoded[i+1]!="MZXW6YTB"[i])return 30;
                    if(scratch[0]!='!'||scratch[3]!='!'||encoded[9]!='?')return 31;
                    writer.Complete();nuint calls=sink.Calls;writer.Complete();if(sink.Count!=16||sink.Calls!=calls||attempts!=baseline)return 32;
                    for(nuint i=0;i<16;i++)if(encoded[i+1]!="{{pipeline}}"[i])return 33;
                    if(encoded[0]!='?'||encoded[17]!='?')return 34;
                }
                return 0;
            }
            """, optimization, denyAllocation: true);
    }

    private static void Run(string program, int optimization, bool denyAllocation = false)
    {
        string[] names = ["core/Exception.gf", "core/ITextWriter.gf", "core/Encoding/Base32.gf", "core/Encoding/Base32Encoder.gf",
            "core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/byte_reader.gf",
            "core/Binary/BinaryPrimitives.gf", "core/Binary/BinaryBufferReader.gf", "core/Memory/bytes.gf", "libc/Memory/allocation.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("base32-encoder-tests.gf", program)).ToList();
        if (denyAllocation) sources.Add(new SourceFile("deny-allocation.gf", """
            int attempts=0;
            namespace Memory{
                public replace void*? Allocate(nuint size){attempts++;return null;}
                public replace void Free(void* storage)nothrow{}
            }
            """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
