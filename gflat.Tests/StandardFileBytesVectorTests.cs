namespace gflat.Tests;

public sealed class StandardFileBytesVectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BorrowedVectorsRoundTripAppendAndHandleEmptyFileOperations(int optimization)
    {
        await WithDirectory(directory =>
        {
            string executable = Compile(directory, """
                using std; using std::Collections;
                int main(int argc,char** argv){
                    Vector<byte> bytes=FileBytes::ReadAll(argv[1]);nuint count=bytes.Count;nuint capacity=bytes.Capacity;
                    FileBytes::WriteAll(argv[2],ref bytes);FileBytes::AppendAll(argv[3],ref bytes);
                    if(bytes.Count!=count||bytes.Capacity!=capacity)return 1;
                    File input=File::OpenRead(argv[1]);for(nuint i=0;i<count;i++)if(bytes[i]!=(byte)input.ReadByte())return 2;
                    if(input.ReadByte()!=-1)return 3;input.Close();
                    Vector<byte> empty=new();
                    FileBytes::WriteAll(argv[4],ref empty);FileBytes::AppendAll(argv[5],ref empty);
                    FileBytes::AppendAll(argv[3],ref empty);
                    if(empty.Count!=0||empty.Capacity!=0)return 4;
                    File truncated=File::OpenRead(argv[4]);if(truncated.ReadByte()!=-1)return 5;truncated.Close();
                    File created=File::OpenRead(argv[5]);if(created.ReadByte()!=-1)return 6;created.Close();
                    // Remove while alive proves both successful owned output files have closed.
                    File::Remove(argv[4]);File::Remove(argv[5]);return 0;
                }
                """, optimization);
            var random=new Random(7164096);
            foreach(int length in new[]{0,1,4095,4096,4097,12005})
            {
                byte[] expected=new byte[length];random.NextBytes(expected);
                byte[] special=[0,255,128,13,10,239,187,191];special.AsSpan(0,Math.Min(length,special.Length)).CopyTo(expected);
                string input=Path.Combine(directory,"input.bin"),written=Path.Combine(directory,"written.bin");
                string appended=Path.Combine(directory,"appended.bin"),truncated=Path.Combine(directory,"truncated.bin"),created=Path.Combine(directory,"created.bin");
                File.WriteAllBytes(input,expected);File.WriteAllBytes(written,[7,8,9]);
                byte[] prefix=[9,0,255];File.WriteAllBytes(appended,prefix);File.WriteAllBytes(truncated,[1,2,3]);
                Run(executable,[input,written,appended,truncated,created]);
                Assert.Equal(expected,File.ReadAllBytes(written));Assert.Equal(prefix.Concat(expected),File.ReadAllBytes(appended));
                Assert.Equal(expected,File.ReadAllBytes(input));Assert.False(File.Exists(truncated));Assert.False(File.Exists(created));
            }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task VectorWritesBorrowImmutableOwnersWithoutGflatAllocation(int optimization)
    {
        await WithDirectory(directory =>
        {
            string executable=Compile(directory,"""
                using std; using std::Collections;
                extern void*? malloc(nuint size);extern void free(void* value);
                int allocations=0;int frees=0;bool deny=false;
                namespace Memory{
                    public replace void*? Allocate(nuint size){allocations++;if(deny)return null;return malloc(size);}
                    public replace void Free(void* value)nothrow{frees++;free(value);}
                }
                int main(int argc,char** argv){
                    {
                        Vector<byte> source=new();for(nuint i=0;i<256;i++)source.Add((byte)i);
                        immutable(Vector<byte>) bytes=move source;nuint capacity=bytes.Capacity;
                        Span<immutable(byte)> view=bytes.AsImmutable();immutable(byte)* address=view.Data!;
                        Vector<byte> empty=new();int before=allocations;int freedBefore=frees;deny=true;
                        FileBytes::WriteAll(argv[1],ref bytes);FileBytes::AppendAll(argv[1],ref bytes);
                        FileBytes::AppendAll(argv[1],ref empty);FileBytes::WriteAll(argv[2],ref empty);
                        if(allocations!=before||frees!=freedBefore||bytes.Count!=256||bytes.Capacity!=capacity)return 1;
                        Span<immutable(byte)> after=bytes.AsImmutable();if(after.Data!=address)return 2;
                        for(nuint i=0;i<256;i++)if(after[i]!=(byte)i)return 3;
                        if(empty.Count!=0||empty.Capacity!=0)return 4;
                        deny=false;
                    }
                    if(allocations!=frees)return 5;return 0;
                }
                """,optimization);
            string written=Path.Combine(directory,"written.bin"),empty=Path.Combine(directory,"empty.bin");
            Run(executable,[written,empty]);
            byte[] bytes=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();
            Assert.Equal(bytes.Concat(bytes),File.ReadAllBytes(written));Assert.Empty(File.ReadAllBytes(empty));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task OpenAndDeferredCloseFailuresPreserveBorrowedVector(int optimization)
    {
        await WithDirectory(directory =>
        {
            string executable=Compile(directory,"""
                using std; using std::Collections;
                int main(int argc,char** argv){
                    Vector<byte> bytes=new();bytes.Add(0);bytes.Add(255);bytes.Add(128);nuint capacity=bytes.Capacity;
                    bool caught=false;try{FileBytes::WriteAll(argv[1],ref bytes);return 1;}catch(IOException* error){caught=true;}
                    if(!caught)return 2;caught=false;
                    try{FileBytes::AppendAll(argv[1],ref bytes);return 3;}catch(IOException* error){caught=true;}
                    if(!caught)return 4;
                    Vector<byte> empty=new();
                    try{FileBytes::WriteAll(argv[1],ref empty);return 5;}catch(IOException* error){}
                    try{FileBytes::AppendAll(argv[1],ref empty);return 6;}catch(IOException* error){}
                    if(argc>2){
                        // Linux /dev/full defers a small buffered write error until Close.
                        try{FileBytes::WriteAll(argv[2],ref bytes);return 7;}catch(IOException* error){}
                        try{FileBytes::AppendAll(argv[2],ref bytes);return 8;}catch(IOException* error){}
                    }
                    if(bytes.Count!=3||bytes.Capacity!=capacity||bytes[0]!=0||bytes[1]!=255||bytes[2]!=128)return 9;
                    if(empty.Count!=0||empty.Capacity!=0)return 10;return 0;
                }
                """,optimization);
            string missing=Path.Combine(directory,"missing-parent","output.bin");
            Run(executable,OperatingSystem.IsLinux()?[missing,"/dev/full"]:[missing]);
            Assert.False(File.Exists(missing));
        });
    }

    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-file-bytes-vector-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static string Compile(string directory,string program,int optimization)
    {
        // Every native fixture selects only binary owning dependencies: no String or GC implementation.
        string[] names=["core/IOException.gf","core/EndOfStreamException.gf","core/Collections/IEnumerable.gf",
            "core/Collections/Enumerable.gf","core/Span.gf","core/Collections/Vector.gf","libc/File.gf","libc/File/FileBytes.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std",name))))
            .Append(new SourceFile("file-bytes-vector-test.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
        Assert.True(compiled.ExitCode==0,compiled.StandardError);return executable;
    }
    private static void Run(string executable,string[] arguments)
    {
        var result=NativeToolchain.Run(executable,arguments,30000);
        Assert.True(result.ExitCode==0,$"FileBytes vector exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
