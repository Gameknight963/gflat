using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class TailExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolTailsBinaryFileLinesAndValidatesArguments(int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile("tail.gf",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/tail.gf"))));
        string directory=Path.Combine(Path.GetTempPath(),"gflat_tail_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"tail.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            string inputPath=Path.Combine(directory,"input records.bin");
            string maximum=((ulong)nuint.MaxValue).ToString(CultureInfo.InvariantCulture);
            List<(string? Argument,ulong Count)> counts=[(null,10),("0",0),("1",1),("2",2),("17",17),("1000",1000),("1001",1001),(maximum,(ulong)nuint.MaxValue),("+1",1),("+0002",2),("000",0)];
            List<byte[]> inputs=[[],[10],[10,10,10],[13,10],[13,13,10],[13],"one"u8.ToArray(),"one\n"u8.ToArray(),
                "first\r\nsecond\n\nlast\r"u8.ToArray(),
                [0,255,128,26,13,10,195,40,10,0,13],
                [26,110,111,116,32,101,111,102,10,255,0],
                Enumerable.Repeat((byte)'x',18000).Concat(new byte[]{13,10,255,0,26,13}).ToArray()];
            using(var wrap=new MemoryStream()){
                for(int line=0;line<1000;line++){
                    wrap.Write(Encoding.ASCII.GetBytes($"line-{line:D4}"));
                    if(line%31==0)wrap.Write([0,26,255]);
                    if(line==999)break;
                    if(line%2==0)wrap.WriteByte(13);wrap.WriteByte(10);
                }
                inputs.Add(wrap.ToArray());
            }
            var random=new Random(104379);
            using(var arbitrary=new MemoryStream()){
                for(int line=0;line<80;line++){
                    byte[] bytes=new byte[random.Next(0,97)];random.NextBytes(bytes);
                    // Keep LF delimiters explicit. CR, NUL, Ctrl+Z and invalid UTF8
                    // remain ordinary binary payload and exercise the byte oracle.
                    for(int i=0;i<bytes.Length;i++)if(bytes[i]==10)bytes[i]=255;
                    arbitrary.Write(bytes);if(line%3==0)arbitrary.WriteByte(13);arbitrary.WriteByte(10);
                }
                arbitrary.Write([255,0,26,13]);inputs.Add(arbitrary.ToArray());
            }
            Assert.Equal(OperatingSystem.IsWindows()?new byte[]{97,13,13,10}:new byte[]{97,13,10},Expected([97,13,13,10],10));
            Assert.Equal(OperatingSystem.IsWindows()?new byte[]{26,13,10}:new byte[]{26,10},Expected([26,10],10));
            foreach(byte[] input in inputs){
                await File.WriteAllBytesAsync(inputPath,input);
                foreach(var count in counts){
                    string[] arguments=count.Argument==null?[inputPath]:[inputPath,count.Argument];
                    var result=await Run(executable,arguments);
                    Assert.True(result.Code==0,$"tail count '{count.Argument??"default"}' exit {result.Code}: {result.Error}");
                    Assert.Empty(result.Error);Assert.Equal(Expected(input,count.Count),result.Output);
                    Assert.Equal(input,await File.ReadAllBytesAsync(inputPath));
                }
            }
            byte[] preserved="unchanged\r\n"u8.ToArray();await File.WriteAllBytesAsync(inputPath,preserved);
            string overflow=(new BigInteger((ulong)nuint.MaxValue)+1).ToString(CultureInfo.InvariantCulture);
            List<string[]> invalid=[[],[inputPath,"1","extra"]];
            foreach(string count in new[]{"-1","-0",""," "," 1","1 ","\t1","1\n","+","--1","+-1","1x","1_0","1.0",overflow,"+"+overflow})invalid.Add([inputPath,count]);
            foreach(string[] arguments in invalid){
                var result=await Run(executable,arguments);
                Assert.True(result.Code==2,$"Expected argument exit2, received {result.Code}: {result.Error}");
                Assert.Empty(result.Output);Assert.NotEmpty(result.Error);
                Assert.Equal(preserved,await File.ReadAllBytesAsync(inputPath));
            }
            string missing=Path.Combine(directory,"missing file.bin");
            foreach(string[] arguments in new[]{new[]{missing},new[]{missing,"0"},new[]{missing,"1"}}){
                var result=await Run(executable,arguments);
                Assert.True(result.Code==1,$"Expected input exit1, received {result.Code}: {result.Error}");
                Assert.Empty(result.Output);Assert.StartsWith("tail: ",result.Error);
            }
        }finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static byte[] Expected(byte[] input,ulong count)
    {
        // File.OpenRead uses rb on EVERY platform: no CRT CRLF pretranslation
        // or Ctrl+Z EOF treatment is applied to these input bytes.
        var lines=new List<byte[]>();int start=0;
        for(int i=0;i<input.Length;i++){
            if(input[i]!=10)continue;
            int end=i;if(end>start&&input[end-1]==13)end--;
            lines.Add(input[start..end]);start=i+1;
        }
        if(start<input.Length)lines.Add(input[start..]);
        int retained=lines.Count;if(count<(ulong)retained)retained=(int)count;
        byte[] newline=OperatingSystem.IsWindows()?[13,10]:[10];
        using var output=new MemoryStream();
        foreach(byte[] line in lines.Skip(lines.Count-retained)){output.Write(line);output.Write(newline);}
        return output.ToArray();
    }

    private static async Task<(int Code,byte[] Output,string Error)> Run(string executable,IEnumerable<string> arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());process.StandardInput.Close();
        using var output=new MemoryStream();Task outputTask=process.StandardOutput.BaseStream.CopyToAsync(output);
        Task<string> error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){process.Kill(true);throw new TimeoutException("tail timed out.");}
        await outputTask;return(process.ExitCode,output.ToArray(),await error);
    }
}
