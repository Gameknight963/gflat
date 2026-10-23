using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace gflat.Tests;

public sealed class IpCountExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolCountsCanonicalAddressesAndRejectsInvalidSuffixesBeforeOutput(int optimization)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-ip-count-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Examples/ip-count.gf"));
            string executable=Compile(directory,"tool",source,optimization);
            var samples=new List<string>{"","\n \t\r\n","::1",
                "2001:0DB8:0:0:0:0:0:1\n 192.0.2.1 \r\n2001:db8::1\n::FFFF:C000:0201\n::ffff:192.0.2.1\n192.0.2.1\n",
                new string(' ',246)+"192.0.2.1\n"};
            var random=new Random(93013);var bulk=new List<string>();
            for(int i=0;i<120;i++)
            {
                byte[] bytes=new byte[16];random.NextBytes(bytes);
                string canonical=new IPAddress(bytes).ToString();
                string expanded=string.Join(":",Enumerable.Range(0,8).Select(group=>
                    ((bytes[group*2]<<8)|bytes[group*2+1]).ToString("X4",CultureInfo.InvariantCulture)));
                bulk.Add(expanded);bulk.Add(canonical);bulk.Add(new IPAddress(new byte[]{192,0,2,(byte)i}).ToString());
            }
            random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bulk));
            samples.Add(string.Join("\n",bulk)+"\n");
            foreach(string input in samples)
            {
                var result=await Run(executable,Encoding.UTF8.GetBytes(input),[]);
                Assert.True(result.Code==0,result.Error);Assert.Empty(result.Error);
                Assert.Equal(Expected(input),result.Output);
            }
            foreach(string invalid in new[]{"127.1","127.000.0.1","0x7f.0.0.1","256.0.0.1","2001:::1",
                "[::1]","127.0.0.1:80","fe80::1%2","192.0.2.1/24","192.0.2.1\0"})
            {
                var result=await Run(executable,Encoding.UTF8.GetBytes("192.0.2.1\n\n"+invalid+"\n"),[]);
                Assert.Equal(2,result.Code);Assert.Empty(result.Output);
                Assert.Equal("Line 3: invalid IP address"+Environment.NewLine,result.Error);
            }
            var longLine=await Run(executable,Encoding.ASCII.GetBytes(new string(' ',256)+"192.0.2.1\n"),[]);
            Assert.Equal(1,longLine.Code);Assert.Empty(longLine.Output);Assert.StartsWith("ip-count: ",longLine.Error);
            var usage=await Run(executable,[],["unexpected"]);
            Assert.Equal(2,usage.Code);Assert.Empty(usage.Output);Assert.Contains("Usage: ip-count",usage.Error);

            // A read-only stdout stream fails writes without changing file permissions.
            string originalMain="int main(int argc, char** argv)";
            Assert.Contains(originalMain,source);
            string failing=Compile(directory,"output-failure",source.Replace(originalMain,"int IpCountMain(int argc, char** argv)")+"""

                extern void* __gflat_stdio_stdout();
                extern void*? freopen(readonly(char)* path,readonly(char)* mode,void* stream);
                int main(int argc,char** argv){
                    if(freopen(argv[1],"rb",__gflat_stdio_stdout())==null)return 90;
                    return IpCountMain(1,argv);
                }
                """,optimization);
            string readOnly=Path.Combine(directory,"read only.txt");byte[] saved="unchanged"u8.ToArray();
            File.WriteAllBytes(readOnly,saved);
            var failure=await Run(failing,"192.0.2.1\n"u8.ToArray(),[readOnly]);
            Assert.Equal(1,failure.Code);Assert.Empty(failure.Output);Assert.StartsWith("ip-count: ",failure.Error);
            Assert.Equal(saved,File.ReadAllBytes(readOnly));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static string Expected(string input)
    {
        var counts=new Dictionary<string,ulong>(StringComparer.Ordinal);var order=new List<string>();
        using var reader=new StringReader(input);
        while(reader.ReadLine() is string line)
        {
            line=line.Trim();if(line.Length==0)continue;
            string address=IPAddress.Parse(line).ToString();
            if(counts.TryGetValue(address,out ulong count))counts[address]=count+1;
            else{counts.Add(address,1);order.Add(address);}
        }
        return string.Concat(order.Select(address=>address+"\t"+counts[address].ToString(CultureInfo.InvariantCulture)+Environment.NewLine));
    }

    private static string Compile(string directory,string name,string source,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("ip-count.gf",source));
        string ir=Path.Combine(directory,name+".ll"),executable=Path.Combine(directory,name+".exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
        Assert.True(compiled.ExitCode==0,compiled.StandardError);return executable;
    }

    private static async Task<(int Code,string Output,string Error)> Run(string executable,byte[] input,string[] arguments)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;
        Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(input,timeout.Token);process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch(OperationCanceledException)
        {
            process.Kill(entireProcessTree:true);await process.WaitForExitAsync();throw new TimeoutException("ip-count timed out.");
        }
        return(process.ExitCode,await output,await error);
    }
}
