namespace gflat.Tests;

public sealed class StandardFileVarIntTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalReadWriteBytesMatchIndependentOracleWithoutReadAhead(int optimization)
    {
        var random = new Random(62183);
        var narrow = new SortedSet<uint> { 0, 1, 127, 128, 129, uint.MaxValue };
        var wide = new SortedSet<ulong> { 0, 1, 127, 128, 129, ulong.MaxValue };
        for (int bits = 7; bits < 32; bits += 7) {
            uint boundary = 1u << bits;
            narrow.UnionWith([boundary - 1, boundary, boundary + 1]);
        }
        for (int bits = 7; bits < 64; bits += 7) {
            ulong boundary = 1ul << bits;
            wide.UnionWith([boundary - 1, boundary, boundary + 1]);
        }
        for (int i = 0; i < 80; i++) {
            narrow.Add((uint)random.NextInt64(1L << 32));
            wide.Add(BitConverter.ToUInt64(RandomBytes(random, 8)));
        }
        using var expected = new MemoryStream();
        foreach (ulong value in narrow.Select(v => (ulong)v).Concat(wide)) {
            expected.Write(Encode(value)); expected.WriteByte(0x5a);
        }
        WithDirectory(directory => {
            string input = Path.Combine(directory, "input.bin"), output = Path.Combine(directory, "output.bin");
            byte[] oracle = expected.ToArray(); File.WriteAllBytes(input, oracle);
            string program = $$"""
                using std; using std::IO; int attempts=0;
                extern void free(void* value);
                namespace Memory {
                    public replace void*? Allocate(nuint bytes){attempts++;return null;}
                    public replace void Free(void* value) nothrow{free(value);}
                }
                int main(int argc,char** argv){
                    const uint[{{narrow.Count}}] narrow=[{{string.Join(",",narrow.Select(v=>v+"u"))}}];
                    const ulong[{{wide.Count}}] wide=[{{string.Join(",",wide.Select(v=>v+"ul"))}}];
                    File input=File::OpenRead(argv[1]);File output=File::OpenWrite(argv[2]);
                    for(nuint i=0;i<{{narrow.Count}};i++){
                        if(FileVarInt::ReadUInt32(&input)!=narrow[i]||input.ReadByte()!=90)return 1;
                        FileVarInt::WriteUInt32(&output,narrow[i]);output.WriteByte((char)90);
                    }
                    for(nuint i=0;i<{{wide.Count}};i++){
                        if(FileVarInt::ReadUInt64(&input)!=wide[i]||input.ReadByte()!=90)return 2;
                        FileVarInt::WriteUInt64(&output,wide[i]);output.WriteByte((char)90);
                    }
                    if(!input.IsOpen||!output.IsOpen||input.EndOfFile)return 3;
                    if(input.ReadByte()!=-1||!input.EndOfFile)return 4;
                    input.Close();output.Close();if(attempts!=0)return 5;return 0;
                }
                """;
            Run(Compile(directory, program, optimization), [input, output]);
            Assert.Equal(oracle, File.ReadAllBytes(output)); Assert.Equal(oracle, File.ReadAllBytes(input));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidAndTruncatedReadsPreserveTheFirstUnconsumedByte(int optimization)
        => WithDirectory(directory => {
            string executable = Compile(directory, """
                using std; using std::IO;
                int main(int argc,char** argv){
                    File input=File::OpenRead(argv[1]);int caught=0;
                    try{
                        if(argv[2][0]=='4')FileVarInt::ReadUInt32(&input);
                        else FileVarInt::ReadUInt64(&input);
                    }catch(EndOfStreamException* error){caught=1;}
                    catch(IOException* error){caught=2;}
                    int expected=2;if(argv[3][0]=='e')expected=1;
                    if(caught!=expected||!input.IsOpen)return 1;
                    if(expected==1){if(!input.EndOfFile||input.ReadByte()!=-1)return 2;}
                    else{
                        int marker=90;if(argv[4][0]=='h')marker=128;
                        if(input.EndOfFile||input.ReadByte()!=marker||input.EndOfFile)return 3;
                        if(input.ReadByte()!=-1||!input.EndOfFile)return 4;
                    }
                    input.Close();return 0;
                }
                """, optimization);
            string path = Path.Combine(directory, "malformed.bin");
            foreach (int width in new[] { 32, 64 }) {
                int maximum = width == 32 ? 5 : 10;
                for (int length = 0; length < maximum; length++) {
                    byte[] truncated = Enumerable.Repeat((byte)0x80, length).ToArray();
                    File.WriteAllBytes(path, truncated);
                    Run(executable, [path, width == 32 ? "4" : "8", "e", "l"]);
                    Assert.Equal(truncated, File.ReadAllBytes(path));
                }
                List<byte[]> malformed = [[0x80,0], [0x81,0], [0xff,0]];
                byte[] overflow = Enumerable.Repeat((byte)0x80, maximum).ToArray();
                foreach (byte final in new byte[] { (byte)(width == 32 ? 16 : 2), 0x80, 0xff }) {
                    byte[] bytes = (byte[])overflow.Clone(); bytes[^1] = final; malformed.Add(bytes);
                }
                foreach (byte[] bytes in malformed)
                foreach (byte marker in new byte[] { 0x5a,0x80 }) {
                    byte[] input = [..bytes, marker]; File.WriteAllBytes(path, input);
                    Run(executable, [path, width == 32 ? "4" : "8", "i", marker == 128 ? "h" : "l"]);
                    Assert.Equal(input, File.ReadAllBytes(path));
                }
            }
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClosedAndWrongModeFailuresKeepBorrowedFileOwnership(int optimization)
        => WithDirectory(directory => {
            string executable = Compile(directory, """
                using std; using std::IO;
                int main(int argc,char** argv){
                    File closed=new();int caught=0;
                    try{FileVarInt::ReadUInt32(&closed);}catch(EndOfStreamException* error){return 1;}catch(IOException* error){caught++;}
                    try{FileVarInt::ReadUInt64(&closed);}catch(EndOfStreamException* error){return 2;}catch(IOException* error){caught++;}
                    try{FileVarInt::WriteUInt32(&closed,1u);}catch(IOException* error){caught++;}
                    try{FileVarInt::WriteUInt64(&closed,1ul);}catch(IOException* error){caught++;}
                    if(caught!=4||closed.IsOpen)return 3;
                    File output=File::OpenWrite(argv[2]);
                    try{FileVarInt::ReadUInt64(&output);}catch(EndOfStreamException* error){return 4;}catch(IOException* error){caught++;}
                    if(caught!=5||!output.IsOpen||output.EndOfFile)return 5;
                    output.ClearError();FileVarInt::WriteUInt32(&output,300u);output.Close();
                    File input=File::OpenRead(argv[1]);
                    try{FileVarInt::WriteUInt64(&input,1ul);}catch(IOException* error){caught++;}
                    if(caught!=6||!input.IsOpen)return 6;
                    input.ClearError();if(FileVarInt::ReadUInt32(&input)!=300u)return 7;input.Close();
                    return 0;
                }
                """, optimization);
            string input = Path.Combine(directory, "input.bin"), output = Path.Combine(directory, "output.bin");
            byte[] oracle = Encode(300); File.WriteAllBytes(input, oracle);
            Run(executable, [input, output]); Assert.Equal(oracle, File.ReadAllBytes(input)); Assert.Equal(oracle, File.ReadAllBytes(output));
        });

    private static byte[] Encode(ulong value)
    {
        var result = new List<byte>();
        do {
            byte next = (byte)(value & 127); value >>= 7;
            result.Add(value == 0 ? next : (byte)(next | 128));
        } while (value != 0);
        return result.ToArray();
    }
    private static byte[] RandomBytes(Random random,int count){byte[] bytes=new byte[count];random.NextBytes(bytes);return bytes;}
    private static void WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-file-varint-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{Directory.Delete(directory,true);}
    }
    private static string Compile(string directory,string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("file-varint-test.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var result=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
        Assert.True(result.ExitCode==0,result.StandardError);return executable;
    }
    private static void Run(string executable,string[] arguments)
    {
        var result=NativeToolchain.Run(executable,arguments,30000);
        Assert.True(result.ExitCode==0,$"File varint exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        Assert.Empty(result.StandardOutput);Assert.Empty(result.StandardError);
    }
}
