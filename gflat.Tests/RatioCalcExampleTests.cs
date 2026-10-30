using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class RatioCalcExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesExactFractionOraclesAndRejectsMalformedOperands(int optimization)
    {
        var cases=new List<(string Left,string Operator,string Right,string Expected)>();
        void Pair(string left,string right){
            Fraction a=ReadValid(left),b=ReadValid(right);
            foreach(string operation in new[]{"+","-","*","/"}){
                if(operation=="/"&&b.N.IsZero)continue;
                Fraction expected=operation switch{
                    "+"=>Canonical(a.N*b.D+b.N*a.D,a.D*b.D),"-"=>Canonical(a.N*b.D-b.N*a.D,a.D*b.D),
                    "*"=>Canonical(a.N*b.N,a.D*b.D),_=>Canonical(a.N*b.D,a.D*b.N)};
                cases.Add((left,operation,right,expected.Text));
            }
        }
        foreach(var pair in new (string Left,string Right)[]{("0","0"),("0","-7"),("1/6","1/3"),("-2/3","5/-7"),
            ("-9223372036854775808/-1","1/9223372036854775808"),("4/2","2"),("0/-999","1/-2"),("+000/-001","-00/+7"),
            ("0006/-0008","+010/+015")})Pair(pair.Left,pair.Right);
        BigInteger common=BigInteger.Pow(10,900)+17;
        Pair(Text(common*84)+"/"+Text(-common*126),Text(common*25)+"/"+Text(common*35));
        Pair(Text(BigInteger.Pow(10,300)+123)+"/3","-1/"+Text(BigInteger.Pow(10,140)+17));
        var random=new Random(951731);for(int sample=0;sample<8;sample++){
            byte[] an=new byte[12],ad=new byte[10],bn=new byte[12],bd=new byte[10];random.NextBytes(an);random.NextBytes(ad);random.NextBytes(bn);random.NextBytes(bd);
            BigInteger firstDen=new(ad),secondDen=new(bd);if(firstDen.IsZero)firstDen=1;if(secondDen.IsZero)secondDen=1;
            Pair(Text(new BigInteger(an))+"/"+Text(firstDen),Text(new BigInteger(bn))+"/"+Text(secondDen));
        }
        string directory=Path.Combine(Path.GetTempPath(),"gflat-ratio-calc-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("ratio-calc.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/ratio-calc.gf"))));
            string ll=Path.Combine(directory,"tool.ll"),exe=Path.Combine(directory,"ratio-calc.exe");File.WriteAllText(ll,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ll,exe,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            foreach(var item in cases){
                var result=await Run(exe,[item.Left,item.Operator,item.Right]);Assert.True(result.Code==0,$"ratio-calc {item.Operator} exit {result.Code}: {Encoding.UTF8.GetString(result.Error)}");
                Assert.Empty(result.Error);Assert.Equal(Bytes(item.Expected),result.Output);
            }
            foreach(string[] arguments in new[]{Array.Empty<string>(),new[]{"1"},new[]{"1","+"},new[]{"1","+","2","extra"}})
                await Error(exe,arguments,2,"Usage: ratio-calc <left> <operator> <right>");
            foreach(string operation in new[]{"","%","gcd","^"," +","/ ","**"})await Error(exe,["1",operation,"2"],2,"ratio-calc: expected operator +, -, *, or /");
            foreach(string input in new[]{"","+","-","/","1/","/2","1/2/3","1//2","1.0","1/2.0"," 1","1 ","1 /2","1/ 2","1_0","0x10","++1","1/-","1/+","1/0","0/-000","1/+000","\u0661/2"}){
                await Error(exe,[input,"+","1"],2,"ratio-calc: operands must be integers or numerator/denominator with a nonzero denominator");
                await Error(exe,["1","+",input],2,"ratio-calc: operands must be integers or numerator/denominator with a nonzero denominator");
            }
            foreach(string zero in new[]{"0","-00","+000","0/7","-000/-999"})
                await Error(exe,["2/3","/",zero],1,"ratio-calc: Rational divisor is zero");
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsBufferedAndImmediateOutputFailures(int optimization)
        =>ExampleOutputFailure.AssertReportedWithArguments("ratio-calc",optimization,
            "ratio-calc: Console flush failed","ratio-calc: Console write failed","1/6","+","1/3");

    private readonly record struct Fraction(BigInteger N,BigInteger D){public string Text=>D.IsOne?RatioCalcExampleTests.Text(N):RatioCalcExampleTests.Text(N)+"/"+RatioCalcExampleTests.Text(D);}
    private static Fraction Canonical(BigInteger numerator,BigInteger denominator){if(denominator.Sign<0){numerator=-numerator;denominator=-denominator;}BigInteger gcd=BigInteger.GreatestCommonDivisor(numerator,denominator);return new(numerator/gcd,denominator/gcd);}
    private static Fraction ReadValid(string text){string[] parts=text.Split('/');return Canonical(BigInteger.Parse(parts[0],CultureInfo.InvariantCulture),parts.Length==1?BigInteger.One:BigInteger.Parse(parts[1],CultureInfo.InvariantCulture));}
    private static string Text(BigInteger value)=>value.ToString(CultureInfo.InvariantCulture);
    private static byte[] Bytes(string text)=>Encoding.ASCII.GetBytes(text+Environment.NewLine);
    private static async Task Error(string executable,string[] arguments,int code,string message){var result=await Run(executable,arguments);Assert.True(result.Code==code,$"Expected {code}, got {result.Code}: {Encoding.UTF8.GetString(result.Error)}");Assert.Empty(result.Output);Assert.Equal(Bytes(message),result.Error);}
    private static async Task<(int Code,byte[] Output,byte[] Error)> Run(string executable,IEnumerable<string> arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);Assert.True(process.Start());process.StandardInput.Close();
        using var output=new MemoryStream();using var error=new MemoryStream();Task stdout=process.StandardOutput.BaseStream.CopyToAsync(output);Task stderr=process.StandardError.BaseStream.CopyToAsync(error);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await Task.WhenAll(stdout,stderr);throw new TimeoutException("ratio-calc timed out");}
        await Task.WhenAll(stdout,stderr);return(process.ExitCode,output.ToArray(),error.ToArray());
    }
}
