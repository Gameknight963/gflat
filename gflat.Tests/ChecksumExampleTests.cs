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
        => ExampleOutputFailure.AssertReported("checksum", optimization, "Unable to write checksums");

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
