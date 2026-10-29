namespace gflat.Tests.Platform;

public sealed class WindowsTcpClientStubTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task DeterministicNativeStubsCheckOwnershipErrorsAddressesAndPartialIoOnEveryHost(int optimization)
    {
        Assert.Equal(8,IntPtr.Size);
        string original=File.ReadAllText(WindowsTcpClientTests.CopiedClient),fixture=original;
        foreach(var replacement in Replacements)
        {
            Assert.Equal(1,fixture.Split(replacement.Declaration,StringSplitOptions.None).Length-1);
            fixture=fixture.Replace(replacement.Declaration,replacement.Definition,StringComparison.Ordinal);
        }
        string restored=fixture;
        foreach(var replacement in Replacements)restored=restored.Replace(replacement.Definition,replacement.Declaration,StringComparison.Ordinal);
        Assert.Equal(original,restored);
        string directory=Path.Combine(Path.GetTempPath(),"gflat-windows-tcp-stub-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory,"TcpClient.original.gf"),original);
            File.WriteAllText(Path.Combine(directory,"TcpClient.stub.gf"),fixture);
            File.WriteAllText(Path.Combine(directory,"main.gf"),Program);
            File.WriteAllText(Path.Combine(directory,"SocketException.gf"),File.ReadAllText(WindowsTcpClientTests.CopiedException));
            string ir=Compiler.Emit(WindowsTcpClientTests.SourceFiles(fixture,Program),options:new(){NoStdLib=true,InternalizeFunctions=true});
            foreach(var replacement in Replacements)
            {
                string symbol=replacement.Declaration.Split('(')[0].Split(' ').Last();
                Assert.DoesNotContain("@"+symbol+"(",ir); // No unmodified Windows import remains.
            }
            string path=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");File.WriteAllText(path,ir);
            var compile=NativeToolchain.Compile(path,output,optimizationLevel:optimization);
            File.WriteAllText(Path.Combine(directory,"compile.stdout"),compile.StandardOutput);
            File.WriteAllText(Path.Combine(directory,"compile.stderr"),compile.StandardError);
            Assert.True(compile.ExitCode==0,compile.StandardError);
            var result=NativeToolchain.Run(output,[],20000);
            File.WriteAllText(Path.Combine(directory,"run.stdout"),result.StandardOutput);
            File.WriteAllText(Path.Combine(directory,"run.stderr"),result.StandardError);
            File.WriteAllText(Path.Combine(directory,"transcript.txt"),$"Stub logic only; no real WinSock ABI call. Optimization={optimization}; exit={result.ExitCode}");
            Assert.True(result.ExitCode==0,$"TCP stub exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        catch(Exception error){error.Data["WindowsTcpDirectory"]=directory;File.WriteAllText(Path.Combine(directory,"failure.txt"),error.ToString());throw;}
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static readonly (string Declaration,string Definition)[] Replacements=
    {
        ("private extern int WSAStartup(ushort version, void* data) nothrow;","""
        private int WSAStartup(ushort version,void* data)nothrow{
            Probe::Starts++;if(version!=(ushort)514||(nuint)data%8!=0){Probe::Bad=true;return 10022;}
            if(Probe::StartupError!=0){Probe::LastError=9999;return Probe::StartupError;}
            Probe::Active++;*((ushort*)data)=Probe::Version;return 0;
        }
        """),
        ("private extern int WSACleanup() nothrow;","""
        private int WSACleanup()nothrow{
            Probe::CleanupCalls++;if(Probe::Active<=0){Probe::Bad=true;return Probe::Fail(10093);}
            if(Probe::CleanupFailures>0){Probe::CleanupFailures--;return Probe::Fail(10036);}
            Probe::Active--;Probe::LastError=7002;return 0;
        }
        """),
        ("private extern int WSAGetLastError() nothrow;","private int WSAGetLastError()nothrow{return Probe::LastError;}"),
        ("private extern nuint socket(int family, int kind, int protocol) nothrow;","""
        private nuint socket(int family,int kind,int protocol)nothrow{
            Probe::Sockets++;if(Probe::Active<=0||family!=Probe::Family||kind!=1||protocol!=6){Probe::Bad=true;Probe::LastError=10022;return (nuint)0-1;}
            if(Probe::SocketFailure){Probe::LastError=10055;return (nuint)0-1;}
            nuint result=Probe::Next;Probe::Next++;return result;
        }
        """),
        ("private extern int connect(nuint socket, readonly(void)* address, int length) nothrow;","""
        private int connect(nuint socket,readonly(void)* address,int length)nothrow{
            Probe::Connects++;if(!Probe::Usable(socket))return Probe::Fail(10038);
            int expectedLength=16;if(Probe::Family==23)expectedLength=28;
            if(length!=expectedLength||(nuint)address%4!=0){Probe::Bad=true;return Probe::Fail(10022);}
            readonly(byte)* data=(readonly(byte)*)address;
            byte[16] ipv6=[(byte)0x20,(byte)1,(byte)0x0d,(byte)0xb8,(byte)0x12,(byte)0x34,(byte)0x56,(byte)0x78,(byte)0x90,(byte)0xab,(byte)0xcd,(byte)0xef,(byte)1,(byte)2,(byte)3,(byte)4];
            for(int index=0;index<length;index++){
                byte expected=0;if(index==0)expected=(byte)Probe::Family;else if(index==2)expected=(byte)0x12;else if(index==3)expected=(byte)0x34;
                else if(Probe::Family==2){if(index==4)expected=(byte)192;else if(index==6)expected=(byte)2;else if(index==7)expected=(byte)17;}
                else if(index>=8&&index<24)expected=ipv6[(nuint)(index-8)];
                if(data[index]!=expected)Probe::Bad=true;
            }
            if(Probe::ConnectFailure)return Probe::Fail(10061);return 0;
        }
        """),
        ("private extern int recv(nuint socket, char* data, int count, int flags) nothrow;","""
        private int recv(nuint socket,char* data,int count,int flags)nothrow{
            Probe::Reads++;if(!Probe::Usable(socket))return Probe::Fail(10038);
            if(count<=0||flags!=0)Probe::Bad=true;
            if(Probe::ExpectCap&&count!=2147483647)Probe::Bad=true;
            if(Probe::CheckAddress&&((nuint)data!=Probe::Base+(nuint)Probe::Received||count!=Probe::Total-Probe::Received))Probe::Bad=true;
            if(Probe::ReadMode==2)return Probe::Fail(10054);
            if(Probe::ReadMode==1||(Probe::ReadMode==3&&Probe::Received==3))return 0;
            int amount=count;if(amount>2)amount=2;if(Probe::ReadMode==3&&amount>3-Probe::Received)amount=3-Probe::Received;
            for(int index=0;index<amount;index++)data[index]=(char)(48+Probe::Received+index);
            Probe::Received+=amount;return amount;
        }
        """),
        ("private extern int send(nuint socket, readonly(char)* data, int count, int flags) nothrow;","""
        private int send(nuint socket,readonly(char)* data,int count,int flags)nothrow{
            Probe::Writes++;if(!Probe::Usable(socket))return Probe::Fail(10038);
            if(count<=0||flags!=0)Probe::Bad=true;
            if(Probe::ExpectCap&&count!=2147483647)Probe::Bad=true;
            if(Probe::CheckAddress&&((nuint)data!=Probe::Base+(nuint)Probe::Sent||count!=Probe::Total-Probe::Sent))Probe::Bad=true;
            if(Probe::WriteMode==2||(Probe::WriteMode==3&&Probe::Sent>=2))return Probe::Fail(10054);
            if(Probe::WriteMode==1)return 0;
            int amount=count;if(amount>2)amount=2;
            for(int index=0;index<amount;index++)if(data[index]!=(char)((Probe::Sent+index)*37))Probe::Bad=true;
            Probe::Sent+=amount;return amount;
        }
        """),
        ("private extern int shutdown(nuint socket, int how) nothrow;","""
        private int shutdown(nuint socket,int how)nothrow{
            Probe::Shutdowns++;if(!Probe::Usable(socket))return Probe::Fail(10038);if(how!=1)Probe::Bad=true;
            if(Probe::ShutdownFailure)return Probe::Fail(10058);return 0;
        }
        """),
        ("private extern int closesocket(nuint socket) nothrow;","""
        private int closesocket(nuint socket)nothrow{
            Probe::CloseCalls++;if(!Probe::Usable(socket))return Probe::Fail(10038);
            if(Probe::CloseFailures>0){Probe::CloseFailures--;return Probe::Fail(10038);}
            Probe::Closed=Probe::Closed|(1ul<<(int)socket);Probe::LastError=7001;return 0;
        }
        """)
    };

    private const string Program="""
        using Windows;using std::IO;using std::Net;
        public static class Probe{
            public static bool Bad;public static int Starts;public static int Active;public static int CleanupCalls;
            public static int Sockets;public static int Connects;public static int CloseCalls;public static int Reads;public static int Writes;public static int Shutdowns;
            public static int LastError;public static int StartupError;public static ushort Version;public static int Family;
            public static bool SocketFailure;public static bool ConnectFailure;public static bool ShutdownFailure;
            public static int CloseFailures;public static int CleanupFailures;public static nuint Next;public static ulong Closed;
            public static int ReadMode;public static int WriteMode;public static int Received;public static int Sent;
            public static bool ExpectCap;public static bool CheckAddress;public static nuint Base;public static int Total;
            public static int Fail(int error)nothrow{LastError=error;return -1;}
            public static bool Usable(nuint handle)nothrow{
                if(Active<=0||handle>=Next||handle>=32){Bad=true;return false;}
                if((Closed&(1ul<<(int)handle))!=0ul){Bad=true;return false;}return true;
            }
            // Bad intentionally survives Reset so every earlier scenario remains checked.
            public static void Reset()nothrow{
                Starts=0;Active=0;CleanupCalls=0;Sockets=0;Connects=0;CloseCalls=0;Reads=0;Writes=0;Shutdowns=0;
                LastError=0;StartupError=0;Version=(ushort)514;Family=2;SocketFailure=false;ConnectFailure=false;ShutdownFailure=false;
                CloseFailures=0;CleanupFailures=0;Next=0;Closed=0ul;ReadMode=0;WriteMode=0;Received=0;Sent=0;
                ExpectCap=false;CheckAddress=false;Base=0;Total=0;
            }
        }
        IpEndpoint Endpoint(){return IpEndpoint::Parse("192.0.2.17:4660");}
        int main(){
            Probe::Reset();
            {
                TcpClient closed=new();if(closed.IsOpen)return 1;closed.Close();closed.Close();int closedErrors=0;
                try{closed.Read((char*)1,0);}catch(ref IOException error){closedErrors++;}
                try{closed.ReadExactly((char*)1,0);}catch(ref IOException error){closedErrors++;}
                try{closed.Write((readonly(char)*)1,0);}catch(ref IOException error){closedErrors++;}
                try{closed.ShutdownSend();}catch(ref IOException error){closedErrors++;}
                if(closedErrors!=4)return 2;
            }
            if(Probe::Starts!=0||Probe::CleanupCalls!=0||Probe::CloseCalls!=0||Probe::Reads!=0||Probe::Writes!=0||Probe::Shutdowns!=0)return 3;
            Probe::Reset();Probe::StartupError=10091;bool caught=false;
            try{TcpClient value=TcpClient::Connect(Endpoint());}catch(ref SocketException error){caught=error.GetCode()==10091;}
            if(!caught||Probe::Starts!=1||Probe::Active!=0||Probe::CleanupCalls!=0||Probe::Sockets!=0)return 4;
            Probe::Reset();Probe::Version=(ushort)257;caught=false;
            try{TcpClient value=TcpClient::Connect(Endpoint());}catch(ref SocketException error){caught=error.GetCode()==10092;}
            if(!caught||Probe::Starts!=1||Probe::CleanupCalls!=1||Probe::Active!=0||Probe::Sockets!=0)return 5;
            Probe::Reset();Probe::SocketFailure=true;caught=false;
            try{TcpClient value=TcpClient::Connect(Endpoint());}catch(ref SocketException error){caught=error.GetCode()==10055;}
            if(!caught||Probe::CleanupCalls!=1||Probe::CloseCalls!=0||Probe::Active!=0||Probe::Connects!=0)return 6;
            Probe::Reset();Probe::ConnectFailure=true;caught=false;
            try{TcpClient value=TcpClient::Connect(Endpoint());}catch(ref SocketException error){caught=error.GetCode()==10061;}
            if(!caught||Probe::CloseCalls!=1||Probe::CleanupCalls!=1||Probe::Active!=0||Probe::LastError!=7002)return 7;
            Probe::Reset();
            {
                TcpClient original=TcpClient::Connect(Endpoint());TcpClient moved=move original;original=new();
                if(original.IsOpen||!moved.IsOpen||Probe::Next!=1||Probe::Active!=1)return 8;
                moved.Close();moved.Close();if(moved.IsOpen||Probe::CloseCalls!=1||Probe::CleanupCalls!=1||Probe::Active!=0)return 9;
            }
            if(Probe::CloseCalls!=1||Probe::CleanupCalls!=1)return 10;
            Probe::Reset();Probe::Family=23;
            {TcpClient value=TcpClient::Connect(IpEndpoint::Parse("[2001:db8:1234:5678:90ab:cdef:102:304]:4660"));}
            if(Probe::Starts!=1||Probe::CloseCalls!=1||Probe::CleanupCalls!=1||Probe::Active!=0)return 11;
            Probe::Reset();
            {
                TcpClient first=TcpClient::Connect(Endpoint());TcpClient second=TcpClient::Connect(Endpoint());
                first.Close();if(Probe::Active!=1||!second.IsOpen||first.IsOpen)return 12;
                char value='\0';second.Write(&value,1);second.Close();
            }
            if(Probe::Starts!=2||Probe::CleanupCalls!=2||Probe::CloseCalls!=2||Probe::Active!=0)return 13;
            Probe::Reset();
            {
                TcpClient value=TcpClient::Connect(Endpoint());Probe::CloseFailures=1;caught=false;
                try{value.Close();}catch(ref SocketException error){caught=error.GetCode()==10038;}
                if(!caught||!value.IsOpen||Probe::Active!=1||Probe::CloseCalls!=1||Probe::CleanupCalls!=0)return 14;
                value.Close();if(value.IsOpen||Probe::Active!=0||Probe::CloseCalls!=2||Probe::CleanupCalls!=1)return 15;
            }
            Probe::Reset();
            {
                TcpClient value=TcpClient::Connect(Endpoint());Probe::CleanupFailures=1;caught=false;
                try{value.Close();}catch(ref SocketException error){caught=error.GetCode()==10036;}
                if(!caught||value.IsOpen||Probe::Active!=1||Probe::CloseCalls!=1||Probe::CleanupCalls!=1)return 16;
                value.Close();if(Probe::Active!=0||Probe::CloseCalls!=1||Probe::CleanupCalls!=2)return 17;
            }
            Probe::Reset();
            {
                TcpClient value=TcpClient::Connect(Endpoint());int before=Probe::Reads;
                if(value.Read((char*)1,0)!=0)return 18;value.ReadExactly((char*)1,0);value.Write((readonly(char)*)1,0);
                if(Probe::Reads!=before||Probe::Writes!=0)return 19;
                char[8] buffer;for(nuint index=0;index<8;index++)buffer[index]=(char)165;
                Probe::Base=(nuint)&buffer[1];Probe::Total=6;Probe::CheckAddress=true;
                if(value.Read(&buffer[1],6)!=2||Probe::Reads!=1||buffer[1]!=(char)48||buffer[2]!=(char)49||buffer[3]!=(char)165)return 20;
                Probe::Received=0;Probe::Reads=0;value.ReadExactly(&buffer[1],6);
                if(Probe::Reads!=3)return 21;for(nuint index=0;index<6;index++)if(buffer[index+1]!=(char)(48+index))return 22;
                if(buffer[0]!=(char)165||buffer[7]!=(char)165)return 23;
                for(nuint index=0;index<8;index++)buffer[index]=(char)165;
                Probe::Received=0;Probe::Reads=0;IByteReader* input=&value;
                std::IO::ReadExactly(input,&buffer[1],6);
                if(Probe::Reads!=3||Probe::Received!=6)return 37;
                for(nuint index=0;index<6;index++)if(buffer[index+1]!=(char)(48+index))return 38;
                if(buffer[0]!=(char)165||buffer[7]!=(char)165)return 39;
                std::IO::ReadExactly(input,(char*)1,0);
                if(Probe::Reads!=3||Probe::Received!=6)return 40;
                for(nuint index=0;index<8;index++)buffer[index]=(char)165;
                Probe::Received=0;Probe::Reads=0;Probe::ReadMode=3;Probe::Total=5;caught=false;
                try{value.ReadExactly(&buffer[1],5);}catch(ref EndOfStreamException error){caught=true;}
                if(!caught||Probe::Reads!=3||Probe::Received!=3||!value.IsOpen||buffer[0]!=(char)165)return 24;
                for(nuint index=0;index<3;index++)if(buffer[index+1]!=(char)(48+index))return 25;
                for(nuint index=4;index<8;index++)if(buffer[index]!=(char)165)return 26;
                Probe::CheckAddress=false;Probe::ReadMode=1;if(value.Read(buffer,1)!=0||!value.IsOpen)return 27;
                Probe::ReadMode=2;caught=false;try{value.Read(buffer,1);}catch(ref SocketException error){caught=error.GetCode()==10054;}
                if(!caught||!value.IsOpen)return 28;
                Probe::ExpectCap=true;caught=false;try{value.Read(buffer,(nuint)2147483648ul);}catch(ref SocketException error){caught=error.GetCode()==10054;}
                if(!caught)return 29;Probe::ExpectCap=false;
                for(nuint index=0;index<6;index++)buffer[index+1]=(char)(index*37);
                Probe::Base=(nuint)&buffer[1];Probe::Total=6;Probe::CheckAddress=true;Probe::WriteMode=0;Probe::Sent=0;
                value.Write(&buffer[1],6);if(Probe::Writes!=3||Probe::Sent!=6)return 30;
                Probe::Sent=0;Probe::WriteMode=3;caught=false;
                try{value.Write(&buffer[1],6);}catch(ref SocketException error){caught=error.GetCode()==10054;}
                if(!caught||Probe::Sent!=2||!value.IsOpen)return 31;
                Probe::CheckAddress=false;Probe::WriteMode=1;Probe::LastError=9999;caught=false;
                try{value.Write(buffer,1);}catch(ref IOException error){caught=error.GetCode()==1;}
                if(!caught||!value.IsOpen)return 32;
                Probe::WriteMode=2;Probe::ExpectCap=true;caught=false;
                try{value.Write(buffer,(nuint)2147483648ul);}catch(ref SocketException error){caught=error.GetCode()==10054;}
                if(!caught)return 33;Probe::ExpectCap=false;
                Probe::ShutdownFailure=true;caught=false;try{value.ShutdownSend();}catch(ref SocketException error){caught=error.GetCode()==10058;}
                if(!caught||!value.IsOpen)return 34;Probe::ShutdownFailure=false;value.ShutdownSend();
                if(Probe::Shutdowns!=2)return 35;value.Close();
            }
            if(Probe::Active!=0||Probe::CloseCalls!=1||Probe::CleanupCalls!=1||Probe::Bad)return 36;return 0;
        }
        """;
}