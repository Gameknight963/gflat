using System.Buffers.Binary;
using System.IO.Compression;

namespace gflat.Tests;

public class ChecksumExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolChecksMultipleBinaryFilesAcrossChunksAndContinuesAfterFailure(int optimization)
    {
        string source=Path.Combine(AppContext.BaseDirectory,"Examples/checksum.gf");
        string directory=Path.Combine(Path.GetTempPath(),"gflat-checksums-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile(source,File.ReadAllText(source)));
            string ir=Path.Combine(directory,"checksums.ll"), exe=Path.Combine(directory,"checksums.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,exe,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var paths=new List<string>(); var expected=new List<string>();
            foreach(int length in new[]{0,9,4095,4096,4097,32769})
            {
                byte[] data=Enumerable.Range(0,length).Select(i=>(byte)(i*37)).ToArray();
                string path=Path.Combine(directory,$"input {length}.bin");
                File.WriteAllBytes(path,data); paths.Add(path);
                expected.Add($"{Crc(data):x8} {Adler(data):x8}  {path}");
            }
            var run=NativeToolchain.Run(exe,paths,30000);
            Assert.Equal(0,run.ExitCode);
            Assert.Equal("",run.StandardError);
            Assert.Equal(expected,run.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            string missing=Path.Combine(directory,"missing.bin");
            var mixed=NativeToolchain.Run(exe,[missing,paths[1]],30000);
            Assert.Equal(1,mixed.ExitCode);
            Assert.Equal(new[]{expected[1]},mixed.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal("Unable to read: "+missing,mixed.StandardError.TrimEnd('\r','\n'));
            var usage=NativeToolchain.Run(exe,[],30000);
            Assert.Equal(2,usage.ExitCode);
            Assert.Equal("",usage.StandardOutput);
            Assert.Equal("Usage: checksum <file> [more files...]",usage.StandardError.TrimEnd('\r','\n'));
            foreach(string path in paths) File.Delete(path);
        }
        finally { Directory.Delete(directory,true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailure(int optimization)
    {
        if(!OperatingSystem.IsWindows()&&!OperatingSystem.IsLinux())return;
        string directory=Path.Combine(Path.GetTempPath(),"gflat-checksum-flush-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string example=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/checksum.gf"));
            const string signature="int main(int argc, char** argv)";
            Assert.Contains(signature,example);
            example=example.Replace(signature,"int ExampleMain(int argc, char** argv)");
            string failure=OperatingSystem.IsWindows()?"""
                extern int _fileno(void* stream);
                extern nint _get_osfhandle(int descriptor);
                extern int CloseHandle(void* handle);
                bool BreakOutput(void* stream){
                    // Keep the CRT descriptor valid to avoid its invalid-parameter
                    // handler; only the underlying OS handle becomes unwritable.
                    return CloseHandle((void*)_get_osfhandle(_fileno(stream)))!=0;
                }
                """:"""
                bool BreakOutput(void* stream)=>freopen("/dev/full","wb",stream)!=null;
                """;
            string setup=OperatingSystem.IsWindows()?"""
                if(setvbuf(stream,buffer,0,4096)!=0)return 90;
                if(!BreakOutput(stream))return 91;
                """:"""
                if(!BreakOutput(stream))return 91;
                if(setvbuf(stream,buffer,0,4096)!=0)return 90;
                """;
            string wrapper="""
                extern void* __gflat_stdio_stdout();
                extern int setvbuf(void* stream,char* buffer,int mode,nuint count);
                extern int fclose(void* stream);
                extern void*? freopen(readonly(char)* path,readonly(char)* mode,void* stream);
                int main(int argc,char** argv){
                    void* stream=__gflat_stdio_stdout();char[4096] buffer;
                    if(argc==3){
                        if(freopen(argv[1],"rb",stream)==null)return 92;
                        int status=ExampleMain(2,argv);fclose(stream);return status;
                    }
                    SETUP
                    int status=ExampleMain(argc,argv);
                    // The buffer must remain alive until the stream is closed.
                    // The example's status, not this cleanup, detects the failure.
                    fclose(stream);return status;
                }
                """.Replace("SETUP",setup);
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("checksum-with-output-failure.gf",example+failure+wrapper));
            string ir=Path.Combine(directory,"checksums.ll"),exe=Path.Combine(directory,"checksums.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,exe,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            string input=Path.Combine(directory,"input.bin");
            File.WriteAllBytes(input,[0,1,2,255]);
            foreach(string[] arguments in new[]{new[]{input},new[]{input,"read-only-output"}})
            {
                var run=NativeToolchain.Run(exe,arguments,30000);
                Assert.Equal(1,run.ExitCode);
                Assert.Equal("",run.StandardOutput);
                Assert.Equal("Unable to write checksums",run.StandardError.TrimEnd('\r','\n'));
                Assert.DoesNotContain("Unable to read",run.StandardError);
            }
        }
        finally{Directory.Delete(directory,true);}
    }

    private static uint Crc(byte[] data)
    {
        if(data.Length==0) return 0u;
        using var output=new MemoryStream();
        using(var gzip=new GZipStream(output,CompressionLevel.Fastest,leaveOpen:true)) gzip.Write(data);
        return BinaryPrimitives.ReadUInt32LittleEndian(output.ToArray().AsSpan((int)output.Length-8,4));
    }

    private static uint Adler(byte[] data)
    {
        if(data.Length==0) return 1u;
        using var output=new MemoryStream();
        using(var zlib=new ZLibStream(output,CompressionLevel.Fastest,leaveOpen:true)) zlib.Write(data);
        return BinaryPrimitives.ReadUInt32BigEndian(output.ToArray().AsSpan((int)output.Length-4,4));
    }
}
