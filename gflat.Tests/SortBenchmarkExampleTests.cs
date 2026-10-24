using System.Globalization;

namespace gflat.Tests;

public sealed class SortBenchmarkExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualSortToolReportsConservedChecksumAndNonnegativeDurations(int optimization)
    {
        bool windows=OperatingSystem.IsWindows();
        Assert.True(windows||OperatingSystem.IsLinux(),"The sort benchmark has Windows x64 and Linux LP64 clock providers.");
        string platform=Path.Combine(AppContext.BaseDirectory,"Platform",windows?"windows":"linux",windows?"PerformanceCounter.gf":"MonotonicClock.gf");
        string example=Path.Combine(AppContext.BaseDirectory,"Examples/sort-bench");
        string alias=Path.Combine(example,windows?"Clock.Windows.gf":"Clock.Linux.gf");
        string main=Path.Combine(example,"main.gf");
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Concat([platform,alias,main]).Select(path=>new SourceFile(path,File.ReadAllText(path)));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_sort_bench_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"sort-bench.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            (string Argument,int Count)[] valid=[("0",0),("1",1),("17",17),("257",257),("+17",17)];
            foreach(var sample in valid){
                var result=NativeToolchain.Run(executable,[sample.Argument]);
                Assert.True(result.ExitCode==0,$"count {sample.Argument}: exit {result.ExitCode}: {result.StandardError}");
                Assert.Equal("",result.StandardError);
                string[] lines=Normalize(result.StandardOutput).Split('\n');
                Assert.Equal(5,lines.Length);Assert.Equal("",lines[4]);
                Assert.Equal("Count: "+sample.Count.ToString(CultureInfo.InvariantCulture),lines[0]);
                Assert.Equal("Checksum: "+ExpectedChecksum(sample.Count).ToString(CultureInfo.InvariantCulture),lines[1]);
                AssertDuration(lines[2],"Sort: ");AssertDuration(lines[3],"StableSort: ");
            }
            string[][] invalid=[["-1"],["1000001"],["18446744073709551616"],["bad"],["17suffix"],[""],["1","2"]];
            foreach(var arguments in invalid){
                var result=NativeToolchain.Run(executable,arguments);
                Assert.Equal(2,result.ExitCode);Assert.Equal("",result.StandardOutput);
                Assert.Equal("Usage: sort-bench [count: 0..1000000]\n",Normalize(result.StandardError));
            }
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static void AssertDuration(string line,string prefix)
    {
        Assert.StartsWith(prefix,line);
        Assert.True(TimeSpan.TryParseExact(line[prefix.Length..],"c",CultureInfo.InvariantCulture,out var duration),line);
        Assert.True(duration>=TimeSpan.Zero,line);
    }

    // SplitMix64 specification, independent of the tool's sort/verification code.
    // NextInt consumes the upper 32 bits, interpreted as a signed integer.
    private static long ExpectedChecksum(int count)
    {
        ulong state=17;long checksum=0;
        for(int i=0;i<count;i++){
            state=unchecked(state+0x9e3779b97f4a7c15UL);ulong value=state;
            value=unchecked((value^(value>>30))*0xbf58476d1ce4e5b9UL);
            value=unchecked((value^(value>>27))*0x94d049bb133111ebUL);
            value^=value>>31;
            checksum+=unchecked((int)(uint)(value>>32));
        }
        return checksum;
    }
    private static string Normalize(string value)=>value.Replace("\r\n","\n");
}
