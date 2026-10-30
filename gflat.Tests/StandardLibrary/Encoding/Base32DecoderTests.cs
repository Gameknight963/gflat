using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class Base32DecoderTests
{
    private static string Encode(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var text = new StringBuilder();
        for (int start = 0; start < bytes.Length * 8; start += 5)
        {
            int digit = 0;
            for (int offset = 0; offset < 5; offset++)
            {
                int bit = start + offset; digit *= 2;
                if (bit < bytes.Length * 8) digit += (bytes[bit / 8] >> (7 - bit % 8)) & 1;
            }
            text.Append(alphabet[digit]);
        }
        while (text.Length % 8 != 0) text.Append('=');
        return text.ToString();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentHostBytesRfcVectorsAndChunkBoundariesPreserveExactOutputWithoutAllocation(int optimization)
    {
        string[] plain = ["", "f", "fo", "foo", "foob", "fooba", "foobar"];
        string[] rfc = ["", "MY======", "MZXQ====", "MZXW6===", "MZXW6YQ=", "MZXW6YTB", "MZXW6YTBOI======"];
        var cases = plain.Select(Encoding.ASCII.GetBytes).ToList();
        for (int index = 0; index < cases.Count; index++) Assert.Equal(rfc[index], Encode(cases[index]));
        cases.Add(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        var random = new Random(807531);
        foreach (int length in new[] { 4, 5, 6, 9, 10, 11, 194, 195, 196, 199, 200, 201, 204, 205, 255, 256, 257, 399, 400, 401 })
        {
            byte[] bytes = new byte[length]; random.NextBytes(bytes); cases.Add(bytes);
        }
        string encoded = string.Concat(cases.Select(Encode));
        string expected = string.Concat(cases.Select(Convert.ToHexString));
        Run($$"""
            using std;using std::Encoding;
            int Digit(char value)nothrow{if(value<='9')return (int)value-48;return (int)value-65+10;}
            struct Sink:ITextWriter{
                private char* output;public nuint Count;public nuint Calls;
                public Sink(char* data){output=data;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    if(count==0||count>200||count>401-Count)throw new Exception("Invalid Base32 decoded block");
                    Calls++;for(nuint i=0;i<count;i++)output[Count+i]=input[i];Count+=count;
                }
            }
            int main(){
                nuint[{{cases.Count}}] lengths=[{{string.Join(",", cases.Select(value => value.Length))}}];
                readonly(char)* encoded="{{encoded}}";readonly(char)* oracle="{{expected}}";
                nuint[12] chunks=[1,2,3,4,5,6,7,8,17,319,320,321];nuint textOffset=0;nuint byteOffset=0;
                for(nuint row=0;row<{{cases.Count}};row++){
                    nuint length=lengths[row];nuint size=((length+4)/5)*8;
                    for(nuint mode=0;mode<12;mode++){
                        char[648] text;for(nuint i=0;i<size;i++)text[i]=encoded[textOffset+i];
                        char[403] output;for(nuint i=0;i<403;i++)output[i]='?';Sink sink=new(&output[1]);Base32Decoder decoder=new(&sink);
                        decoder.Write((readonly(char)*)1,0);nuint position=0;
                        while(position<size){
                            nuint count=size-position;if(count>chunks[mode])count=chunks[mode];decoder.Write((readonly(char)*)text+position,count);
                            for(nuint i=0;i<count;i++)text[position+i]='?';position+=count;decoder.Write((readonly(char)*)1,0);
                        }
                        if(sink.Count!=length)return 1;nuint calls=sink.Calls;decoder.Complete();decoder.Complete();
                        if(sink.Calls!=calls||output[0]!='?'||output[length+1]!='?')return 2;
                        for(nuint i=0;i<length;i++)if((byte)output[i+1]!=(byte)(Digit(oracle[byteOffset+2*i])*16+Digit(oracle[byteOffset+2*i+1])))return 3;
                    }
                    textOffset+=size;byteOffset+=length*2;
                }
                if(attempts!=0)return 4;return 0;
            }
            """, optimization, denyAllocation: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictInvalidInputsAndIncompleteGroupsPoisonWithoutPromisingRollbackOfCurrentBatch(int optimization)
    {
        var cases = new List<(byte[] Text, byte[] Prefix)>();
        void Add(string text, string prefix = "") => cases.Add((Encoding.ASCII.GetBytes(text), Encoding.ASCII.GetBytes(prefix)));
        for (int length = 1; length <= 7; length++) Add("MZXW6YTB"[..length]);
        foreach (string invalid in new[] { "=", "========", "A=======", "AAA=====", "AAAAAA==", "MY=====", "MY=======",
            "my======", "MY======\n", "MY====== ", " MY======", "MZ======", "MZXR====", "MZXW7===", "MZXW6YR=" })
            Add(invalid, invalid.StartsWith("MY======", StringComparison.Ordinal) ? "f" : "");
        Add("MY======A", "f"); Add("MY======MY======", "f"); Add("MZXW6YTBMY======A", "foobaf"); Add("MZXW6YTBM", "fooba");
        cases.Add(([65, 65, 65, 0, 65, 65, 65, 65], []));
        cases.Add(([65, 65, 65, 255, 65, 65, 65, 65], []));
        cases.Add((Encoding.ASCII.GetBytes(new string('A', 320) + "!"), new byte[200]));
        cases.Add((Encoding.ASCII.GetBytes(new string('A', 328) + "MZ======"), new byte[205]));
        string textHex = string.Concat(cases.Select(value => Convert.ToHexString(value.Text)));
        string prefixes = string.Concat(cases.Select(value => Convert.ToHexString(value.Prefix)));
        Run($$"""
            using std;using std::Encoding;
            int Digit(char value)nothrow{if(value<='9')return (int)value-48;return (int)value-65+10;}
            struct Sink:ITextWriter{
                private char* output;public nuint Count;public nuint Calls;public bool Bad;
                public Sink(char* data){output=data;Count=0;Calls=0;Bad=false;}
                public void Write(readonly(char)* input,nuint count){
                    if(count==0||count>200||count>400-Count){Bad=true;throw new Exception("Invalid decoded output");}
                    Calls++;for(nuint i=0;i<count;i++)output[Count+i]=input[i];Count+=count;
                }
            }
            bool Check(readonly(char)* text,nuint length,nuint split,readonly(char)* prefix,nuint prefixLength){
                char[402] output;for(nuint i=0;i<402;i++)output[i]='?';Sink sink=new(&output[1]);Base32Decoder decoder=new(&sink);int caught=0;
                try{decoder.Write(text,split);decoder.Write(text+split,length-split);decoder.Complete();}catch(ref Exception error){caught++;}
                if(caught!=1||sink.Bad||sink.Count>prefixLength||output[0]!='?'||output[401]!='?')return false;
                for(nuint i=0;i<sink.Count;i++)if((byte)output[i+1]!=(byte)(Digit(prefix[2*i])*16+Digit(prefix[2*i+1])))return false;
                nuint calls=sink.Calls;nuint count=sink.Count;
                try{decoder.Write((readonly(char)*)1,0);}catch(ref Exception error){caught++;}
                try{decoder.Write("AAAAAAAA",8);}catch(ref Exception error){caught++;}
                try{decoder.Complete();}catch(ref Exception error){caught++;}
                Base32Decoder duplicate=decoder;try{duplicate.Complete();}catch(ref Exception error){caught++;}
                return caught==5&&sink.Calls==calls&&sink.Count==count;
            }
            int main(){
                readonly(char)* textHex="{{textHex}}";readonly(char)* prefixes="{{prefixes}}";
                nuint[{{cases.Count}}] lengths=[{{string.Join(",", cases.Select(value => value.Text.Length))}}];
                nuint[{{cases.Count}}] prefixLengths=[{{string.Join(",", cases.Select(value => value.Prefix.Length))}}];
                nuint textOffset=0;nuint prefixOffset=0;char[336] text;
                for(nuint row=0;row<{{cases.Count}};row++){
                    nuint length=lengths[row];for(nuint i=0;i<length;i++)text[i]=(char)(Digit(textHex[textOffset+2*i])*16+Digit(textHex[textOffset+2*i+1]));
                    for(nuint split=0;split<=length;split++)if(!Check(text,length,split,prefixes+prefixOffset,prefixLengths[row]))return 11;
                    textOffset+=length*2;prefixOffset+=prefixLengths[row]*2;
                }
                char[402] output;Sink sink=new(output);Base32Decoder prior=new(&sink);prior.Write("MZXW6YTB",8);
                bool caught=false;try{prior.Write("MZ======",8);}catch(ref Exception error){caught=true;}
                if(!caught||sink.Count!=5)return 12;for(nuint i=0;i<5;i++)if(output[i]!="fooba"[i])return 13;
                char[328] batchInput;for(nuint i=0;i<320;i++)batchInput[i]='A';for(nuint i=0;i<8;i++)batchInput[320+i]="MZ======"[i];
                char[402] batchOutput;Sink committed=new(batchOutput);Base32Decoder batch=new(&committed);caught=false;
                try{batch.Write(batchInput,328);}catch(ref Exception error){caught=true;}
                if(!caught||committed.Count!=200)return 14;for(nuint i=0;i<200;i++)if(batchOutput[i]!='\0')return 15;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SinkFailureCarryCopiesAndReaderPipelinePreserveBorrowingCompletionAndFailureState(int optimization)
    {
        string small = Encode(Encoding.ASCII.GetBytes("foobar"));
        byte[] largeBytes = Enumerable.Range(0, 201).Select(value => (byte)value).ToArray();
        string large = Encode(largeBytes);
        Run($$"""
            using std;using std::Encoding;using std::IO;using std::Binary;
            int destroyed=0;
            int Digit(char value)nothrow{if(value<='9')return (int)value-48;return (int)value-65+10;}
            class SinkFailure:Exception{
                public SinkFailure():base("original Base32 decoded sink failure",83){}
            }
            struct Sink:ITextWriter{
                private char* output;public int Budget;public nuint Count;public nuint Calls;
                public Sink(char* data,int budget){output=data;Budget=budget;Count=0;Calls=0;}
                public void Write(readonly(char)* input,nuint count){
                    Calls++;for(nuint i=0;i<count;i++){
                        if(Budget==0)throw new SinkFailure();output[Count]=input[i];Count++;Budget--;
                    }
                }
                ~Sink(){destroyed++;}
            }
            bool Failure(int budget,bool large){
                char[404] output;for(nuint i=0;i<404;i++)output[i]='?';Sink sink=new(&output[1],budget);Base32Decoder decoder=new(&sink);int caught=0;
                readonly(char)* oracle="666F6F626172";if(large)oracle="{{Convert.ToHexString(largeBytes)}}";
                try{
                    if(large)decoder.Write("{{large}}",{{large.Length}});
                    else{decoder.Write("MZX",3);decoder.Write("W6YTB",5);decoder.Write("OI======",8);}
                    decoder.Complete();
                }catch(ref SinkFailure error){if(error.GetCode()!=83)return false;caught++;}catch{return false;}
                if(caught!=1||sink.Count!=(nuint)budget||output[0]!='?'||output[(nuint)budget+1]!='?')return false;
                for(nuint i=0;i<(nuint)budget;i++)if((byte)output[i+1]!=(byte)(Digit(oracle[2*i])*16+Digit(oracle[2*i+1])))return false;
                nuint calls=sink.Calls;sink.Budget=1000;
                try{decoder.Write((readonly(char)*)1,0);}catch(ref Exception error){caught++;}
                try{decoder.Write("AAAAAAAA",8);}catch(ref Exception error){caught++;}
                try{decoder.Complete();}catch(ref Exception error){caught++;}
                Base32Decoder duplicate=decoder;try{duplicate.Complete();}catch(ref Exception error){caught++;}
                return caught==5&&sink.Calls==calls&&sink.Count==(nuint)budget;
            }
            int main(){
                for(int budget=0;budget<6;budget++)if(!Failure(budget,false))return 21;
                if(!Failure(0,true)||!Failure(199,true)||!Failure(200,true))return 22;
                readonly(char)* original="MZXW6YTB";readonly(char)* alternate="MZXW6YTA";
                for(nuint carry=1;carry<=7;carry++){
                    char[24] output;for(nuint i=0;i<24;i++)output[i]='?';int beforeDestroyed=destroyed;
                    {
                        Sink sink=new(&output[1],1000);int before=attempts;
                        {
                            char[7] input;for(nuint i=0;i<7;i++)input[i]=original[i];Base32Decoder decoder=new(&sink);decoder.Write(input,carry);
                            Base32Decoder duplicate=decoder;for(nuint i=0;i<7;i++)input[i]='?';Base32Decoder moved=move duplicate;
                            moved.Write(original+carry,8-carry);moved.Complete();decoder.Write(alternate+carry,8-carry);decoder.Complete();
                            if(sink.Count!=10)return 23;for(nuint i=0;i<10;i++)if(output[i+1]!="foobafoob`"[i])return 24;
                            Base32Decoder complete=moved;nuint calls=sink.Calls;complete.Complete();complete.Complete();if(sink.Calls!=calls||attempts!=before)return 25;
                            int rejected=0;try{complete.Write((readonly(char)*)1,0);}catch(ref Exception error){rejected++;}
                            if(rejected!=1||sink.Calls!=calls)return 26;
                        }
                        nuint calls=sink.Calls;{Base32Decoder abandoned=new(&sink);abandoned.Write("M",1);}
                        Base32Decoder empty=new(&sink);empty.Write((readonly(char)*)1,0);empty.Complete();empty.Complete();
                        if(sink.Calls!=calls||destroyed!=beforeDestroyed||output[0]!='?'||output[11]!='?')return 27;
                    }
                    if(destroyed!=beforeDestroyed+1)return 28;
                }
                char[10] output;for(nuint i=0;i<10;i++)output[i]='?';int baseline=attempts;
                {
                    Sink sink=new(&output[1],1000);Base32Decoder decoder=new(&sink);
                    byte[16] text=[{{string.Join(",", Encoding.ASCII.GetBytes(small).Select(value => "(byte)" + value))}}];BinaryBufferReader reader=new(text,16);IByteReader* input=&reader;
                    char[5] scratch;for(nuint i=0;i<5;i++)scratch[i]='!';
                    if(std::IO::CopyTo(input,&decoder,&scratch[1],3)!=16||reader.Position!=16||sink.Count!=6)return 29;
                    decoder.Write((readonly(char)*)1,0);nuint calls=sink.Calls;decoder.Complete();decoder.Complete();
                    if(sink.Calls!=calls||attempts!=baseline||scratch[0]!='!'||scratch[4]!='!'||output[0]!='?'||output[7]!='?')return 30;
                    for(nuint i=0;i<6;i++)if(output[i+1]!="foobar"[i])return 31;
                }
                return 0;
            }
            """, optimization, countAllocations: true);
    }

    private static void Run(string program, int optimization, bool denyAllocation = false, bool countAllocations = false)
    {
        string[] names = ["core/Exception.gf", "core/ITextWriter.gf", "core/Encoding/Base32.gf", "core/Encoding/Base32Decoder.gf",
            "core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/byte_reader.gf",
            "core/Binary/BinaryPrimitives.gf", "core/Binary/BinaryBufferReader.gf", "core/Memory/bytes.gf", "libc/Memory/allocation.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("base32-decoder-tests.gf", program)).ToList();
        if (denyAllocation || countAllocations) sources.Add(new SourceFile("allocation-hooks.gf", $$"""
            int attempts=0;
            namespace Memory{
                namespace Imports{private extern void*? malloc(nuint size);private extern void free(void* storage);}
                public replace void*? Allocate(nuint size){
                    attempts++;{{(denyAllocation ? "return null;" : "return Imports::malloc(size);")}}
                }
                public replace void Free(void* storage)nothrow{Imports::free(storage);}
            }
            """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
