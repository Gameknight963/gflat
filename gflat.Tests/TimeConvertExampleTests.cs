using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class TimeConvertExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesDateTimeOffsetUnixAndUtcOraclesAndReportsExactCliErrors(int optimization)
    {
        var cases=new List<(string Mode,string Input,string Expected)>();
        var timestamps=new List<System.DateTimeOffset>{System.DateTimeOffset.MinValue,System.DateTimeOffset.MaxValue,
            System.DateTimeOffset.UnixEpoch.AddTicks(-1),System.DateTimeOffset.UnixEpoch,System.DateTimeOffset.UnixEpoch.AddTicks(1),
            new(new System.DateTime(2024,6,1,12,30,0,DateTimeKind.Unspecified),TimeSpan.FromHours(2)),
            new(new System.DateTime(1970,1,1,0,0,0,DateTimeKind.Unspecified).AddTicks(-1),TimeSpan.FromMinutes(-345))};
        var random=new Random(316987);for(int sample=0;sample<12;sample++){
            long utc=random.NextInt64(System.DateTime.MaxValue.Ticks);int offset=random.Next(-840,841);long wall=utc+offset*600000000L;
            if(wall>=0&&wall<=System.DateTime.MaxValue.Ticks)timestamps.Add(new(new System.DateTime(wall,DateTimeKind.Unspecified),TimeSpan.FromMinutes(offset)));
        }
        foreach(var value in timestamps){string text=Canonical(value);cases.Add(("utc",text,Canonical(value.ToOffset(TimeSpan.Zero))));cases.Add(("seconds",text,Number(value.ToUnixTimeSeconds())));cases.Add(("milliseconds",text,Number(value.ToUnixTimeMilliseconds())));}
        cases.Add(("utc","2000-01-01T00:00:00-00:00","2000-01-01T00:00:00Z"));
        cases.Add(("milliseconds","1969-12-31T23:59:59.9Z","-100"));
        foreach(long value in new[]{-62135596800L,-1L,0L,1L,253402300799L})cases.Add(("from-seconds",Number(value),Canonical(System.DateTimeOffset.FromUnixTimeSeconds(value))));
        foreach(long value in new[]{-62135596800000L,-1L,0L,1L,253402300799999L})cases.Add(("from-milliseconds",Number(value),Canonical(System.DateTimeOffset.FromUnixTimeMilliseconds(value))));
        for(int i=0;i<8;i++){
            long seconds=random.NextInt64(-62135596800L,253402300800L),milliseconds=random.NextInt64(-62135596800000L,253402300800000L);
            cases.Add(("from-seconds",Number(seconds),Canonical(System.DateTimeOffset.FromUnixTimeSeconds(seconds))));
            cases.Add(("from-milliseconds",Number(milliseconds),Canonical(System.DateTimeOffset.FromUnixTimeMilliseconds(milliseconds))));
        }
        foreach(string mode in new[]{"from-seconds","from-milliseconds"}){
            cases.Add((mode,"-000","1970-01-01T00:00:00Z"));cases.Add((mode,"+000","1970-01-01T00:00:00Z"));
        }
        string directory=Path.Combine(Path.GetTempPath(),"gflat-time-convert-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("time-convert.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/time-convert.gf"))));
            string ll=Path.Combine(directory,"tool.ll"),exe=Path.Combine(directory,"time-convert.exe");File.WriteAllText(ll,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ll,exe,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            foreach(var row in cases){var result=await Run(exe,[row.Mode,row.Input]);Assert.True(result.Code==0,$"{row.Mode}: {Encoding.UTF8.GetString(result.Error)}");Assert.Empty(result.Error);Assert.Equal(Bytes(row.Expected),result.Output);}
            foreach(string[] args in new[]{Array.Empty<string>(),new[]{"utc"},new[]{"utc","2000-01-01T00:00:00Z","extra"}})await Error(exe,args,2,"Usage: time-convert <mode> <value>");
            foreach(string mode in new[]{"","UTC","Seconds","from-seconds ","unix","milliseconds "})await Error(exe,[mode,"0"],2,"time-convert: expected utc, seconds, milliseconds, from-seconds, or from-milliseconds");
            foreach(string mode in new[]{"utc","seconds","milliseconds"})foreach(string input in new[]{"","0","2000-01-01T00:00:00","2000-01-01T00:00:00z","2000-01-01T00:00:00+14:01","0001-01-01T00:00:00+00:01","9999-12-31T23:59:59.9999999-00:01"," 2000-01-01T00:00:00Z"})
                await Error(exe,[mode,input],2,"time-convert: expected an explicit-offset timestamp");
            foreach(string mode in new[]{"from-seconds","from-milliseconds"}){
                foreach(string input in new[]{"","+","-"," 1","1 ","1.0","1_0","0x10","++1","1/2","\u0661","9223372036854775808","-9223372036854775809"})await Error(exe,[mode,input],2,"time-convert: expected a signed decimal long");
                long minimum=mode=="from-seconds"?-62135596800L:-62135596800000L,maximum=mode=="from-seconds"?253402300799L:253402300799999L;
                string diagnostic=mode=="from-seconds"?"Unix seconds are outside years 1 through 9999":"Unix milliseconds are outside years 1 through 9999";
                foreach(long input in new[]{minimum-1,maximum+1,long.MinValue,long.MaxValue})await Error(exe,[mode,Number(input)],1,"time-convert: "+diagnostic);
            }
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsBufferedAndImmediateOutputFailures(int optimization)
        =>ExampleOutputFailure.AssertReportedWithArguments("time-convert",optimization,
            "time-convert: Console flush failed","time-convert: Console write failed","utc","2000-01-01T01:00:00+01:00");

    private static string Number(long value)=>value.ToString(CultureInfo.InvariantCulture);
    private static string Canonical(System.DateTimeOffset value){string clock=value.DateTime.ToString(value.Ticks%10000000==0?"yyyy-MM-dd'T'HH:mm:ss":"yyyy-MM-dd'T'HH:mm:ss.fffffff",CultureInfo.InvariantCulture);int minutes=(int)value.Offset.TotalMinutes;if(minutes==0)return clock+"Z";int magnitude=Math.Abs(minutes);return clock+(minutes<0?"-":"+")+(magnitude/60).ToString("D2",CultureInfo.InvariantCulture)+":"+(magnitude%60).ToString("D2",CultureInfo.InvariantCulture);}
    private static byte[] Bytes(string text)=>Encoding.ASCII.GetBytes(text+Environment.NewLine);
    private static async Task Error(string executable,string[] arguments,int code,string message){var result=await Run(executable,arguments);Assert.True(result.Code==code,$"Expected {code}, got {result.Code}: {Encoding.UTF8.GetString(result.Error)}");Assert.Empty(result.Output);Assert.Equal(Bytes(message),result.Error);}
    private static async Task<(int Code,byte[] Output,byte[] Error)> Run(string executable,IEnumerable<string> arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);Assert.True(process.Start());process.StandardInput.Close();
        using var output=new MemoryStream();using var error=new MemoryStream();Task stdout=process.StandardOutput.BaseStream.CopyToAsync(output);Task stderr=process.StandardError.BaseStream.CopyToAsync(error);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await Task.WhenAll(stdout,stderr);throw new TimeoutException("time-convert timed out");}
        await Task.WhenAll(stdout,stderr);return(process.ExitCode,output.ToArray(),error.ToArray());
    }
}
