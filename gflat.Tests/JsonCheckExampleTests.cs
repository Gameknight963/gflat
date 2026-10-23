using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public sealed class JsonCheckExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolValidatesWholeFilesAndReportsErrorsWithoutChangingInput(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("json-check.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/json-check.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"json-check.exe"),input=Path.Combine(directory,"document with spaces.json");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            (string Text,int Tokens,int Depth)[] valid=[("true",1,0),(" \r\n123.00e-99\t",1,0),("\"x\\u0000\"",1,0),
                ("[]",2,1),("{}",2,1),("{\"x\":[{},false],\"x\":null}",10,3),
                (new string('[',300)+"0"+new string(']',300),601,300)];
            foreach(var value in valid)
            {
                byte[] original=Encoding.UTF8.GetBytes(value.Text);File.WriteAllBytes(input,original);
                var result=await Run(executable,[input]);
                Assert.Equal(0,result.ExitCode);Assert.Empty(result.Error);
                Assert.Equal($"Tokens: {value.Tokens}{Environment.NewLine}Maximum nesting: {value.Depth}{Environment.NewLine}",result.Output);
                Assert.Equal(original,File.ReadAllBytes(input));
            }
            (byte[] Bytes,int Offset,string Reason)[] invalid=[([],0,"Incomplete"),("[] x"u8.ToArray(),3,"Trailing"),
                ("{\"a\":1,}"u8.ToArray(),7,"property name"),([34,0xff,34],0,"Invalid JSON string"),
                ("[0"u8.ToArray(),2,"Incomplete"),("\"\\uD800\""u8.ToArray(),0,"Invalid JSON string")];
            foreach(var value in invalid)
            {
                File.WriteAllBytes(input,value.Bytes);var result=await Run(executable,[input]);
                Assert.Equal(1,result.ExitCode);Assert.Empty(result.Output);
                Assert.Contains(input+": byte "+value.Offset+": ",result.Error);
                Assert.Contains(value.Reason,result.Error);Assert.Equal(value.Bytes,File.ReadAllBytes(input));
            }
            string missing=Path.Combine(directory,"missing.json");var absent=await Run(executable,[missing]);
            Assert.Equal(1,absent.ExitCode);Assert.Empty(absent.Output);Assert.Contains(missing,absent.Error);
            Assert.DoesNotContain(": byte ",absent.Error);
            foreach(string[] args in new string[][]{[],[input,input]})
            {
                var usage=await Run(executable,args);Assert.Equal(2,usage.ExitCode);Assert.Empty(usage.Output);Assert.Contains("Usage:",usage.Error);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static async Task<(int ExitCode,string Output,string Error)> Run(string executable,string[] arguments)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();Assert.Fail("json-check timed out.");}
        return(process.ExitCode,await output,await error);
    }
}
