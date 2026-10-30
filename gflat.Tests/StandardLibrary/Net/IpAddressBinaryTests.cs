using System.Net;
using System.Text;

namespace gflat.Tests.Std.Net;

public sealed class IpAddressBinaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task NetworkBytesMatchIndependentAddressesAtEveryOutputCapacity(int optimization)
    {
        await WithDirectory(directory =>
        {
            List<byte[]> inputs = [new byte[4], [255, 255, 255, 255], [192, 0, 2, 1],
                [1, 128, 0, 255], new byte[16], Enumerable.Repeat((byte)255, 16).ToArray(),
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 192, 0, 2, 1],
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 192, 0, 2, 1],
                Enumerable.Range(0, 16).Select(i => (byte)(i * 17)).ToArray()];
            var random = new Random(4165952);
            for (int sample = 0; sample < 160; sample++)
            {
                byte[] bytes = new byte[sample % 2 == 0 ? 4 : 16]; random.NextBytes(bytes); inputs.Add(bytes);
            }
            string manifest = Path.Combine(directory, "addresses.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(inputs.Count);
                foreach (byte[] input in inputs)
                {
                    var address = new IPAddress(input);
                    byte[] text = Encoding.ASCII.GetBytes(address.ToString());
                    byte[] expected = address.GetAddressBytes();
                    Assert.Equal(input, expected);
                    writer.Write(expected.Length); writer.Write(expected); writer.Write(text.Length); writer.Write(text);
                }
            }
            Run(directory, """
                using std; using std::Text; using std::IO; using std::Net;
                int main(int argc,char** argv){
                    File file=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&file);
                    for(int row=0;row<rows;row++){
                        nuint count=(nuint)FileBinary::ReadInt32LittleEndian(&file);byte[16] expected;
                        file.ReadExactly((char*)expected,count);
                        nuint length=(nuint)FileBinary::ReadInt32LittleEndian(&file);char[46] text;
                        file.ReadExactly(text,length);text[length]='\0';
                        IpAddress fromText=IpAddress::Parse(text,length);IpAddress value=new();
                        if(!IpAddress::TryFromBytes(expected,count,&value)||value.ByteCount!=count||value.IsIpv4!=(count==4))return 1;
                        IpAddress throwing=IpAddress::FromBytes(expected,count);
                        if(!value.Equals(&fromText)||!value.Equals(&throwing)||value.GetHashCode()!=fromText.GetHashCode())return 2;
                        byte[22] output;
                        for(nuint capacity=0;capacity<=20;capacity++){
                            for(nuint i=0;i<22;i++)output[i]=90; nuint written=99;
                            bool success=value.TryWriteBytes(&output[1],capacity,ref written);
                            if(success!=(capacity>=count))return 3;
                            if(!success){if(written!=0)return 4;for(nuint i=0;i<22;i++)if(output[i]!=90)return 5;}
                            else{if(written!=count||output[0]!=90)return 6;
                                for(nuint i=0;i<count;i++)if(output[i+1]!=expected[i])return 7;
                                for(nuint i=count+1;i<22;i++)if(output[i]!=90)return 8;}
                        }
                        nuint written=9;if(value.TryWriteBytes(((nuint)1).reinterpret<byte*>(),0,ref written)||written!=0)return 9;
                        if(!value.Equals(&fromText))return 10;
                        for(nuint i=0;i<count;i++)expected[i]=(byte)(expected[i]+1);
                        if(!value.Equals(&fromText))return 11;
                    }
                    IpAddress ipv4=IpAddress::Parse("192.0.2.1");IpAddress mapped=IpAddress::Parse("::ffff:192.0.2.1");
                    if(ipv4.ByteCount!=4||mapped.ByteCount!=16||ipv4.Equals(&mapped))return 12;
                    file.Close();return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InvalidLengthsAvoidInputReadsResetTryResultsAndPreserveThrowingDestinations(int optimization)
        => await WithDirectory(directory => Run(directory, """
            using std; using std::Text; using std::IO; using std::Net;
            int main(){
                IpAddress zero=new();IpAddress seed=IpAddress::Parse("2001:db8::1234");
                for(nuint count=0;count<=33;count++){
                    if(count==4||count==16)continue;
                    IpAddress value=seed;
                    if(IpAddress::TryFromBytes(((nuint)1).reinterpret<readonly(byte)*>(),count,&value)||!value.Equals(&zero)||value.ByteCount!=4)return 1;
                    value=seed;bool caught=false;
                    try{value=IpAddress::FromBytes(((nuint)1).reinterpret<readonly(byte)*>(),count);return 2;}
                    catch(Exception* error){caught=CString::Equals(error.GetMessage(),"IP address requires 4 or 16 bytes");}
                    if(!caught||!value.Equals(&seed))return 3;
                }
                IpAddress value=seed;nuint maximum=(nuint)0-(nuint)1;
                if(IpAddress::TryFromBytes(((nuint)1).reinterpret<readonly(byte)*>(),maximum,&value)||!value.Equals(&zero))return 4;
                value=seed;bool caught=false;
                try{value=IpAddress::FromBytes(((nuint)1).reinterpret<readonly(byte)*>(),maximum);return 5;}
                catch(Exception* error){caught=CString::Equals(error.GetMessage(),"IP address requires 4 or 16 bytes");}
                if(!caught||!value.Equals(&seed))return 6;return 0;
            }
            """, [], optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task BinaryOperationsStageOverlappingStorageAndAllocateNothingOnSuccessOrTryFailure(int optimization)
        => await WithDirectory(directory => Run(directory, """
            using std; using std::Text; using std::IO; using std::Net;
            int allocations=0;int frees=0;
            namespace Memory{
                public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* value)nothrow{frees++;}
            }
            int main(){
                IpAddress[4] backing;byte* storage=(&backing[0]).reinterpret<byte*>();nuint extent=(nuint)sizeof(IpAddress)*4;
                if(extent<40||extent>128)return 1;byte[16] expected;
                nuint[3] offsets;offsets[0]=0;offsets[1]=1;offsets[2]=7;
                for(int family=0;family<2;family++){
                    nuint count=4;if(family!=0)count=16;
                    for(nuint i=0;i<count;i++)expected[i]=(byte)(i*17+3);
                    IpAddress original=IpAddress::FromBytes(expected,count);
                    for(int index=0;index<3;index++){
                        nuint offset=offsets[index];
                        for(nuint i=0;i<count;i++)storage[offset+i]=expected[i];
                        if(!IpAddress::TryFromBytes(storage+offset,count,&backing[0])||!backing[0].Equals(&original))return 2;
                        for(nuint i=0;i<extent;i++)storage[i]=90;
                        backing[0]=original;nuint written=91;
                        // Snapshot receiver bytes too: writes may touch only the requested output interval.
                        byte[128] before;for(nuint i=0;i<extent;i++)before[i]=storage[i];
                        if(!backing[0].TryWriteBytes(storage+offset,count,ref written)||written!=count)return 3;
                        for(nuint i=0;i<count;i++)if(storage[offset+i]!=expected[i])return 4;
                        for(nuint i=0;i<extent;i++)if((i<offset||i>=offset+count)&&storage[i]!=before[i])return 5;
                        backing[0]=original;for(nuint i=0;i<extent;i++)before[i]=storage[i];written=91;
                        if(backing[0].TryWriteBytes(storage+offset,count-1,ref written)||written!=0)return 6;
                        for(nuint i=0;i<extent;i++)if(storage[i]!=before[i])return 7;
                    }
                }
                IpAddress zero=new();backing[0]=IpAddress::FromBytes(expected,16);
                if(IpAddress::TryFromBytes(storage+1,15,&backing[0])||!backing[0].Equals(&zero))return 8;
                if(allocations!=0||frees!=0)return 9;return 0;
            }
            """, [], optimization));

    [Fact]
    public void SelectiveSourceBinaryTryContractNeedsNoStringCollectorOrLibcImplementation()
    {
        string[] names = ["core/Net/IpAddress.gf", "core/ITextWriter.gf", "core/IFormattable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/IHashable.gf", "core/Hashing/primitive_hashing.gf", "core/Exception.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("binary-contract.gf", """
                using std; using std::Text; using std::IO; using std::Net;
                namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* value)nothrow{}}
                bool Convert(readonly(byte)* input,nuint count,byte* output,nuint capacity,ref nuint written)nothrow{
                    IpAddress value=new();bool success=IpAddress::TryFromBytes(input,count,&value);nuint size=value.ByteCount;
                    return value.TryWriteBytes(output,capacity,ref written)&&success;
                }
                """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true });
        string body = System.Text.RegularExpressions.Regex.Match(ir,
            @"(?ms)^define [^\r\n]*@gflat\$Convert\([^\r\n]*\{\r?\n(?<body>.*?)^\}").Groups["body"].Value;
        Assert.NotEmpty(body); Assert.DoesNotContain("invoke", body);
        Assert.Contains("TryFromBytes", body); Assert.Contains("TryWriteBytes", body); Assert.Contains("ByteCount", body);
    }

    private static async Task WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-ip-address-binary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }
    private static void Run(string directory, string program, string[] arguments, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("ip-address-binary-test.gf", program));
        string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "main.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compilation.ExitCode == 0, compilation.StandardError);
        var result = NativeToolchain.Run(executable, arguments, 30000);
        Assert.True(result.ExitCode == 0, $"IP binary exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
