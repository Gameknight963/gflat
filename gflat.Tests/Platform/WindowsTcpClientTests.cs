using System.Net;
using System.Net.Sockets;
using System.Text;
using gflat.CompileExceptions;
using gflat.Projects;

namespace gflat.Tests.Platform;

public sealed class WindowsTcpClientTests
{
    internal static string CopiedClient => Path.Combine(AppContext.BaseDirectory,"Platform","windows-network","TcpClient.gf");
    internal static string CopiedException => Path.Combine(AppContext.BaseDirectory,"Platform","windows-network","SocketException.gf");
    internal static string ProjectPath => Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot,"libraries","platform","windows-network","WindowsNetwork.gfproj");

    internal static IEnumerable<SourceFile> SourceFiles(string client,string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path)))
            .Append(new SourceFile(CopiedException,File.ReadAllText(CopiedException)))
            .Append(new SourceFile(CopiedClient,client))
            .Append(new SourceFile("windows-tcp-test.gf",program));

    [Fact]
    public void OptionalProjectOwnsWinsockDependencyAndUnmodifiedPublicApiEmitsOnEveryHost()
    {
        Assert.Equal(8,IntPtr.Size);
        var graph=ProjectGraph.Load(ProjectPath);
        var library=Assert.Single(graph.NativeLinkInputs.Libraries);
        Assert.Equal("Name",library.Kind);Assert.Equal("ws2_32",library.Value);
        string repository=TestSupport.ProjectFileFixture.RepositoryRoot;
        foreach(string relative in new[]{"std/std.gfproj","libraries/platform/windows/Windows.gfproj","libraries/platform/windows-random/WindowsRandom.gfproj"})
            Assert.DoesNotContain(ProjectGraph.Load(Path.Combine(repository,relative)).NativeLinkInputs.Libraries,
                item=>item.Value.Equals("ws2_32",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ProjectPath)!,"TcpClient.gf")),File.ReadAllBytes(CopiedClient));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ProjectPath)!,"SocketException.gf")),File.ReadAllBytes(CopiedException));
        string ir=Compiler.Emit(SourceFiles(File.ReadAllText(CopiedClient),"""
            using Windows;using std::Net;
            int main(){
                TcpClient empty=new();empty.Close();
                TcpClient client=TcpClient::Connect(IpEndpoint::Parse("127.0.0.1:12345"));
                char[3] data;client.Read(data,3);client.ReadExactly(data,3);
                ITextWriter* output=&client;output.Write(data,3);client.ShutdownSend();client.Close();return 0;
            }
            """),options:new(){NoStdLib=true,InternalizeFunctions=true});
        foreach(string signature in new[]{"@WSAStartup(i16, i8*)","@WSACleanup()","@WSAGetLastError()",
            "@socket(i32, i32, i32)","@connect(i64, i8*, i32)","@recv(i64, i8*, i32, i32)",
            "@send(i64, i8*, i32, i32)","@shutdown(i64, i32)","@closesocket(i64)"})
            Assert.Contains(signature,ir);
        // Emit only: this validates source and signatures, not execution of Windows ABI on another OS.
    }

    [Theory]
    [InlineData("TcpClient duplicate=original;")]
    [InlineData("TcpClient duplicate=new();duplicate=original;")]
    public void SocketAndStartupOwnershipCannotBeImplicitlyCopied(string statement)
    {
        string program="using Windows;int main(){TcpClient original=new();"+statement+"return 0;}";
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(SourceFiles(File.ReadAllText(CopiedClient),program),
            options:new(){NoStdLib=true,InternalizeFunctions=true}));
        Assert.Contains("copy",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UnmodifiedWindowsClientTransfersBinaryDataAndBalancesTwoLiveOwnersOnLoopback(int optimization)
    {
        if(!OperatingSystem.IsWindows())return;
        Assert.Equal(8,IntPtr.Size);
        var graph=ProjectGraph.Load(ProjectPath);
        string directory=Path.Combine(Path.GetTempPath(),"gflat-windows-tcp-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string client=File.ReadAllText(CopiedClient);
            File.WriteAllText(Path.Combine(directory,"TcpClient.gf"),client);
            File.WriteAllText(Path.Combine(directory,"SocketException.gf"),File.ReadAllText(CopiedException));
            File.WriteAllText(Path.Combine(directory,"main.gf"),LoopbackProgram);
            string ir=Compiler.Emit(SourceFiles(client,LoopbackProgram),options:new(){NoStdLib=true,InternalizeFunctions=true});
            string path=Path.Combine(directory,"main.ll"),executable=Path.Combine(directory,"main.exe");
            File.WriteAllText(path,ir);
            var compilation=NativeToolchain.Compile(path,executable,optimizationLevel:optimization,nativeLibraries:graph.NativeLinkInputs);
            File.WriteAllText(Path.Combine(directory,"compile.stdout"),compilation.StandardOutput);
            File.WriteAllText(Path.Combine(directory,"compile.stderr"),compilation.StandardError);
            Assert.True(compilation.ExitCode==0,compilation.StandardError);
            await Exchange(executable,directory,IPAddress.Loopback,twoClients:false);
            await Exchange(executable,directory,IPAddress.Loopback,twoClients:true);
            if(Socket.OSSupportsIPv6){
                await Exchange(executable,directory,IPAddress.IPv6Loopback,twoClients:false);
                await Exchange(executable,directory,IPAddress.IPv6Loopback,twoClients:true);
            }
            else File.WriteAllText(Path.Combine(directory,"ipv6-unavailable.txt"),"Socket.OSSupportsIPv6 was false; IPv6 was not executed.");
        }
        catch(Exception error){error.Data["WindowsTcpDirectory"]=directory;throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static async Task Exchange(string executable,string directory,IPAddress address,bool twoClients)
    {
        string label=(address.AddressFamily==AddressFamily.InterNetwork?"ipv4":"ipv6")+(twoClients?"-two":"-binary");
        var transcript=new StringBuilder();object sync=new();
        void Trace(string value){lock(sync)transcript.AppendLine(DateTime.UtcNow.ToString("O")+" "+value);}
        var listener=new TcpListener(address,0);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Task? server=null;Task<ProcessResult>? native=null;
        try
        {
            listener.Start();string endpoint=((IPEndPoint)listener.LocalEndpoint).ToString();
            Trace("Listening "+endpoint);
            server=twoClients?ServeTwo(listener,Trace,deadline.Token):ServeBinary(listener,Trace,deadline.Token);
            native=Task.Run(()=>NativeToolchain.Run(executable,new[]{endpoint,twoClients?"1":"0"},20000));
            try{await Task.WhenAll(server,native).WaitAsync(TimeSpan.FromSeconds(30));}
            finally
            {
                if(native.IsCompletedSuccessfully)
                {
                    ProcessResult completed=await native;
                    File.WriteAllText(Path.Combine(directory,label+".stdout"),completed.StandardOutput);
                    File.WriteAllText(Path.Combine(directory,label+".stderr"),completed.StandardError);
                    Trace("Native exit "+completed.ExitCode);
                }
            }
            ProcessResult result=await native;
            Assert.True(result.ExitCode==0,$"TCP {label} exit {result.ExitCode}: {result.StandardError}");
            Assert.Equal("",result.StandardOutput);Assert.Equal("",result.StandardError);
        }
        catch(Exception error)
        {
            Trace("Failure "+error);deadline.Cancel();listener.Stop();
            foreach(Task? task in new Task?[]{server,native})
            {
                if(task==null)continue;
                try{await task.WaitAsync(TimeSpan.FromSeconds(25));}
                catch(Exception cleanupError){Trace("Peer/task cleanup "+cleanupError);}
            }
            if(native?.IsCompletedSuccessfully==true)
            {
                ProcessResult completed=await native;
                    File.WriteAllText(Path.Combine(directory,label+".stdout"),completed.StandardOutput);
                File.WriteAllText(Path.Combine(directory,label+".stderr"),completed.StandardError);
            }
            else Trace("NativeToolchain.Run did not return streams; its timeout/error is recorded above.");
            error.Data["WindowsTcpDirectory"]=directory;throw;
        }
        finally
        {
            deadline.Cancel();listener.Stop();
            lock(sync)File.WriteAllText(Path.Combine(directory,label+".transcript"),transcript.ToString());
        }
    }

    private static async Task ServeBinary(TcpListener listener,Action<string> trace,CancellationToken token)
    {
        using var peer=await listener.AcceptTcpClientAsync(token);using var stream=peer.GetStream();
        trace("Accepted binary peer");byte[] request=new byte[4096];await stream.ReadExactlyAsync(request,token);
        Assert.Equal(Enumerable.Range(0,4096).Select(index=>(byte)(index%251)).ToArray(),request);
        byte[] extra=new byte[1];Assert.Equal(0,await stream.ReadAsync(extra,token));trace("Request and send EOF verified");
        byte[] response=Enumerable.Range(0,4096).Select(index=>(byte)((index*17+23)%256)).ToArray();
        int[] sizes={1,3,17,255,1024};int at=0,part=0;
        while(at<response.Length){int count=Math.Min(sizes[part++%sizes.Length],response.Length-at);await stream.WriteAsync(response.AsMemory(at,count),token);at+=count;}
        peer.Client.Shutdown(SocketShutdown.Send);trace("Response and receive EOF sent");
    }

    private static async Task ServeTwo(TcpListener listener,Action<string> trace,CancellationToken token)
    {
        using var first=await listener.AcceptTcpClientAsync(token);using var second=await listener.AcceptTcpClientAsync(token);
        using var firstStream=first.GetStream();using var secondStream=second.GetStream();trace("Accepted two peers");
        byte[] value=new byte[1];Assert.Equal(0,await firstStream.ReadAsync(value,token));trace("Moved first owner closed");
        await secondStream.ReadExactlyAsync(value,token);Assert.Equal((byte)85,value[0]);
        Assert.Equal(0,await secondStream.ReadAsync(value,token));
        await secondStream.WriteAsync(new byte[]{170},token);second.Client.Shutdown(SocketShutdown.Send);
        trace("Second owner transferred after first close");
    }

    private const string LoopbackProgram="""
        using Windows;using std::Net;using std::IO;
        extern nuint socket(int family,int kind,int protocol)nothrow;
        extern int WSAGetLastError()nothrow;
        extern int closesocket(nuint socket)nothrow;
        int Single(IpEndpoint endpoint){
            TcpClient original=TcpClient::Connect(endpoint);TcpClient client=move original;original=new();
            if(original.IsOpen||!client.IsOpen)return 1;
            char[4098] request;request[0]='A';request[4097]='Z';
            for(nuint index=0;index<4096;index++)request[index+1]=(char)(index%251);
            ITextWriter* output=&client;output.Write(&request[1],4096);client.ShutdownSend();
            char[4098] response;response[0]='Q';response[4097]='R';client.ReadExactly(&response[1],2048);
            IByteReader* input=&client;std::IO::ReadExactly(input,&response[2049],2048);
            if(request[0]!='A'||request[4097]!='Z'||response[0]!='Q'||response[4097]!='R')return 2;
            for(nuint index=0;index<4096;index++)if((byte)response[index+1]!=(byte)((index*17+23)%256))return 3;
            char tail='\0';if(client.Read(&tail,1)!=0||!client.IsOpen)return 4;
            bool caught=false;try{client.ReadExactly(&tail,1);}catch(ref EndOfStreamException error){caught=true;}
            if(!caught||!client.IsOpen)return 5;
            client.Write(((nuint)1).reinterpret<readonly(char)*>(),0);if(client.Read(((nuint)1).reinterpret<char*>(),0)!=0)return 6;
            client.Close();client.Close();if(client.IsOpen)return 7;return 0;
        }
        int Two(IpEndpoint endpoint){
            TcpClient first=TcpClient::Connect(endpoint);TcpClient second=TcpClient::Connect(endpoint);
            {TcpClient moved=move first;first=new();if(first.IsOpen||!moved.IsOpen||!second.IsOpen)return 11;moved.Close();}
            char value=(char)85;second.Write(&value,1);second.ShutdownSend();value='\0';second.ReadExactly(&value,1);
            if((byte)value!=(byte)170||!second.IsOpen)return 12;
            if(second.Read(&value,1)!=0||!second.IsOpen)return 13;
            second.Close();first.Close();if(second.IsOpen||first.IsOpen)return 14;return 0;
        }
        int main(int argc,char** argv){
            if(argc!=3)return 90;IpEndpoint endpoint=IpEndpoint::Parse(argv[1]);int result=0;
            if(argv[2][0]=='1')result=Two(endpoint);else result=Single(endpoint);if(result!=0)return result;
            nuint raw=socket(2,1,6);if(raw!=(nuint)0-1){closesocket(raw);return 91;}
            if(WSAGetLastError()!=10093)return 92;return 0;
        }
        """;
}
