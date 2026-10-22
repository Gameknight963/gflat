using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class JsonMinifyExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolPreservesOrderedTokensAndValidatesBeforeOutput(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("json-minify.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/json-minify.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-minify-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"json-minify.exe"),input=Path.Combine(directory,"document with spaces.json");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            List<string> documents=["true"," \r\n-0.00000E+99999999999\t ","{}","[ ]",
                " { \"same\" : 1e-999999999, \"same\": [-0,12345678901234567890123456789012345678901234567890,1.2000E+03] } ",
                "{\"\\u0000\\uD83D\\uDE00\":\"\\b\\f\\n\\r\\t\\/\\\\\\\"\\u0000\",\"literal\":\"\u20ac\ud83d\ude00\u2028\uffff\"}",
                "\"\"", " \""+new string('x',15000)+"\\u0000\\u20ac\" ",
                new string('[',300)+" {\"x\":true,\"x\":null} "+new string(']',300)];
            var random=new Random(71124);
            for(int sample=0;sample<16;sample++)
            {
                var strings=Enumerable.Range(0,8).Select(_=>new string(Enumerable.Range(0,random.Next(25)).Select(_=>(char)random.Next(128)).ToArray())).ToArray();
                documents.Add(JsonSerializer.Serialize(new{sample,strings,values=Enumerable.Range(0,10).Select(_=>random.NextInt64()).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
            }
            foreach(string document in documents)
            {
                byte[] original=Encoding.UTF8.GetBytes(document);File.WriteAllBytes(input,original);
                var result=await Run(executable,[input]);Assert.Equal(0,result.ExitCode);Assert.Empty(result.Error);
                Assert.True(result.Output.AsSpan().EndsWith(Encoding.UTF8.GetBytes(Environment.NewLine)));
                byte[] compact=result.Output[..^Encoding.UTF8.GetByteCount(Environment.NewLine)];
                Assert.Equal(Tokens(original,false),Tokens(compact,true));
                Assert.Equal(original,File.ReadAllBytes(input));
                // A second pass must be stable, including original string escaping and numeric spelling.
                File.WriteAllBytes(input,compact);var repeated=await Run(executable,[input]);
                Assert.Equal(0,repeated.ExitCode);Assert.Equal(result.Output,repeated.Output);Assert.Empty(repeated.Error);
            }
            byte[][] invalid=[[]," "u8.ToArray(),"{} garbage"u8.ToArray(),"[1,]"u8.ToArray(),"[1"u8.ToArray(),
                "{\"name\":true,}"u8.ToArray(),"\"\\uD800\""u8.ToArray(),[34,0xff,34],[34,0,34],
                [0xef,0xbb,0xbf,48],Encoding.UTF8.GetBytes("[\""+new string('a',10000)+"\"] trailing")];
            foreach(byte[] original in invalid)
            {
                File.WriteAllBytes(input,original);var result=await Run(executable,[input]);
                Assert.Equal(1,result.ExitCode);Assert.Empty(result.Output);
                Assert.Contains(input+": byte ",result.Error);Assert.Equal(original,File.ReadAllBytes(input));
            }
            string missing=Path.Combine(directory,"missing.json");var absent=await Run(executable,[missing]);
            Assert.Equal(1,absent.ExitCode);Assert.Empty(absent.Output);Assert.Contains(missing,absent.Error);
            foreach(string[] args in new string[][]{[],[input,input]})
            {
                var usage=await Run(executable,args);Assert.Equal(2,usage.ExitCode);Assert.Empty(usage.Output);Assert.Contains("Usage:",usage.Error);
            }
        }
        finally
        {
            // Windows may keep the just-exited executable mapped briefly. Retry
            // deletion without suppressing a persistent cleanup failure.
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Delete(directory, true); break; }
                catch (Exception error) when (OperatingSystem.IsWindows() && attempt < 8 &&
                    error is IOException or UnauthorizedAccessException)
                { await Task.Delay(50 * (attempt + 1)); }
            }
        }
    }

    private static List<(JsonTokenType Kind,string Value)> Tokens(byte[] bytes,bool requireCompact)
    {
        var result=new List<(JsonTokenType,string)>();
        var reader=new Utf8JsonReader(bytes,new JsonReaderOptions{MaxDepth=1024});
        long previousEnd=0;
        while(reader.Read())
        {
            if(requireCompact)
            {
                var gap=bytes.AsSpan((int)previousEnd,(int)(reader.TokenStartIndex-previousEnd));
                Assert.True(gap.IsEmpty||gap.SequenceEqual(","u8)||gap.SequenceEqual(":"u8));
            }
            string value=reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName or JsonTokenType.Number
                ? Encoding.UTF8.GetString(reader.ValueSpan) : "";
            result.Add((reader.TokenType,value));
            previousEnd=reader.BytesConsumed;
        }
        if(requireCompact)Assert.Equal(bytes.Length,previousEnd);
        return result;
    }

    private static async Task<(int ExitCode,byte[] Output,string Error)> Run(string executable,string[] arguments)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;using var output=new MemoryStream();
        var bytes=process.StandardOutput.BaseStream.CopyToAsync(output);var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();Assert.Fail("json-minify timed out.");}
        await bytes;return(process.ExitCode,output.ToArray(),await error);
    }
}
