using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class QuoteLinesExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolProducesIndependentJsonLinesAndReportsInvalidInput(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("quote-lines.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/quote-lines.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat-quote-lines-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"quote-lines.exe"),input=Path.Combine(directory,"input file.txt");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            (string Input,string[] Lines)[] cases=[
                ("",[]),("\n",[""]),("a\n\n",["a",""]),("a\r\nb",["a","b"]),
                ("bare\rreturn\nfinal\r",["bare\rreturn","final\r"]),
                ("\"quoted\"\\path\t\0\b\f\n",["\"quoted\"\\path\t\0\b\f"]),
                ("\ufeff\u20ac\ud83d\ude00\uffff\n",["\ufeff\u20ac\ud83d\ude00\uffff"]),
                (new string('x',10000)+"\nshort\n",[new string('x',10000),"short"])
            ];
            foreach(var item in cases)
            {
                byte[] original=Encoding.UTF8.GetBytes(item.Input);
                File.WriteAllBytes(input,original);
                var result=await Run(executable,[input]);
                Assert.Equal(0,result.ExitCode);Assert.Empty(result.Error);
                Assert.Equal(original,File.ReadAllBytes(input));
                string output=new UTF8Encoding(false,true).GetString(result.Output);
                string[] lines=output.Split('\n');
                Assert.Equal(item.Lines.Length,output.Length==0 ? 0 : lines.Length-1);
                if(output.Length!=0)Assert.EndsWith("\n",output);
                for(int i=0;i<item.Lines.Length;i++)
                {
                    using var json=JsonDocument.Parse(lines[i]);
                    Assert.Equal(item.Lines[i],json.RootElement.GetString());
                }
            }
            foreach(byte[] bytes in new byte[][] { [0xc0,0x80], [65,0xed,0xa0,0x80], [0xf0,0x90,0x80] })
            {
                File.WriteAllBytes(input,bytes);
                var invalid=await Run(executable,[input]);
                Assert.Equal(1,invalid.ExitCode);Assert.Empty(invalid.Output);Assert.Contains("Invalid UTF-8",invalid.Error);
                Assert.Equal(bytes,File.ReadAllBytes(input));
            }
            File.WriteAllBytes(input,[65,10,0xff]);
            var prefix=await Run(executable,[input]);
            Assert.Equal(1,prefix.ExitCode);
            using(var first=JsonDocument.Parse(Encoding.UTF8.GetString(prefix.Output).Trim()))Assert.Equal("A",first.RootElement.GetString());
            var missing=await Run(executable,[Path.Combine(directory,"missing")]);
            Assert.Equal(1,missing.ExitCode);Assert.Empty(missing.Output);Assert.NotEmpty(missing.Error);
            var usage=await Run(executable,[]);
            Assert.Equal(2,usage.ExitCode);Assert.Empty(usage.Output);Assert.Contains("Usage:",usage.Error);
        }
        finally {Directory.Delete(directory,true);}
    }

    private static async Task<(int ExitCode,byte[] Output,string Error)> Run(string executable,string[] arguments)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;
        using var output=new MemoryStream();
        var bytes=process.StandardOutput.BaseStream.CopyToAsync(output);
        var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try {await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();Assert.Fail("quote-lines timed out.");}
        await bytes;
        return(process.ExitCode,output.ToArray(),await error);
    }
}
