using TextEncoding = System.Text.Encoding;

namespace gflat.Tests.Std.Codecs;

public sealed class HexDecoderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task HostOraclePartitionsAndBinaryFilePipelineUseBoundedStorageWithoutAllocation(int optimization)
        => await WithDirectory(directory =>
        {
            string manifest = Path.Combine(directory, "corpus.bin");
            int cases = 0;
            var random = new Random(75193);
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                Add([], [], 0);
                for (int length = 1; length <= 3; length++)
                {
                    byte[] bytes = Enumerable.Range(0, length).Select(value => (byte)(value * 73 + length)).ToArray();
                    int count = length * 2;
                    for (int mask = 0; mask < 1 << (count - 1); mask++)
                    {
                        var chunks = new List<int>(); int start = 0;
                        for (int index = 1; index < count; index++)
                            if ((mask & (1 << (index - 1))) != 0) { chunks.Add(index - start); start = index; }
                        chunks.Add(count - start);
                        for (int mode = 0; mode < 3; mode++) Add(bytes, chunks.ToArray(), mode);
                    }
                }
                foreach (int length in new[] { 255, 256, 257, 511, 512, 513, 4095, 4096, 4097, 16385 })
                {
                    byte[] bytes = new byte[length]; random.NextBytes(bytes);
                    for (int mode = 0; mode < 3; mode++)
                    {
                        Add(bytes, [length * 2], mode);
                        Add(bytes, Enumerable.Repeat(1, length * 2).ToArray(), mode);
                        Add(bytes, Partition(length * 2, 257), mode);
                    }
                }
                Add(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray(), [1, 255, 256], 2);
                for (int sample = 0; sample < 12; sample++)
                {
                    byte[] bytes = new byte[random.Next(1, 8193)]; random.NextBytes(bytes);
                    Add(bytes, Partition(bytes.Length * 2, random.Next(2, 258)), sample % 3);
                }
                void Add(byte[] bytes, int[] chunks, int mode)
                {
                    string upper = Convert.ToHexString(bytes);
                    char[] text = upper.ToCharArray();
                    for (int index = 0; index < text.Length; index++)
                        if (mode == 1 || mode == 2 && index % 2 == 0) text[index] = char.ToLowerInvariant(text[index]);
                    Assert.Equal(text.Length, chunks.Sum());
                    writer.Write(text.Length); writer.Write(chunks.Length);
                    foreach (int chunk in chunks) writer.Write(chunk);
                    writer.Write(TextEncoding.ASCII.GetBytes(text)); writer.Write(bytes.Length); writer.Write(bytes); cases++;
                }
            }
            byte[] original = new byte[65537]; random.NextBytes(original);
            string input = Path.Combine(directory, "input.hex"), output = Path.Combine(directory, "output.bin");
            byte[] encoded = TextEncoding.ASCII.GetBytes(Convert.ToHexString(original).ToLowerInvariant());
            File.WriteAllBytes(input, encoded);
            Run(directory, $$"""
                using Standard; using Standard::IO; using Standard::Encoding;
                int attempts=0;
                namespace Memory {
                    public replace void*? Allocate(nuint count){attempts++;return null;}
                    public replace void Free(void* value)nothrow{}
                }
                struct Sink:ITextWriter {
                    private char* data;public nuint Count;public int Calls;
                    public Sink(char* storage){data=storage;Count=0;Calls=0;}
                    public void Write(readonly(char)* text,nuint count){
                        if(count==0||count>256||Count+count>16385)throw new Exception("Invalid block");
                        Calls++;for(nuint i=0;i<count;i++)data[Count+i]=text[i];Count+=count;
                    }
                }
                int main(int argc,char** argv){
                    File fixture=File::OpenRead(argv[1]);
                    char[32770] text;char[16387] output;char[16385] expected;int[32770] chunks;
                    for(int sample=0;sample<{{cases}};sample++){
                        int size=FileBinary::ReadInt32LittleEndian(&fixture);
                        int count=FileBinary::ReadInt32LittleEndian(&fixture);
                        if(size<0||size>32770||count<0||count>32770)return 1;
                        for(int i=0;i<count;i++)chunks[i]=FileBinary::ReadInt32LittleEndian(&fixture);
                        fixture.ReadExactly(text,(nuint)size);
                        int wanted=FileBinary::ReadInt32LittleEndian(&fixture);
                        fixture.ReadExactly(expected,(nuint)wanted);
                        output[0]='!';output[(nuint)wanted+1]='!';Sink sink=new(&output[1]);HexDecoder decoder=new(&sink);
                        decoder.Write(((nuint)1).reinterpret<readonly(char)*>(),0);nuint position=0;
                        for(int chunk=0;chunk<count;chunk++){
                            decoder.Write((readonly(char)*)text+position,(nuint)chunks[chunk]);
                            for(nuint i=0;i<(nuint)chunks[chunk];i++)text[position+i]='?';
                            HexDecoder copied=decoder;decoder=move copied;
                            position+=(nuint)chunks[chunk];decoder.Write(((nuint)1).reinterpret<readonly(char)*>(),0);
                        }
                        int calls=sink.Calls;decoder.Complete();decoder.Complete();
                        if(sink.Count!=(nuint)wanted||sink.Calls!=calls||output[0]!='!'||output[(nuint)wanted+1]!='!')return 2;
                        for(nuint i=0;i<(nuint)wanted;i++)if(output[i+1]!=expected[i])return 3;
                    }
                    fixture.Close();
                    File source=File::OpenRead(argv[2]);File destination=File::OpenWrite(argv[3]);
                    HexDecoder decoder=new(&destination);char[257] buffer;
                    if(source.CopyTo(&decoder,buffer,257)!={{encoded.Length}})return 4;
                    decoder.Complete();decoder.Complete();
                    if(!source.IsOpen||!destination.IsOpen)return 5;
                    source.Close();destination.Close();if(attempts!=0)return 6;return 0;
                }
                """, optimization, [manifest, input, output]);
            Assert.Equal(original, File.ReadAllBytes(output)); Assert.Equal(encoded, File.ReadAllBytes(input));
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AllByteClassesOddFinishAndIndependentCopiesRespectStateContracts(int optimization)
        => await WithDirectory(directory =>
        {
            int[] values = Enumerable.Range(0, 256).Select(value =>
            {
                try { return (int)Convert.FromHexString(new string([(char)value, '0']))[0]; }
                catch (FormatException) { return -1; }
            }).ToArray();
            Run(directory, $$"""
                using Standard;using Standard::Encoding;
                struct Sink:ITextWriter {
                    public int Count;public int Calls;public int Last;
                    public Sink(){Count=0;Calls=0;Last=-1;}
                    public void Write(readonly(char)* text,nuint count){if(count==0)throw new Exception("Unexpected empty block");Calls++;Count+=(int)count;Last=(int)(byte)text[count-1];}
                }
                bool Rejected(ref HexDecoder decoder,ref Sink sink){
                    int calls=sink.Calls;int count=sink.Count;int caught=0;
                    try{decoder.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(ref Exception error){caught++;}
                    try{decoder.Write("00",2);}catch(ref Exception error){caught++;}
                    try{decoder.Complete();}catch(ref Exception error){caught++;}
                    HexDecoder copy=decoder;try{copy.Complete();}catch(ref Exception error){caught++;}
                    return caught==4&&sink.Calls==calls&&sink.Count==count;
                }
                int main(){
                    int[256] expected=[{{string.Join(',', values)}}];
                    for(int candidate=0;candidate<256;candidate++)for(int place=0;place<2;place++){
                        Sink sink=new();HexDecoder decoder=new(&sink);char[2] pair=['0','0'];pair[place]=(char)candidate;bool caught=false;
                        try{decoder.Write(pair,2);decoder.Complete();}catch(ref Exception error){caught=true;}
                        if(expected[candidate]<0){if(!caught||sink.Calls!=0)return 1;}
                        else{
                            int wanted=expected[candidate];if(place==1)wanted=wanted/16;
                            if(caught||sink.Count!=1||sink.Last!=wanted)return 2;
                        }
                    }
                    Sink sink=new();HexDecoder empty=new(&sink);empty.Write(((nuint)1).reinterpret<readonly(char)*>(),0);empty.Complete();empty.Complete();
                    if(sink.Calls!=0)return 3;
                    try{empty.Write(((nuint)1).reinterpret<readonly(char)*>(),0);return 4;}catch(ref Exception error){}
                    empty.Complete();
                    for(int mode=0;mode<3;mode++){
                        Sink failedSink=new();HexDecoder failed=new(&failedSink);
                        try{
                            if(mode==0){failed.Write("f",1);failed.Write(((nuint)1).reinterpret<readonly(char)*>(),0);failed.Complete();}
                            if(mode==1){failed.Write("g",1);failed.Write("0",1);}
                            if(mode==2){char[516] text;for(nuint i=0;i<514;i++)text[i]='0';text[514]='g';text[515]='0';failed.Write(text,516);}
                            return 5;
                        }catch(ref Exception error){}
                        if(!Rejected(ref failed,ref failedSink))return 6;
                    }
                    HexDecoder first=new(&sink);char[1] carry=['a'];first.Write(carry,1);carry[0]='?';
                    HexDecoder copy=first;HexDecoder moved=move copy;
                    first.Write("0",1);moved.Write("f",1);first.Complete();moved.Complete();
                    if(sink.Count!=2||sink.Last!=175)return 7;
                    HexDecoder branch=new(&sink);branch.Write("c",1);HexDecoder sibling=branch;
                    try{branch.Write("x",1);return 8;}catch(ref Exception error){}
                    sibling.Write("0",1);sibling.Complete();if(sink.Count!=3||sink.Last!=192)return 9;
                    int calls=sink.Calls;{HexDecoder abandoned=new(&sink);abandoned.Write("e",1);}
                    if(sink.Calls!=calls)return 10;sink.Write("!",1);if(sink.Last!=33)return 11;
                    return 0;
                }
                """, optimization);
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PartialSinkErrorsPropagateUnchangedAndPermanentlyLatchFailure(int optimization)
        => await WithDirectory(directory => Run(directory, """
            using Standard;using Standard::Encoding;
            class SinkFailure:Exception{public SinkFailure():base("partial sink",77){}}
            struct Sink:ITextWriter {
                public int Budget;public int Count;public int Calls;
                public Sink(int budget){Budget=budget;Count=0;Calls=0;}
                public void Write(readonly(char)* text,nuint count){
                    if(count==0||count>256)throw new Exception("Invalid block");Calls++;
                    for(nuint i=0;i<count;i++){if(Budget==0)throw new SinkFailure();Budget--;Count++;}
                }
            }
            int main(){
                int[7] budgets=[0,1,255,256,257,511,512];char[1026] input;for(nuint i=0;i<1026;i++)input[i]='0';
                for(nuint sample=0;sample<7;sample++){
                    Sink sink=new(budgets[sample]);HexDecoder decoder=new(&sink);bool caught=false;
                    try{decoder.Write(input,1026);}catch(ref SinkFailure error){if(error.GetCode()!=77)return 1;caught=true;}
                    catch{return 2;}
                    if(!caught||sink.Count!=budgets[sample])return 3;
                    int calls=sink.Calls;int count=sink.Count;sink.Budget=10000;int rejected=0;
                    try{decoder.Write(((nuint)1).reinterpret<readonly(char)*>(),0);}catch(ref Exception error){rejected++;}
                    try{decoder.Complete();}catch(ref Exception error){rejected++;}
                    HexDecoder copy=decoder;try{copy.Write("00",2);}catch(ref Exception error){rejected++;}
                    if(rejected!=3||sink.Calls!=calls||sink.Count!=count)return 4;
                }
                return 0;
            }
            """, optimization));

    [Fact]
    public void SelectiveCodecSourcesDoNotRequireStringOrCollector()
    {
        string[] names = ["Encoding/Hex.gf", "Encoding/HexDecoder.gf", "ITextWriter.gf", "IFormattable.gf", "Exception.gf"];
        var sources = names.Select(name => Path.Combine(AppContext.BaseDirectory, "Standard/core", name))
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("selective-hex.gf", """
                using Standard::Encoding;
                namespace Memory { public void*? Allocate(nuint count)=>null;public void Free(void* value)nothrow{} }
                struct Sink:ITextWriter { public void Write(readonly(char)* data,nuint count){} }
                void Decode(Sink* sink,readonly(char)* text,nuint count){HexDecoder decoder=new(sink);decoder.Write(text,count);decoder.Complete();}
                """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        // Successful emission with only these sources proves no String definition is required.
        Assert.NotEmpty(ir);
        Assert.DoesNotContain("TracingCollector", ir);
    }

    private static int[] Partition(int count, int chunk)
        => Enumerable.Range(0, (count + chunk - 1) / chunk).Select(index => Math.Min(chunk, count - index * chunk)).ToArray();

    private static async Task WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-hex-decoder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        catch (Exception error) { error.Data["HexDecoderDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void Run(string directory, string program, int optimization, string[]? arguments = null)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("hex-decoder.gf", program));
        string input = Path.Combine(directory, "main.ll"), output = Path.Combine(directory, "main.exe");
        File.WriteAllText(input, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(output, arguments ?? [], 30000);
        Assert.True(result.ExitCode == 0, $"O{optimization}, exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
