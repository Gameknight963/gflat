using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class JsonGetExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolSelectsRawValuesAndValidatesEntireInputBeforeOutput(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("json-get.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/json-get.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat-json-get-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"json-get.exe");
            string input=Path.Combine(directory,"document with spaces.json");
            string pointerFile=Path.Combine(directory,"pointer with spaces.txt");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var valid=new List<(string Document,string Pointer,Func<JsonElement,JsonElement> Select)>
            {
                (" \r\n { \"a\" : [1e+02, \"x\\u0000\\u20ac\", { \"b\": true }] } \t","",root=>root),
                ("{\n \"a\": [1,\n 2]\n}","",root=>root),
                ("{\"a\":[1e+02,\"x\\u0000\\u20ac\",{\"b\":true}]}","/a/0",root=>root.GetProperty("a")[0]),
                ("{\"a\":[1e+02,\"x\\u0000\\u20ac\",{\"b\":true}]}","/a/1",root=>root.GetProperty("a")[1]),
                ("{\"a\":[1e+02,\"x\\u0000\\u20ac\",{\"b\":true}]}","/a/2",root=>root.GetProperty("a")[2]),
                ("{\"a/b\":{\"m~n\":[null,false]}}","/a~1b/m~0n/0",root=>root.GetProperty("a/b").GetProperty("m~n")[0]),
                ("{\"\":{\"\":4},\"~1\":5}","//",root=>root.GetProperty("").GetProperty("")),
                ("{\"\":{\"\":4},\"~1\":5}","/~01",root=>root.GetProperty("~1")),
                ("{\"😀\":\"é\"}","/😀",root=>root.GetProperty("😀")),
                ("{\"a\\u0000b\":42}","/a\0b",root=>root.GetProperty("a\0b")),
                ("{\"same\":1,\"same\":2,\"other\":3}","/other",root=>root.GetProperty("other")),
                (" \"root\\u0000\" ","",root=>root),
                (" [ ] ","",root=>root),
                ("false","",root=>root)
            };
            var random=new Random(690102);
            for(int row=0;row<8;row++){
                string document=JsonSerializer.Serialize(new{values=Enumerable.Range(0,8).Select(_=>random.NextInt64()).ToArray()});
                int index=row;valid.Add((document,"/values/"+index,root=>root.GetProperty("values")[index]));
            }
            foreach(var sample in valid)
            {
                byte[] original=Encoding.UTF8.GetBytes(sample.Document);File.WriteAllBytes(input,original);
                using JsonDocument document=JsonDocument.Parse(original);
                string consoleText=sample.Select(document.RootElement).GetRawText().Replace("\n",Environment.NewLine);
                byte[] expected=Encoding.UTF8.GetBytes(consoleText+Environment.NewLine);
                File.WriteAllBytes(pointerFile,Encoding.UTF8.GetBytes(sample.Pointer));
                var result=await Run(executable,[input,"--pointer-file",pointerFile]);
                Assert.True(result.Code==0,$"Pointer file '{sample.Pointer}' exited {result.Code}: {result.Error}");Assert.Empty(result.Error);Assert.Equal(expected,result.Output);
                // argv is a native narrow string on Windows. The counted file mode
                // covers all UTF-8/NUL pointers; plain ASCII arguments are portable.
                if(!sample.Pointer.Contains('\0')&&(!OperatingSystem.IsWindows()||sample.Pointer.All(c=>c<=127)))
                {
                    var argument=await Run(executable,[input,sample.Pointer]);
                    Assert.True(argument.Code==0,$"Pointer argument '{sample.Pointer}' exited {argument.Code}: {argument.Error}");
                    Assert.Empty(argument.Error);Assert.Equal(expected,argument.Output);
                }
                Assert.Equal(original,File.ReadAllBytes(input));
            }
            (string Document,string Pointer)[] unresolved=
            [
                ("{\"a\":1}","/missing"),("{\"a\":1}","/a/x"),("[1]","/01"),("[1]","/-"),
                ("[1]","/18446744073709551616"),("{\"a\":1,\"\\u0061\":2}","/a"),
                ("{\"a\":1}","/~2"),("{\"a\":1}","#/a"),("{\"a\":1}","a")
            ];
            foreach(var sample in unresolved)
            {
                File.WriteAllBytes(input,Encoding.UTF8.GetBytes(sample.Document));
                var result=await Run(executable,[input,sample.Pointer]);
                Assert.Equal(2,result.Code);Assert.Empty(result.Output);
                Assert.Equal("JSON pointer did not resolve"+Environment.NewLine,result.Error);
                File.WriteAllBytes(pointerFile,Encoding.UTF8.GetBytes(sample.Pointer));
                var fileResult=await Run(executable,[input,"--pointer-file",pointerFile]);
                Assert.Equal(2,fileResult.Code);Assert.Empty(fileResult.Output);Assert.Equal(result.Error,fileResult.Error);
            }
            File.WriteAllBytes(input,"{\"a\":1}"u8.ToArray());File.WriteAllBytes(pointerFile,[255]);
            var invalidUtf8=await Run(executable,[input,"--pointer-file",pointerFile]);
            Assert.Equal(2,invalidUtf8.Code);Assert.Empty(invalidUtf8.Output);Assert.Contains("did not resolve",invalidUtf8.Error);
            foreach(string pointer in new[]{"/a","/~2",""})
            {
                byte[] original=" {\"a\":1} trailing"u8.ToArray();File.WriteAllBytes(input,original);
                var result=await Run(executable,[input,pointer]);Assert.Equal(1,result.Code);Assert.Empty(result.Output);
                Assert.Contains("Invalid JSON at byte 9: ",result.Error);Assert.Equal(original,File.ReadAllBytes(input));
            }
            foreach(byte[] original in new byte[][]{[],"[1,]"u8.ToArray(),[34,255,34]})
            {
                File.WriteAllBytes(input,original);var result=await Run(executable,[input,""]);
                Assert.Equal(1,result.Code);Assert.Empty(result.Output);Assert.Contains("Invalid JSON at byte ",result.Error);
            }
            var missing=await Run(executable,[Path.Combine(directory,"missing.json"),""]);
            Assert.Equal(1,missing.Code);Assert.Empty(missing.Output);Assert.NotEmpty(missing.Error);
            var missingPointer=await Run(executable,[input,"--pointer-file",Path.Combine(directory,"missing-pointer.txt")]);
            Assert.Equal(1,missingPointer.Code);Assert.Empty(missingPointer.Output);Assert.NotEmpty(missingPointer.Error);
            foreach(string[] arguments in new string[][]{[],[input],[input,"","extra"],[input,"--pointer-file"],[input,"--pointer-file",pointerFile,"extra"]})
            {
                var result=await Run(executable,arguments);Assert.Equal(2,result.Code);Assert.Empty(result.Output);
                Assert.Equal("Usage: json-get <input-file> <pointer>"+Environment.NewLine+"       json-get <input-file> --pointer-file <utf8-file>"+Environment.NewLine,result.Error);
            }
        }
        finally {await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static async Task<(int Code,byte[] Output,string Error)> Run(string executable,string[] arguments)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;using var output=new MemoryStream();
        Task bytes=process.StandardOutput.BaseStream.CopyToAsync(output);Task<string> error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();Assert.Fail("json-get timed out.");}
        await bytes;return(process.ExitCode,output.ToArray(),await error);
    }
}
