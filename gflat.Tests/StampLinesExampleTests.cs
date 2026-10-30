using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class StampLinesExampleTests
{
    private const string FailureVariable="GFLAT_TEST_STAMP_LINES_FAIL_SECOND";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolSamplesOnlyStartedLinesAndKeepsPriorOutputWhenTheClockFails(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-stamp-lines-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string actualMain=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/stamp-lines.gf"));
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("stamp-lines.gf",actualMain))
                .Append(new SourceFile("stamp-lines-test-clock.gf","""
                    int clockCalls=0;
                    static class TestClock{
                        public static Standard::Time::DateTime GetUtcNow(){
                            clockCalls++;
                            if(clockCalls==2&&Standard::Environment::GetVariable("GFLAT_TEST_STAMP_LINES_FAIL_SECOND")!=null)
                                throw new Exception("test clock failure",71);
                            return Standard::Time::DateTime::FromTicks(621355968000000000l+(long)(clockCalls-1)*10000000l);
                        }
                    }
                    alias NativeClock=TestClock;
                    """));
            string ll=Path.Combine(directory,"tool.ll"),exe=Path.Combine(directory,"stamp-lines.exe");
            File.WriteAllText(ll,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compilation=NativeToolchain.Compile(ll,exe,optimizationLevel:optimization);
            Assert.True(compilation.ExitCode==0,compilation.StandardError);
            string[] cases=["","\n","\n\n","one","one\n","one\ntwo","one\ntwo\n","a\r\n\r\nb\r\nlast","a\rb\nc",
                new string('a',4095)+"\n"+new string('b',4097)+"\r\nlast",
                new string('x',4096)+"\n\nend\n"];
            foreach(string input in cases){
                var result=await Run(exe,input,[],false,true);
                Assert.True(result.Code==0,result.Error);Assert.Equal("",result.Error);
                Assert.Equal(Encoding.ASCII.GetBytes(Expected(input)),result.Output);
            }
            // The actual main must reject arguments without attempting a stdin read.
            var usage=await Run(exe,"",["unexpected"],false,false);
            Assert.Equal(2,usage.Code);Assert.Empty(usage.Output);Assert.Equal("Usage: stamp-lines"+Environment.NewLine,usage.Error);
            foreach(string firstLine in new[]{"first\n","\n",new string('a',4097)+"\r\n"}){
                var failed=await Run(exe,firstLine+"second\nthird\n",[],true,true);
                Assert.Equal(1,failed.Code);Assert.Equal("Unable to stamp input"+Environment.NewLine,failed.Error);
                Assert.Equal(Encoding.ASCII.GetBytes(Expected(firstLine)),failed.Output);
            }
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static string Expected(string physicalInput)
    {
        // Only ASCII test inputs are used. Match CRT CRLF input translation on Windows;
        // bare CR is retained, and the library itself keys line starts only on LF.
        string input=OperatingSystem.IsWindows()?physicalInput.Replace("\r\n","\n",StringComparison.Ordinal):physicalInput;
        var output=new StringBuilder();bool start=true;int calls=0;
        foreach(char value in input){
            if(start){var timestamp=System.DateTime.UnixEpoch.AddSeconds(calls++);output.Append('[').Append(timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss",CultureInfo.InvariantCulture)).Append("Z] ");}
            output.Append(value);start=value=='\n';
        }
        string logical=output.ToString();return OperatingSystem.IsWindows()?logical.Replace("\n","\r\n",StringComparison.Ordinal):logical;
    }

    private static async Task<(int Code,byte[] Output,string Error)> Run(string executable,string input,string[] arguments,bool failSecond,bool closeInput)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment.Remove(FailureVariable);if(failSecond)process.StartInfo.Environment[FailureVariable]="1";
        Assert.True(process.Start());using var output=new MemoryStream();
        Task outputTask=process.StandardOutput.BaseStream.CopyToAsync(output);Task<string> errorTask=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{
            await process.StandardInput.BaseStream.WriteAsync(Encoding.ASCII.GetBytes(input),timeout.Token);
            if(closeInput)process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw new TimeoutException("stamp-lines timed out");}
        await outputTask;return(process.ExitCode,output.ToArray(),await errorTask);
    }
}
