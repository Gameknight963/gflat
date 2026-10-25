using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class JsonPrettyExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolPreservesRawTokensAndFormatsOnlyAfterWholeInputValidation(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-pretty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
                .Select(path=>new SourceFile(path,File.ReadAllText(path)))
                .Append(new SourceFile("json-pretty.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/json-pretty.gf"))));
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"json-pretty.exe"),input=Path.Combine(directory,"document with spaces.json");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
            const string document=" {\"a\":[1,{\"b\":true}],\"a\":{},\"empty\":[],\"s\":\"\\u0061\\/é😀\",\"n\":-0.1200E+99999999999} ";
            const string golden="{\n\t\"a\": [\n\t\t1,\n\t\t{\n\t\t\t\"b\": true\n\t\t}\n\t],\n\t\"a\": {},\n\t\"empty\": [],\n\t\"s\": \"\\u0061\\/é😀\",\n\t\"n\": -0.1200E+99999999999\n}";
            foreach(int width in new[]{0,2,4,16})
            {
                File.WriteAllText(input,document,new UTF8Encoding(false));byte[] original=File.ReadAllBytes(input);
                var result=await Run(executable,[input,width.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                Assert.Equal(0,result.Code);Assert.Empty(result.Error);
                Assert.Equal(ConsoleBytes(golden.Replace("\t",new string(' ',width))+"\n"),result.Output);
                Assert.Equal(Tokens(original),Tokens(result.Output));Assert.Equal(original,File.ReadAllBytes(input));
                File.WriteAllBytes(input,result.Output);var repeated=await Run(executable,[input,width.ToString()]);
                Assert.Equal(0,repeated.Code);Assert.Empty(repeated.Error);Assert.Equal(result.Output,repeated.Output);
            }
            File.WriteAllText(input,document,new UTF8Encoding(false));var defaults=await Run(executable,[input]);var leadingZero=await Run(executable,[input,"0002"]);
            Assert.Equal(0,defaults.Code);Assert.Empty(defaults.Error);Assert.Equal(ConsoleBytes(golden.Replace("\t","  ")+"\n"),defaults.Output);
            Assert.Equal(0,leadingZero.Code);Assert.Empty(leadingZero.Error);Assert.Equal(defaults.Output,leadingZero.Output);
            var plus=await Run(executable,[input,"+4"]);Assert.Equal(0,plus.Code);Assert.Empty(plus.Error);Assert.Equal(ConsoleBytes(golden.Replace("\t","    ")+"\n"),plus.Output);
            foreach(string scalar in new[]{"null","true","{}","[]","-0.00000E-999999999","\"\\u0000\\uD83D\\uDE00\"","\""+new string('x',15000)+"\\n\""})
            {
                File.WriteAllText(input," \r\n"+scalar+"\t ",new UTF8Encoding(false));var result=await Run(executable,[input,"16"]);
                Assert.Equal(0,result.Code);Assert.Empty(result.Error);Assert.Equal(ConsoleBytes(scalar+"\n"),result.Output);
            }
            byte[][] invalid=[[]," "u8.ToArray(),"{} trailing"u8.ToArray(),"[1,]"u8.ToArray(),"[1"u8.ToArray(),"{\"x\":true,}"u8.ToArray(),
                "\"\\uD800\""u8.ToArray(),[34,0xff,34],[34,0,34],[0xef,0xbb,0xbf,48],Encoding.UTF8.GetBytes("[\""+new string('a',10000)+"\"] suffix")];
            foreach(byte[] original in invalid)
            {
                File.WriteAllBytes(input,original);var result=await Run(executable,[input]);Assert.Equal(1,result.Code);Assert.Empty(result.Output);
                Assert.Contains(input+": byte ",Encoding.UTF8.GetString(result.Error));Assert.Equal(original,File.ReadAllBytes(input));
            }
            File.WriteAllText(input,"[1,]",new UTF8Encoding(false));var offset=await Run(executable,[input]);
            Assert.StartsWith(input+": byte 3: ",Encoding.UTF8.GetString(offset.Error));
            string missing=Path.Combine(directory,"missing.json");var absent=await Run(executable,[missing,"0"]);
            Assert.Equal(1,absent.Code);Assert.Empty(absent.Output);Assert.StartsWith(missing+": ",Encoding.UTF8.GetString(absent.Error));
            foreach(string width in new[]{"","-1","-0","+"," 2","2 ","1.0","0x2","17","999999999999999999999999999999999999","٢"})
            {
                var result=await Run(executable,[missing,width]);Assert.Equal(2,result.Code);Assert.Empty(result.Output);
                Assert.Equal(ConsoleBytes("json-pretty: indent-spaces must be a decimal integer from 0 through 16\n"),result.Error);
            }
            foreach(string[] arguments in new string[][]{[],[missing,"2","extra"]})
            {
                var result=await Run(executable,arguments);Assert.Equal(2,result.Code);Assert.Empty(result.Output);
                Assert.Equal(ConsoleBytes("Usage: json-pretty <input-file> [indent-spaces]\n"),result.Error);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolReportsBothBufferedAndImmediateOutputFailures(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-pretty-output-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"input.json");File.WriteAllText(input,"{\"ok\":true}",new UTF8Encoding(false));
            ExampleOutputFailure.AssertReportedWithArguments("json-pretty",optimization,input+": Console flush failed",input+": Console write failed",input);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static byte[] ConsoleBytes(string text)=>Encoding.UTF8.GetBytes(OperatingSystem.IsWindows()?text.Replace("\n","\r\n"):text);
    private static List<(JsonTokenType Kind,string Raw)> Tokens(byte[] bytes)
    {
        var result=new List<(JsonTokenType,string)>();var reader=new Utf8JsonReader(bytes,new JsonReaderOptions{MaxDepth=1024});
        while(reader.Read())result.Add((reader.TokenType,reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName or JsonTokenType.Number?Encoding.UTF8.GetString(reader.ValueSpan):""));
        return result;
    }
    private static async Task<(int Code,byte[] Output,byte[] Error)> Run(string executable,IEnumerable<string> arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);Assert.True(process.Start());process.StandardInput.Close();
        using var output=new MemoryStream();using var error=new MemoryStream();Task stdout=process.StandardOutput.BaseStream.CopyToAsync(output),stderr=process.StandardError.BaseStream.CopyToAsync(error);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();await Task.WhenAll(stdout,stderr);throw new TimeoutException("json-pretty timed out.");}
        await Task.WhenAll(stdout,stderr);return(process.ExitCode,output.ToArray(),error.ToArray());
    }
}
