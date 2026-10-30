using System.Diagnostics;
using System.Globalization;

namespace gflat.Tests;

public sealed class FileCompareExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesFirstDifferenceOracleWithoutApplicationAllocation(int optimization)
        => await WithDirectory(async directory =>
        {
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("file-compare.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/file-compare.gf"))))
                .Append(new SourceFile("no-allocations.gf","""
                    replace void*? Memory::Allocate(nuint size)=>null;
                    replace void Memory::Free(void* storage)nothrow{}
                    """));
            string ir=Path.Combine(directory,"file-compare.ll"),executable=Path.Combine(directory,"file-compare.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            string left=Path.Combine(directory,"left input.bin"),right=Path.Combine(directory,"right input.bin");
            void Check(byte[] first,byte[] second)
            {
                File.WriteAllBytes(left,first);File.WriteAllBytes(right,second);
                var result=NativeToolchain.Run(executable,[left,right],30000);
                var expected=Reference(first,second);
                Assert.True(result.ExitCode==expected.Status,$"Expected {expected.Status}, got {result.ExitCode}: {result.StandardError}");
                Assert.Equal(expected.Output,result.StandardOutput.Replace("\r\n","\n"));Assert.Empty(result.StandardError);
                Assert.Equal(first,File.ReadAllBytes(left));Assert.Equal(second,File.ReadAllBytes(right));
            }
            var random=new Random(647291);
            Check([],[]);
            foreach(int length in new[]{1,2,4095,4096,4097,4098,8193}){
                byte[] bytes=new byte[length];random.NextBytes(bytes);
                byte[] special=[0,255,128,13,10,26];special.AsSpan(0,Math.Min(length,special.Length)).CopyTo(bytes);
                Check(bytes,bytes);Check(bytes[..^1],bytes);Check(bytes,bytes[..^1]);
            }
            byte[] corpus=new byte[12289];random.NextBytes(corpus);
            foreach(int at in new[]{0,1,4095,4096,4097,8192,12288}){
                byte[] changed=(byte[])corpus.Clone();changed[at]^=255;
                Check(corpus,changed);Check(changed,corpus);
            }
            byte[] earlier=(byte[])corpus.Clone();earlier[1]^=255;
            Check(corpus,earlier[..4097]);Check(earlier[..4097],corpus);
            byte[] finalLeft=File.ReadAllBytes(left),finalRight=File.ReadAllBytes(right);
            var same=NativeToolchain.Run(executable,[left,left],30000);
            Assert.Equal(0,same.ExitCode);Assert.Empty(same.StandardOutput);Assert.Empty(same.StandardError);
            string missing=Path.Combine(directory,"missing.bin");
            foreach(string[] args in new[]{new[]{missing,right},new[]{left,missing},new[]{"",right},new[]{left,""}}){
                var failed=NativeToolchain.Run(executable,args,30000);
                Assert.Equal(2,failed.ExitCode);Assert.Empty(failed.StandardOutput);
                Assert.StartsWith("Unable to compare files: ",failed.StandardError);
                Assert.Contains("File open failed",failed.StandardError,StringComparison.Ordinal);
            }
            foreach(string[] args in new[]{Array.Empty<string>(),new[]{missing},new[]{missing,missing,"extra"}}){
                var usage=NativeToolchain.Run(executable,args,30000);
                Assert.Equal(2,usage.ExitCode);Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: file-compare <left> <right>\n",usage.StandardError.Replace("\r\n","\n"));
            }
            byte[] literalLeft=[0,128],literalRight=[0,255];
            File.WriteAllBytes(Path.Combine(directory,"--left"),literalLeft);File.WriteAllBytes(Path.Combine(directory,"--right"),literalRight);
            var literal=await RunInDirectory(executable,directory,["--left","--right"]);
            Assert.Equal(1,literal.ExitCode);Assert.Empty(literal.StandardError);
            Assert.Equal(Reference(literalLeft,literalRight).Output,literal.StandardOutput.Replace("\r\n","\n"));
            Assert.Equal(literalLeft,File.ReadAllBytes(Path.Combine(directory,"--left")));
            Assert.Equal(literalRight,File.ReadAllBytes(Path.Combine(directory,"--right")));
            Assert.Equal(finalLeft,File.ReadAllBytes(left));Assert.Equal(finalRight,File.ReadAllBytes(right));
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolReportsImmediateAndBufferedOutputFailuresAsErrors(int optimization)
        => await WithDirectory(directory=>
        {
            string left=Path.Combine(directory,"left.bin"),right=Path.Combine(directory,"right.bin");
            byte[] first=[0],second=[255];File.WriteAllBytes(left,first);File.WriteAllBytes(right,second);
            string prefix=$"Unable to compare files: {left} / {right}: ";
            ExampleOutputFailure.AssertReportedWithArguments("file-compare",optimization,
                prefix+"Console flush failed",prefix+"Console write failed",2,left,right);
            Assert.Equal(first,File.ReadAllBytes(left));Assert.Equal(second,File.ReadAllBytes(right));
            return Task.CompletedTask;
        });

    private static (int Status,string Output) Reference(byte[] left,byte[] right)
    {
        int at=0;while(at<left.Length&&at<right.Length&&left[at]==right[at])at++;
        if(at==left.Length&&at==right.Length)return(0,"");
        string kind=at<left.Length&&at<right.Length?"byte":"length";
        string first=at==left.Length?"EOF":left[at].ToString("x2",CultureInfo.InvariantCulture);
        string second=at==right.Length?"EOF":right[at].ToString("x2",CultureInfo.InvariantCulture);
        return(1,$"{kind} {at.ToString("x16",CultureInfo.InvariantCulture)} {first} {second}\n");
    }

    private static async Task WithDirectory(Func<string,Task> body)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-file-compare-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{await body(directory);}catch(Exception error){error.Data["FileCompareExampleDirectory"]=directory;throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static async Task<ProcessResult> RunInDirectory(string executable,string directory,string[] arguments)
    {
        var start=new ProcessStartInfo(executable){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();Assert.Fail("File comparison timed out.");}
        return new ProcessResult(process.ExitCode,await output,await error);
    }
}
