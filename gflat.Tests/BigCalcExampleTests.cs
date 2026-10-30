using System.Diagnostics;
using System.Globalization;
using System.Text;
using Integer=System.Numerics.BigInteger;

namespace gflat.Tests;

// Compile the actual Examples/big-calc.gf;
// do not inject an alternate main, parser or allocator into the oracle fixture.
public sealed class BigCalcExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesBigIntegerArithmeticAndReportsCliErrors(int optimization)
    {
        var cases=new List<(string Left,string Operator,string Right,Integer Expected)>();
        void Pair(Integer left,Integer right){
            foreach(string operation in new[]{"+","-","*","/","%","gcd","lcm"}){
                if(right.IsZero&&operation is "/" or "%")continue;
                Integer expected=operation switch{"+"=>left+right,"-"=>left-right,"*"=>left*right,"/"=>left/right,"%"=>left%right,"gcd"=>Integer.GreatestCommonDivisor(left,right),_=>left.IsZero||right.IsZero?Integer.Zero:Integer.Abs(left/Integer.GreatestCommonDivisor(left,right)*right)};
                cases.Add((left.ToString(CultureInfo.InvariantCulture),operation,right.ToString(CultureInfo.InvariantCulture),expected));
            }
        }
        foreach(var pair in new (Integer,Integer)[]{(0,0),(0,-7),(1,1),(-1,1),(-7,3),(7,-3),(-7,-3),
            (long.MinValue,long.MaxValue),(Integer.Parse("18446744073709551616",CultureInfo.InvariantCulture),1)})Pair(pair.Item1,pair.Item2);
        Pair(Integer.Pow(10,1800)+123,Integer.Pow(10,700)+17);
        Integer common=Integer.Pow(10,3000)+17;Pair(-common*84,common*126);
        var random=new Random(430961);
        for(int sample=0;sample<18;sample++){
            byte[] a=new byte[random.Next(1,193)],b=new byte[random.Next(1,193)];random.NextBytes(a);random.NextBytes(b);
            Integer right=new(b);if(right.IsZero)right=1;Pair(new Integer(a),right);
        }
        cases.AddRange([("+000","+","-00",Integer.Zero),("-000","gcd","+000",Integer.Zero),("-000","lcm","+000",Integer.Zero),("-00084","lcm","+00126",new Integer(252)),
            ("+00123","/","-0007",new Integer(-17)),("-00123","%","+0007",new Integer(-4)),("000","*","-0001",Integer.Zero)]);

        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("big-calc.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/big-calc.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_big_calc_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"big-calc.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            foreach(var item in cases){
                var result=await Run(executable,[item.Left,item.Operator,item.Right]);
                Assert.True(result.Code==0,$"big-calc '{item.Operator}' exit {result.Code}: {Encoding.UTF8.GetString(result.Error)}");
                Assert.Empty(result.Error);Assert.Equal(Bytes(item.Expected.ToString(CultureInfo.InvariantCulture)),result.Output);
            }
            foreach(string[] arguments in new[]{Array.Empty<string>(),new[]{"1"},new[]{"1","+"},new[]{"1","+","2","extra"}})
                await Error(executable,arguments,2,"Usage: big-calc <left> <operator> <right>");
            foreach(string operation in new[]{"","^","GCD","Gcd","LCM","Lcm","lcm "," +","gcd ","/ ","**","÷"})
                await Error(executable,["1",operation,"2"],2,"big-calc: expected operator +, -, *, /, %, gcd, or lcm");
            foreach(string invalid in new[]{"","+","-"," 1","1 ","\t1","1\n","1_0","0x10","1.0","1a","١","++1","--1","+-1","-+1"}){
                await Error(executable,[invalid,"+","1"],2,"big-calc: operands must be decimal integers");
                await Error(executable,["1","+",invalid],2,"big-calc: operands must be decimal integers");
            }
            foreach(string operation in new[]{"/","%"})foreach(string left in new[]{"0","1","-123456789012345678901234567890"})foreach(string zero in new[]{"0","-00","+000"})
                await Error(executable,[left,operation,zero],1,"big-calc: BigInteger divisor is zero");
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsBufferedAndImmediateOutputFailures(int optimization)
        =>ExampleOutputFailure.AssertReportedWithArguments("big-calc",optimization,
            "big-calc: Console flush failed","big-calc: Console write failed","40","+","2");

    private static byte[] Bytes(string line)=>Encoding.ASCII.GetBytes(line+(OperatingSystem.IsWindows()?"\r\n":"\n"));
    private static async Task Error(string executable,string[] arguments,int expectedCode,string diagnostic)
    {
        var result=await Run(executable,arguments);
        Assert.True(result.Code==expectedCode,$"Expected exit{expectedCode}, received {result.Code}: {Encoding.UTF8.GetString(result.Error)}");
        Assert.Empty(result.Output);Assert.Equal(Bytes(diagnostic),result.Error);
    }
    private static async Task<(int Code,byte[] Output,byte[] Error)> Run(string executable,IEnumerable<string> arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());process.StandardInput.Close();
        using var output=new MemoryStream();using var error=new MemoryStream();
        Task stdout=process.StandardOutput.BaseStream.CopyToAsync(output);Task stderr=process.StandardError.BaseStream.CopyToAsync(error);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();await Task.WhenAll(stdout,stderr);throw new TimeoutException("big-calc timed out.");}
        await Task.WhenAll(stdout,stderr);return(process.ExitCode,output.ToArray(),error.ToArray());
    }
}
