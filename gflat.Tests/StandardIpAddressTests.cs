using System.Net;
using System.Text;

namespace gflat.Tests;

public sealed class StandardIpAddressTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task StrictValidFormsCanonicalCapacityAndOrderingMatchIndependentOracle(int optimization)
    {
        await WithDirectory(directory =>
        {
            var rows = new List<(string Input, string Canonical, byte[] Bytes)>();
            void Add(string input, string canonical) => rows.Add((input, canonical, IPAddress.Parse(input).GetAddressBytes()));
            Add("0.0.0.0", "0.0.0.0"); Add("255.255.255.255", "255.255.255.255");
            Add("1.2.3.4", "1.2.3.4"); Add("1.2.3.5", "1.2.3.5");
            Add("::", "::"); Add("::1", "::1"); Add("::ffff", "::ffff");
            Add("0:0:0:0:0:0:0:0", "::");
            Add("FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");
            Add("2001:0db8:0000:0000:0001:0000:0000:0001", "2001:db8::1:0:0:1");
            Add("1:0:0:2:0:0:3:4", "1::2:0:0:3:4");
            Add("1:0:2:0:0:3:0:0", "1:0:2::3:0:0");
            Add("1:2:3:4:5:6:0:8", "1:2:3:4:5:6:0:8");
            Add("1:2:3:4:5:6:7::", "1:2:3:4:5:6:7:0");
            Add("::1:2:3:4:5:6:7", "0:1:2:3:4:5:6:7");
            Add("::ffff:192.0.2.1", "::ffff:192.0.2.1");
            Add("0:0:0:0:0:FFFF:C000:0201", "::ffff:192.0.2.1");
            Add("::192.0.2.1", "::c000:201");
            Add("2001:db8::192.0.2.1", "2001:db8::c000:201");
            Add("1:2:3:4:5:6:192.0.2.1", "1:2:3:4:5:6:c000:201");
            // Maximum counted input length: six full hex words plus a dotted tail.
            Add("ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");
            var random = new Random(59524291);
            for (int sample = 0; sample < 120; sample++)
            {
                byte[] bytes = new byte[sample % 3 == 0 ? 4 : 16]; random.NextBytes(bytes);
                if (bytes.Length == 16 && sample % 4 == 0)
                    Array.Clear(bytes, 2 * (sample % 5), 4);
                var oracle = new IPAddress(bytes);
                string canonical = oracle.ToString();
                rows.Add((FullSpelling(bytes), canonical, bytes));
                rows.Add((canonical, canonical, bytes));
            }
            rows.Sort((left, right) => Compare(left.Bytes, right.Bytes));
            string manifest = Path.Combine(directory, "valid.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count);
                byte[] previous = [0, 0, 0, 0];
                foreach (var row in rows)
                {
                    WriteText(writer, row.Input); WriteText(writer, row.Canonical);
                    // Reconstruct an equivalent complete spelling from independent network bytes.
                    WriteText(writer, FullSpelling(row.Bytes));
                    writer.Write(row.Bytes.Length == 4 ? 1 : 0);
                    writer.Write(Math.Sign(Compare(row.Bytes, previous))); previous = row.Bytes;
                }
            }
            Run(directory, """
                using std; using std::Net; using std::Memory; using std::Collections;
                String ReadText(File* file){nuint count=(nuint)FileBinary::ReadInt32LittleEndian(file);
                    String result=new();result.Resize(count);Span<char> data=result.AsSpan();file.ReadExactly(data.Data!,count);return move result;}
                int main(int argc,char** argv){
                    File file=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&file);IpAddress previous=new();
                    for(int row=0;row<rows;row++){
                        String input=ReadText(&file);String expected=ReadText(&file);String full=ReadText(&file);
                        bool ipv4=FileBinary::ReadInt32LittleEndian(&file)!=0;int comparison=FileBinary::ReadInt32LittleEndian(&file);
                        IpAddress value=new();if(!IpAddress::TryParse(input.Data,input.Length,&value))return 1;
                        IpAddress same=IpAddress::Parse(full.Data,full.Length);IpAddress cstring=IpAddress::Parse(input.Data);
                        if(value.IsIpv4!=ipv4||!value.Equals(&same)||!value.Equals(&cstring)||value.GetHashCode()!=same.GetHashCode())return 2;
                        if(value.CompareTo(&previous)!=comparison||previous.CompareTo(&value)!=-comparison||value.CompareTo(&same)!=0)return 3;
                        char[41] output;
                        for(nuint capacity=0;capacity<=39;capacity++){
                            for(nuint i=0;i<41;i++)output[i]='!';nuint written=91;
                            bool success=value.TryFormat(&output[1],capacity,ref written);
                            if(success!=(capacity>=expected.Length))return 4;
                            if(!success){if(written!=0)return 5;for(nuint i=0;i<41;i++)if(output[i]!='!')return 6;}
                            else{if(written!=expected.Length||output[0]!='!')return 7;
                                for(nuint i=0;i<written;i++)if(output[i+1]!=expected.Data[i])return 8;
                                for(nuint i=written+1;i<41;i++)if(output[i]!='!')return 9;}
                        }
                        nuint written=9;if(value.TryFormat((char*)1,0,ref written)||written!=0)return 10;
                        previous=value;
                    }
                    file.Close();return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task StrictInvalidGrammarResetsOutputAndThrowingParseReportsExactError(int optimization)
    {
        await WithDirectory(directory =>
        {
            string[] invalid = ["", "1", "127.1", "2130706433", "0x7f000001", "0x7f.0.0.1",
                "01.2.3.4", "1.02.3.4", "1.2.003.4", "1.2.3.04", "00.0.0.0",
                "256.0.0.1", "-1.2.3.4", "+1.2.3.4", "1.2.3", "1.2.3.4.5", ".1.2.3",
                "1..2.3", "1.2.3.", " 1.2.3.4", "1.2.3.4 ", "1.2.3.4\r\n", "1.2.3.4:80",
                "[::1]", "::1%eth0", "::1/128", "::g", "12345::", ":", ":::1", "1:::2",
                "1::2::3", "1:2:3:4:5:6:7", "1:2:3:4:5:6:7:8:9", "1:2:3:4:5:6:7:8::",
                "::1:2:3:4:5:6:7:8", "1:2:3:4:5:6:7:", ":1:2:3:4:5:6:7", "1:2:3:4:5:192.0.2.1",
                "1:2:3:4:5:6:7:192.0.2.1", "::ffff:192.00.2.1", "::ffff:192.0.2.256",
                "::ffff:192.0.2.1:2", "192.0.2.1::", "::ffff:192.0.2", "::ffff:0xc0000201",
                " ::1", "::1\t", "::1\0", "\0::1", "::\0:1", "1.2\0.3.4", new string('1',46)];
            var rows = invalid.Select(Encoding.UTF8.GetBytes).ToList();
            rows.Add([58, 58, 128]); rows.Add([255, 46, 48, 46, 48, 46, 48]);
            string manifest = Path.Combine(directory, "invalid.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(rows.Count);
                foreach (byte[] row in rows) { writer.Write(row.Length); writer.Write(row); }
            }
            Run(directory, """
                using std; using std::Net; using std::Memory; using std::Collections;
                int main(int argc,char** argv){
                    File file=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&file);
                    IpAddress zero=new();IpAddress seed=IpAddress::Parse("2001:db8::1");
                    if(!zero.IsIpv4)return 1;char[39] output;nuint written=0;
                    if(!zero.TryFormat(output,39,ref written)||written!=7)return 2;
                    readonly(char)* zeroText="0.0.0.0";for(nuint i=0;i<7;i++)if(output[i]!=zeroText[i])return 2;
                    for(int row=0;row<rows;row++){
                        nuint length=(nuint)FileBinary::ReadInt32LittleEndian(&file);char[64] text;
                        file.ReadExactly(text,length);text[length]='\0';IpAddress value=seed;
                        if(IpAddress::TryParse(text,length,&value)||!value.Equals(&zero))return 3;
                        bool caught=false;
                        try{value=IpAddress::Parse(text,length);return 4;}
                        catch(Exception* error){caught=CString::Equals(error.GetMessage(),"Invalid IP address");}
                        if(!caught)return 5;
                        // C strings stop at NUL; exercise the overload only for genuinely NUL-free invalid rows.
                        bool nul=false;for(nuint i=0;i<length;i++)if(text[i]=='\0')nul=true;
                        if(!nul){value=seed;if(IpAddress::TryParse(text,&value)||!value.Equals(&zero))return 6;
                            caught=false;try{value=IpAddress::Parse(text);return 7;}
                            catch(Exception* error){caught=CString::Equals(error.GetMessage(),"Invalid IP address");}if(!caught)return 8;}
                    }
                    IpAddress value=seed;
                    if(IpAddress::TryParse((readonly(char)*)1,0,&value)||!value.Equals(&zero))return 9;
                    value=seed;if(IpAddress::TryParse((readonly(char)*)1,46,&value)||!value.Equals(&zero))return 10;
                    value=seed;if(IpAddress::TryParse((readonly(char)*)1,(nuint)0-(nuint)1,&value)||!value.Equals(&zero))return 11;
                    file.Close();return 0;
                }
                """, [manifest], optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SuccessfulOperationsAndTryFailuresAllocateNothingAndSupportBackedAliases(int optimization)
        => await WithDirectory(directory => Run(directory, """
            using std; using std::Net; using std::Memory; using std::Collections;
            int allocations=0;
            namespace Memory{public replace void*? Allocate(nuint count){allocations++;return null;}
                public replace void Free(void* value)nothrow{}}
            int main(){
                readonly(char)* text="ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255";
                readonly(char)* expected="ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
                IpAddress value=new();if(!IpAddress::TryParse(text,45,&value)||value.IsIpv4)return 1;
                IpAddress other=IpAddress::Parse(expected);IpAddress copied=other;
                if(!value.Equals(&other)||value.CompareTo(&other)!=0||value.GetHashCode()!=other.GetHashCode())return 2;
                IpAddress[4] backing;char* bytes=(char*)&backing[0];nuint extent=(nuint)sizeof(IpAddress)*4;
                if(extent<46)return 3;
                for(nuint i=0;i<45;i++)bytes[i]=text[i];bytes[45]='\0';
                if(!IpAddress::TryParse(bytes,45,&backing[0])||!backing[0].Equals(&other))return 4;
                for(nuint i=0;i<45;i++)bytes[i]=text[i];bytes[45]='\0';
                if(!IpAddress::TryParse(bytes,&backing[0])||!backing[0].Equals(&other))return 5;
                for(nuint i=0;i<extent;i++)bytes[i]='!';backing[0]=other;nuint written=91;
                if(!backing[0].TryFormat(bytes,39,ref written)||written!=39)return 6;
                for(nuint i=0;i<39;i++)if(bytes[i]!=expected[i])return 7;
                for(nuint i=39;i<extent;i++)if(bytes[i]!='!')return 8;
                readonly(char)* bad="::ffff:192.0.2.256";
                for(nuint i=0;i<18;i++)bytes[i]=bad[i];
                if(IpAddress::TryParse(bytes,18,&backing[0]))return 9;IpAddress zero=new();
                if(!backing[0].Equals(&zero))return 10;
                value=other;if(IpAddress::TryParse((readonly(char)*)1,0,&value)||!value.Equals(&zero))return 11;
                if(allocations!=0||!copied.Equals(&other))return 12;return 0;
            }
            """, [], optimization));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FamilyEqualityHashMapInterpolationAndWriterFailureUseValueContracts(int optimization)
        => await WithDirectory(directory => Run(directory, """
            using std; using std::Net; using std::Memory; using std::Collections;
            struct Sink:ITextWriter{
                public char* Data;public bool Fail;public int Calls;
                public Sink(char* data,bool fail){Data=data;Fail=fail;Calls=0;}
                public void Write(readonly(char)* data,nuint count){Calls++;if(count!=0)Data[0]=data[0];
                    if(Fail)throw new Exception("writer failure",73);for(nuint i=1;i<count;i++)Data[i]=data[i];}
            }
            int main(){
                IpAddress address=IpAddress::Parse("2001:0DB8:0:0:0:0:0:1");IpAddress same=IpAddress::Parse("2001:db8::1");
                IpAddress ipv4=IpAddress::Parse("192.0.2.1");IpAddress mapped=IpAddress::Parse("::ffff:192.0.2.1");
                if(ipv4.Equals(&mapped)||ipv4.CompareTo(&mapped)!=-1||mapped.CompareTo(&ipv4)!=1)return 1;
                HashMap<IpAddress,int> counts=new();counts.Add(address,42);counts.Add(ipv4,7);counts.Add(mapped,9);
                if(counts[&same]!=42||!counts.ContainsKey(&same)||counts[&ipv4]!=7||counts[&mapped]!=9)return 2;
                String text=s$"ip={address}";if(!text.Equals("ip=2001:db8::1"))return 3;
                char[41] output;for(nuint i=0;i<41;i++)output[i]='!';
                Sink sink=new(&output[1],false);IFormattable* formatted=&address;formatted.Format(&sink);
                if(sink.Calls!=1||output[0]!='!'||output[12]!='!')return 4;
                readonly(char)* expected="2001:db8::1";for(nuint i=0;i<11;i++)if(output[i+1]!=expected[i])return 4;
                output[1]='!';Sink failing=new(&output[1],true);
                try{formatted.Format(&failing);return 5;}catch(Exception* error){if(error.GetCode()!=73)return 6;}
                if(failing.Calls!=1||output[1]!='2'||!address.Equals(&same))return 7;return 0;
            }
            """, [], optimization));

    [Fact]
    public void SelectiveSourcesNeedNoStringOrLibcImplementationAndKeepTryOperationsNothrow()
    {
        string[] names = ["core/Net/IpAddress.gf", "core/Text.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/IHashable.gf", "core/Hashing/primitive_hashing.gf", "core/Exception.gf"];
        var sources = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("contract.gf", """
                using std; using std::Net; using std::Memory; using std::Collections;
                namespace Memory{public void*? Allocate(nuint size){return null;}public void Free(void* value)nothrow{}}
                bool Check(readonly(char)* text,nuint length,char* output,nuint capacity,ref nuint written)nothrow{
                    IpAddress value=new();bool valid=IpAddress::TryParse(text,length,&value);IpAddress other=new();
                    bool family=value.IsIpv4;bool equal=value.Equals(&other);int comparison=value.CompareTo(&other);nuint hash=value.GetHashCode();
                    return value.TryFormat(output,capacity,ref written)&&valid;
                }
                """));
        Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true });
    }

    private static int Compare(byte[] left, byte[] right)
        => left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.AsSpan().SequenceCompareTo(right);
    private static string FullSpelling(byte[] bytes)
        => bytes.Length == 4 ? string.Join(".", bytes) : string.Join(":", Enumerable.Range(0, 8).Select(i => ((bytes[i * 2] << 8) | bytes[i * 2 + 1]).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)));
    private static void WriteText(BinaryWriter writer, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static async Task WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-ip-address-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);try { action(directory); }finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }
    private static void Run(string directory, string program, string[] arguments, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("ip-address-test.gf", program));
        string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "main.exe");
        File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
        var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
        Assert.True(compilation.ExitCode == 0, compilation.StandardError);
        var result = NativeToolchain.Run(executable, arguments, 30000);
        Assert.True(result.ExitCode == 0, $"IP address exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
