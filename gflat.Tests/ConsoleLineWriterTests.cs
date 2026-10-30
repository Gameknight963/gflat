using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public sealed class ConsoleLineWriterTests
{
    private const string DenyAllocation="""
        using std; using std::Text; using std::IO;int attempts=0;
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes){attempts++;return null;}
            public replace void Free(void* storage) nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BorrowedWriterAppendsCountedLinesAcrossEndingsAndChunkBoundaries(int optimization)
    {
        string directory=TemporaryDirectory();
        try
        {
            string executable=Compile(directory,DenyAllocation+"""
                int main(int argc,char** argv){
                    File expected=File::OpenRead(argv[1]);
                    int rows=FileBinary::ReadInt32LittleEndian(&expected);
                    char[16385] storage;char[16384] wanted;BufferWriter sink=new(storage,16385);
                    for(int row=0;row<rows;row++){
                        int length=FileBinary::ReadInt32LittleEndian(&expected);
                        if(length<0||length>16382)return 1;
                        expected.ReadExactly(wanted,(nuint)length);
                        sink.Clear();sink.Write("P\0",2);
                        if(!Console::TryReadLine(&sink)||sink.Length!=(nuint)length+2)return 2;
                        if(sink.Data[0]!='P'||sink.Data[1]!='\0')return 3;
                        for(nuint i=0;i<(nuint)length;i++)if(sink.Data[i+2]!=wanted[i])return 4;
                    }
                    sink.Clear();sink.Write("P\0",2);
                    if(Console::TryReadLine(&sink)||Console::TryReadLine(&sink)||sink.Length!=2)return 5;
                    expected.Close();if(attempts!=0)return 6;return 0;
                }
                """,optimization);
            (string Input,string[] Lines)[] samples=
            [
                ("",[]),("\n",[""]),("\r\n",[""]),("first\n\nlast",["first","","last"]),
                ("one\r\ntwo\r\n",["one","two"]),("A\0B\n",["A\0B"]),
                ("bare\rCR\nlast\r",["bare\rCR","last\r"]),
                (new string('x',255)+"\r\rZ\n"+new string('y',256)+"\r\n"+new string('z',513),
                    [new string('x',255)+"\r\rZ",new string('y',256),new string('z',513)])
            ];
            string expected=Path.Combine(directory,"expected.bin");
            foreach(var sample in samples)
            {
                using(var file=new BinaryWriter(File.Create(expected)))
                {
                    file.Write(sample.Lines.Length);
                    foreach(string line in sample.Lines){byte[] bytes=Encoding.UTF8.GetBytes(line);file.Write(bytes.Length);file.Write(bytes);}
                }
                await Run(executable,Encoding.UTF8.GetBytes(sample.Input),[expected]);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CountingSinkReadsVeryLongLineWithoutStringOrApplicationAllocation(int optimization)
    {
        string directory=TemporaryDirectory();
        try
        {
            string executable=Compile(directory,DenyAllocation+"""
                int main(){
                    CountingWriter count=new();count.Write(((nuint)1).reinterpret<readonly(char)*>(),7);
                    if(!Console::TryReadLine(&count)||count.Length!=1048583)return 1;
                    count.Clear();if(!Console::TryReadLine(&count)||count.Length!=0)return 2;
                    if(!Console::TryReadLine(&count)||count.Length!=10)return 3;
                    if(Console::TryReadLine(&count)||count.Length!=10||attempts!=0)return 4;
                    return 0;
                }
                """,optimization);
            await Run(executable,Encoding.ASCII.GetBytes(new string('a',1048576)+"\r\n\nlast\0byte\r"),[]);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SinkExceptionRemainsOriginalAndDoesNotDrainTheUnconsumedLine(int optimization)
    {
        string directory=TemporaryDirectory();
        try
        {
            string executable=Compile(directory,"""
                using std; using std::Text; using std::IO;
                class SinkFailure:Exception{public SinkFailure():base("sink failed",77){}}
                struct Sink:ITextWriter {
                    public nuint Offered;public nuint Accepted;
                    public Sink(){Offered=0;Accepted=0;}
                    public void Write(readonly(char)* data,nuint count){
                        Offered+=count;
                        for(nuint i=0;i<count;i++){
                            if(Accepted==8)throw new SinkFailure();Accepted++;
                        }
                    }
                }
                int main(){
                    Sink sink=new();int caught=0;
                    try{Console::TryReadLine(&sink);}catch(SinkFailure* error){if(error.GetCode()!=77)return 1;caught++;}
                    catch{return 2;}
                    if(caught!=1||sink.Accepted!=8||sink.Offered<=8||sink.Offered>=4096)return 3;
                    CountingWriter remaining=new();
                    if(!Console::TryReadLine(&remaining)||remaining.Length+sink.Offered!=4096)return 4;
                    remaining.Clear();if(!Console::TryReadLine(&remaining)||remaining.Length!=4)return 5;
                    if(Console::TryReadLine(&remaining)||remaining.Length!=4)return 6;
                    return 0;
                }
                """,optimization);
            await Run(executable,Encoding.ASCII.GetBytes(new string('x',4096)+"\nnext\n"),[]);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }

    private static string TemporaryDirectory()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat-console-writer-line-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);return directory;
    }

    private static string Compile(string directory,string program,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("console-writer-line.gf",program));
        string ir=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
        File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var result=NativeToolchain.Compile(ir,executable,optimizationLevel:optimization);
        Assert.True(result.ExitCode==0,result.StandardError);return executable;
    }

    internal static async Task Run(string executable,byte[] input,string[] arguments)
    {
        using var process=new Process{StartInfo=new ProcessStartInfo(executable){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,
            RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        }};
        foreach(string argument in arguments)process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(input,timeout.Token);
            process.StandardInput.Close();await process.WaitForExitAsync(timeout.Token);
        }
        catch(OperationCanceledException){process.Kill(true);throw new TimeoutException("Console writer line test timed out.");}
        string diagnostic=await error;
        Assert.True(process.ExitCode==0,$"Console line sink exit {process.ExitCode}: {diagnostic}");
        Assert.Empty(await output);Assert.Empty(diagnostic);
    }
}
