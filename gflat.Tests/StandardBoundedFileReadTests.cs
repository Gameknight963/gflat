namespace gflat.Tests;

public sealed class StandardBoundedFileReadTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BoundedAndLegacyReadsMatchIndependentBinaryOracle(int optimization)
    {
        await WithDirectory(directory =>
        {
            var random = new Random(197311);
            int[] lengths = [0, 1, 2, 4095, 4096, 4097, 8191, 8192, 8193, 12345,
                .. Enumerable.Range(0, 8).Select(_ => random.Next(1, 16000))];
            var inputs = new List<(string Path, byte[] Bytes)>();
            string manifest = Path.Combine(directory, "cases.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(lengths.Sum(length => length == 0 ? 3 : 5));
                foreach (int length in lengths)
                {
                    byte[] bytes = new byte[length]; random.NextBytes(bytes);
                    byte[] special = [0, 255, 128, 13, 10, 239, 187, 191];
                    special.AsSpan(0, Math.Min(length, special.Length)).CopyTo(bytes);
                    string path = Path.Combine(directory, $"input-{inputs.Count}.bin");
                    File.WriteAllBytes(path, bytes); inputs.Add((path, bytes));
                    ulong[] limits = length == 0 ? [0, 1, ulong.MaxValue] :
                        [0, (ulong)length - 1, (ulong)length, (ulong)length + 1, ulong.MaxValue];
                    foreach (ulong limit in limits)
                    {
                        byte[] name = System.Text.Encoding.UTF8.GetBytes(path);
                        writer.Write(name.Length); writer.Write(name); writer.Write(length);
                        writer.Write(limit); writer.Write(bytes);
                    }
                }
            }
            Run(directory, """
                using std;
                String ReadPath(File* input) {
                    nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);
                    String result=new(); result.Resize(count); Span<char> data=result.AsSpan();
                    input.ReadExactly(data.Data!,count); return move result;
                }
                bool LimitError(Exception* error) {
                    return CString::Equals(error.GetMessage(),"File exceeds maximum byte count");
                }
                int main(int argc,char** argv) {
                    File cases=File::OpenRead(argv[1]); int rows=FileBinary::ReadInt32LittleEndian(&cases);
                    for(int row=0;row<rows;row++) {
                        String path=ReadPath(&cases); nuint length=(nuint)FileBinary::ReadInt32LittleEndian(&cases);
                        nuint limit=(nuint)FileBinary::ReadUInt64LittleEndian(&cases);
                        Vector<byte> expected=new();
                        for(nuint i=0;i<length;i++) expected.Add((byte)cases.ReadByte());
                        if(length>limit) {
                            bool caught=false;
                            try { String result=FileText::ReadAll(path.Data,limit); return 1; }
                            catch(EndOfStreamException* error) { return 2; }
                            catch(IOException* error) { caught=LimitError(error); }
                            if(!caught)return 3;
                            caught=false;
                            try { Vector<byte> result=FileBytes::ReadAll(path.Data,limit); return 4; }
                            catch(EndOfStreamException* error) { return 5; }
                            catch(IOException* error) { caught=LimitError(error); }
                            if(!caught)return 6;
                        } else {
                            String text=FileText::ReadAll(path.Data,limit);
                            String legacyText=FileText::ReadAll(path.Data);
                            Vector<byte> bytes=FileBytes::ReadAll(path.Data,limit);
                            Vector<byte> legacyBytes=FileBytes::ReadAll(path.Data);
                            if(text.Length!=length||legacyText.Length!=length||bytes.Count!=length||legacyBytes.Count!=length)return 7;
                            if(text.Length>limit||bytes.Count>limit)return 8;
                            for(nuint i=0;i<length;i++) {
                                if((byte)text.Data[i]!=expected[i]||(byte)legacyText.Data[i]!=expected[i]||bytes[i]!=expected[i]||legacyBytes[i]!=expected[i])return 9;
                            }
                            if(length!=0) {
                                bytes[0]=(byte)(bytes[0]+1);
                                if(legacyBytes[0]!=expected[0]||(byte)text.Data[0]!=expected[0]||(byte)legacyText.Data[0]!=expected[0])return 10;
                                Span<char> mutableText=text.AsSpan(); mutableText[0]=(char)(expected[0]+1);
                                if((byte)legacyText.Data[0]!=expected[0])return 11;
                            }
                        }
                    }
                    cases.Close();return 0;
                }
                """, [manifest], optimization);
            foreach (var input in inputs) Assert.Equal(input.Bytes, File.ReadAllBytes(input.Path));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task LimitFailuresPreserveDestinationsReleasePartialResultsAndCloseFiles(int optimization)
    {
        await WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "input.bin"), empty = Path.Combine(directory, "empty.bin");
            byte[] source = Enumerable.Range(0, 9000).Select(i => (byte)i).ToArray();
            File.WriteAllBytes(path, source); File.WriteAllBytes(empty, []);
            Run(directory, Counters + """
                int main(int argc,char** argv) {
                    {
                        String target=String::From("sentinel"); Vector<byte> bytes=new(); bytes.Add(73);
                        nuint[4] limits;limits[0]=0;limits[1]=4096;limits[2]=4097;limits[3]=8192;
                        for(int i=0;i<4;i++) {
                            int live=allocated-released;bool caught=false;
                            try { target=FileText::ReadAll(argv[1],limits[i]);return 1; }
                            catch(IOException* error) {caught=CString::Equals(error.GetMessage(),"File exceeds maximum byte count");}
                            if(!caught||!target.Equals("sentinel")||allocated-released!=live)return 2;
                            caught=false;
                            try { bytes=FileBytes::ReadAll(argv[1],limits[i]);return 3; }
                            catch(IOException* error) {caught=CString::Equals(error.GetMessage(),"File exceeds maximum byte count");}
                            if(!caught||bytes.Count!=1||bytes[0]!=73||allocated-released!=live)return 4;
                        }
                        int before=allocated;failAfter=0;
                        {String text=FileText::ReadAll(argv[2],0);Vector<byte> value=FileBytes::ReadAll(argv[2],0);
                         if(text.Length!=0||value.Count!=0||allocated!=before)return 5;}
                        failAfter=-1;
                        try {target=FileText::ReadAll(argv[3],0);return 6;}catch(IOException* error){}
                        try {bytes=FileBytes::ReadAll(argv[3],0);return 7;}catch(IOException* error){}
                        if(!target.Equals("sentinel")||bytes.Count!=1||bytes[0]!=73)return 8;
                        // Remove while alive: Windows CRT rejects removal of an open input stream.
                        File::Remove(argv[1]);File::Remove(argv[2]);
                    }
                    if(allocated!=released)return 9;return 0;
                }
                """, [path, empty, Path.Combine(directory, "missing.bin")], optimization);
            Assert.False(File.Exists(path)); Assert.False(File.Exists(empty));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryMeasuredGrowthFailurePreservesDestinationsAndClosesInput(int optimization)
    {
        await WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "input.bin");
            File.WriteAllBytes(path, Enumerable.Range(0, 9000).Select(i => (byte)i).ToArray());
            Run(directory, Counters + """
                int main(int argc,char** argv) {
                    {
                        String target=String::From("sentinel");Vector<byte> bytes=new();bytes.Add(73);
                        for(int kind=0;kind<2;kind++) {
                            int before=allocated;int points=0;
                            // Successful probes contain only result allocations, no exception payload.
                            if(kind==0){String proof=FileText::ReadAll(argv[1],9000);points=allocated-before;}
                            else {Vector<byte> proof=FileBytes::ReadAll(argv[1],9000);points=allocated-before;}
                            if(points<2)return 1;
                            for(int point=0;point<points;point++) {
                                int live=allocated-released;int deniedBefore=denied;bool caught=false;failAfter=point;
                                try {
                                    if(kind==0)target=FileText::ReadAll(argv[1],9000);
                                    else bytes=FileBytes::ReadAll(argv[1],9000);
                                    return 2;
                                } catch(BadAllocException* error) {caught=true;}
                                // Payload allocations may also be denied and use the EH reserve.
                                failAfter=-1;
                                if(!caught||denied==deniedBefore||allocated-released!=live)return 3;
                                if(!target.Equals("sentinel")||bytes.Count!=1||bytes[0]!=73)return 4;
                            }
                        }
                        // A normal read after all failed reads also checks source contents.
                        String text=FileText::ReadAll(argv[1],9000);Vector<byte> data=FileBytes::ReadAll(argv[1],9000);
                        if(text.Length!=9000||data.Count!=9000)return 5;
                        for(nuint i=0;i<9000;i++)if((byte)text.Data[i]!=(byte)i||data[i]!=(byte)i)return 6;
                        File::Remove(argv[1]);
                    }
                    if(allocated!=released)return 7;return 0;
                }
                """, [path], optimization);
            Assert.False(File.Exists(path));
        });
    }

    private const string Counters = """
        using std;
        extern void*? malloc(nuint size);extern void free(void* value);
        int allocated=0;int released=0;int denied=0;int failAfter=-1;
        namespace Memory {
            public replace void*? Allocate(nuint size) {
                if(failAfter==0){denied++;return null;}if(failAfter>0)failAfter--;
                void*? value=malloc(size);if(value!=null)allocated++;return value;
            }
            public replace void Free(void* value) nothrow {released++;free(value);}
        }
        """;

    private static async Task WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-bounded-file-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static void Run(string directory, string program, string[] arguments, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("bounded-file-read.gf", program));
        string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "main.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        var result = NativeToolchain.Run(executable, arguments, 30000);
        Assert.True(result.ExitCode == 0, $"Bounded file read exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
