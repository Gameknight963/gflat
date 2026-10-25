using System.Buffers.Binary;
using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;

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
            var paths=new List<string>(); var expected=new List<string>(); var shaExpected=new List<string>(); var crc32cExpected=new List<string>();
            foreach(int length in new[]{0,9,55,56,63,64,65,127,128,129,4095,4096,4097,8191,8192,8193,32769})
            {
                byte[] data=length==9 ? System.Text.Encoding.ASCII.GetBytes("123456789") : Enumerable.Range(0,length).Select(i=>(byte)(i*37)).ToArray();
                string path=Path.Combine(directory,$"input {length}.bin");
                File.WriteAllBytes(path,data); paths.Add(path);
                expected.Add($"{Crc(data):x8} {Adler(data):x8}  {path}");
                shaExpected.Add($"{Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()}  {path}");
                crc32cExpected.Add($"{CRC32CReference.Compute(data):x8}  {path}");
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
            var sha=NativeToolchain.Run(exe,["--sha256",..paths],30000);
            Assert.Equal(0,sha.ExitCode);
            Assert.Equal("",sha.StandardError);
            Assert.Equal(shaExpected,sha.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            var shaMixed=NativeToolchain.Run(exe,["--sha256",missing,paths[1]],30000);
            Assert.Equal(1,shaMixed.ExitCode);
            Assert.Equal(new[]{shaExpected[1]},shaMixed.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal("Unable to read: "+missing,shaMixed.StandardError.TrimEnd('\r','\n'));
            var crc32c=NativeToolchain.Run(exe,["--crc32c",..paths],30000);
            Assert.Equal(0,crc32c.ExitCode);
            Assert.Equal("",crc32c.StandardError);
            Assert.Equal(crc32cExpected,crc32c.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            var crc32cMixed=NativeToolchain.Run(exe,["--crc32c",missing,paths[1]],30000);
            Assert.Equal(1,crc32cMixed.ExitCode);
            Assert.Equal(new[]{crc32cExpected[1]},crc32cMixed.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal("Unable to read: "+missing,crc32cMixed.StandardError.TrimEnd('\r','\n'));
            foreach(string[] arguments in new string[][] { [], ["--sha256"], ["--crc32c"], ["--"], ["--sha256","--"], ["--crc32c","--"],
                ["--unknown",paths[0]], ["--sha256","--sha256",paths[0]], ["--crc32c","--crc32c",paths[0]],
                ["--sha256","--crc32c",paths[0]], ["--crc32c","--sha256",paths[0]] })
            {
                var usage=NativeToolchain.Run(exe,arguments,30000);
                Assert.Equal(2,usage.ExitCode);
                Assert.Equal("",usage.StandardOutput);
                Assert.Equal("Usage: checksum [--sha256 | --crc32c] [--] <file> [more files...]",usage.StandardError.TrimEnd('\r','\n'));
            }
            // Only the child changes directory: filenames that look like options
            // must be literal after -- or after the first ordinary path.
            byte[] optionData=[0,255,128,0,1];
            foreach(string optionName in new[]{"--sha256","--crc32c"})
            {
                File.WriteAllBytes(Path.Combine(directory,optionName),optionData);
                string optionSha=Convert.ToHexString(SHA256.HashData(optionData)).ToLowerInvariant()+"  "+optionName;
                string optionDefault=$"{Crc(optionData):x8} {Adler(optionData):x8}  {optionName}";
                string optionCrc32c=$"{CRC32CReference.Compute(optionData):x8}  {optionName}";
                var literal=RunInDirectory(exe,directory,["--sha256","--",optionName]);
                Assert.Equal(0,literal.ExitCode); Assert.Equal("",literal.StandardError);
                Assert.Equal(optionSha,literal.StandardOutput.TrimEnd('\r','\n'));
                var literalCrc32c=RunInDirectory(exe,directory,["--crc32c","--",optionName]);
                Assert.Equal(0,literalCrc32c.ExitCode); Assert.Equal("",literalCrc32c.StandardError);
                Assert.Equal(optionCrc32c,literalCrc32c.StandardOutput.TrimEnd('\r','\n'));
                var literalDefault=RunInDirectory(exe,directory,["--",optionName]);
                Assert.Equal(0,literalDefault.ExitCode); Assert.Equal("",literalDefault.StandardError);
                Assert.Equal(optionDefault,literalDefault.StandardOutput.TrimEnd('\r','\n'));
                var later=RunInDirectory(exe,directory,[paths[0],optionName]);
                Assert.Equal(0,later.ExitCode); Assert.Equal("",later.StandardError);
                Assert.Equal(new[]{expected[0],optionDefault},later.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
                var laterCrc32c=RunInDirectory(exe,directory,["--crc32c",paths[0],optionName]);
                Assert.Equal(0,laterCrc32c.ExitCode); Assert.Equal("",laterCrc32c.StandardError);
                Assert.Equal(new[]{crc32cExpected[0],optionCrc32c},laterCrc32c.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries));
            }
            foreach(string path in paths) File.Delete(path);
        }
        finally { Directory.Delete(directory,true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReported("checksum", optimization, "Unable to write checksums");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Sha256ReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReportedWithLeadingArguments("checksum", optimization, "Unable to write checksums", "--sha256");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Crc32cReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReportedWithLeadingArguments("checksum", optimization, "Unable to write checksums", "--crc32c");

    private static ProcessResult RunInDirectory(string executable,string directory,string[] arguments)
    {
        using var process=new Process { StartInfo=new ProcessStartInfo(executable) {
            WorkingDirectory=directory, UseShellExecute=false, CreateNoWindow=true,
            RedirectStandardOutput=true, RedirectStandardError=true
        } };
        foreach(string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout=process.StandardOutput.ReadToEndAsync();
        var stderr=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree:true); process.WaitForExit();
            throw new TimeoutException("Checksum example did not exit");
        }
        return new(process.ExitCode,stdout.GetAwaiter().GetResult(),stderr.GetAwaiter().GetResult());
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
