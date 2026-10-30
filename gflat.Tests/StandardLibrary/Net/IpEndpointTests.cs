using System.Globalization;
using System.Net;
using System.Text;

namespace gflat.Tests.Std.Net;

public sealed class IpEndpointTests
{
    private const string Counters = """
        using Standard;using Standard::Net;
        extern void*? malloc(nuint bytes);extern void free(void* storage);
        int attempts=0;int allocated=0;int released=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint bytes){attempts++;if(deny)return null;void*? value=malloc(bytes);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NumericAddressAndPortOracleCanonicalFormsAndEveryCapacityAllocateNothing(int optimization)
    {
        var rows = new List<(string Address, ushort Port, string Canonical)>();
        void Add(string address, ushort port, string canonical) => rows.Add((address, port, canonical));
        Add("0.0.0.0", 0, "0.0.0.0:0");
        Add("255.255.255.255", 65535, "255.255.255.255:65535");
        Add("::", 0, "[::]:0");
        Add("::1", 443, "[::1]:443");
        Add("2001:0DB8:0000:0:0:0:0:1", 80, "[2001:db8::1]:80");
        Add("1:0:0:2:0:0:3:4", 7, "[1::2:0:0:3:4]:7");
        Add("::FFFF:192.0.2.1", 8080, "[::ffff:192.0.2.1]:8080");
        Add("::192.0.2.1", 53, "[::c000:201]:53");
        Add("ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255", 65535,
            "[ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff]:65535");
        var random = new Random(827165);
        ushort[] ports = [0, 1, 9, 10, 99, 100, 999, 1000, 9999, 10000, 65535];
        for (int sample = 0; sample < 24; sample++)
        {
            byte[] bytes = new byte[sample % 2 == 0 ? 4 : 16];random.NextBytes(bytes);
            string address = new IPAddress(bytes).ToString();
            ushort port = ports[sample % ports.Length];
            Add(address, port, (bytes.Length == 4 ? address : "[" + address + "]") + ":" + port.ToString(CultureInfo.InvariantCulture));
        }
        var body = new StringBuilder(Counters).Append("int main(){int before=attempts;deny=true;");
        foreach (var row in rows)
        {
            byte[] bytes = IPAddress.Parse(row.Address).GetAddressBytes();
            string input = (bytes.Length == 4 ? row.Address : "[" + row.Address + "]") + ":" + row.Port.ToString("D5", CultureInfo.InvariantCulture);
            body.Append($$"""
                {readonly(char)* input="{{input}}";readonly(char)* expected="{{row.Canonical}}";
                 IpEndpoint value=new();IpEndpoint terminated=new();
                 if(!IpEndpoint::TryParse(input,{{input.Length}},&value)||!IpEndpoint::TryParse(input,&terminated)||!value.Equals(&terminated))return 1;
                 IpAddress address=value.Address;if(address.IsIpv4!={{(bytes.Length == 4 ? "true" : "false")}}||value.Port!=(ushort){{row.Port}})return 2;
                 byte[16] actual;nuint count=0;if(!address.TryWriteBytes(actual,16,ref count)||count!={{bytes.Length}})return 3;
                """);
            for (int index = 0; index < bytes.Length; index++) body.Append($"if(actual[{index}]!=(byte){bytes[index]})return 4;");
            body.Append($$"""
                 IpEndpoint typed=new(address,(ushort){{row.Port}});IpEndpoint copy=typed;
                 if(!typed.Equals(&value)||!copy.Equals(&value)||typed.GetHashCode()!=value.GetHashCode())return 5;
                 IpEndpoint parsed=IpEndpoint::Parse(input,{{input.Length}});IpEndpoint cParsed=IpEndpoint::Parse(input);
                 if(!parsed.Equals(&value)||!cParsed.Equals(&value))return 6;
                 char[51] output;
                 for(nuint capacity=0;capacity<=49;capacity++){
                     for(nuint i=0;i<51;i++)output[i]='!';nuint written=99;
                     bool success=value.TryFormat(&output[1],capacity,ref written);
                     if(success!=(capacity>={{row.Canonical.Length}}))return 7;
                     if(!success){if(written!=0)return 8;for(nuint i=0;i<51;i++)if(output[i]!='!')return 9;}
                     else{if(written!={{row.Canonical.Length}}||output[0]!='!')return 10;
                         for(nuint i=0;i<written;i++)if(output[i+1]!=expected[i])return 11;
                         for(nuint i=written+1;i<51;i++)if(output[i]!='!')return 12;}
                 }
                 nuint written=99;if(value.TryFormat(((nuint)1).reinterpret<char*>(),0,ref written)||written!=0)return 13;
                }
                """);
        }
        body.Append("deny=false;if(attempts!=before)return 14;return 0;}");
        Run(body.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StrictGrammarFailureResetAndThrowingParsersKeepTheirContracts(int optimization)
    {
        string[] invalid = ["", ":", "192.0.2.1", "192.0.2.1:", ":80", "[::1]", "[]:80", "[::1]:", "[::1:80", "::1:80",
            "[192.0.2.1]:80", "192.0.2.1:80:90", "[[::1]]:80", "[::1]]:80", "[::1]80", "[::1]:80]",
            "192.0.2.1:-1", "192.0.2.1:+1", "192.0.2.1:65536", "[::]:99999", "[::]:000000", "[::]:000080",
            "192.0.2.1:80 ", " 192.0.2.1:80", "[::1]: 80", "192.0.02.1:80", "256.0.2.1:80", "localhost:80",
            "[fe80::1%3]:80", "[fe80::1%eth0]:80", "[::1]/128:80", "192.0.2.1/32:80", "[::1]:8x",
            new string('a',54)];
        var body = new StringBuilder(Counters).Append("int main(){IpEndpoint initial=IpEndpoint::Parse(\"[2001:db8::1]:443\");IpEndpoint zero=new();");
        foreach (string input in invalid)
        {
            body.Append($$"""
                {readonly(char)* text="{{input}}";IpEndpoint counted=initial;IpEndpoint terminated=initial;int before=attempts;deny=true;
                 if(IpEndpoint::TryParse(text,{{input.Length}},&counted)||IpEndpoint::TryParse(text,&terminated)||!counted.Equals(&zero)||!terminated.Equals(&zero))return 1;
                 deny=false;if(attempts!=before)return 2;
                 for(int kind=0;kind<2;kind++){bool caught=false;IpEndpoint result=initial;
                     try{if(kind==0)result=IpEndpoint::Parse(text,{{input.Length}});else result=IpEndpoint::Parse(text);}
                     catch(ref Exception error){caught=true;readonly(char)* message=error.GetMessage();readonly(char)* expected="Invalid IP endpoint";
                         for(nuint i=0;i<20;i++)if(message[i]!=expected[i])return 3;}
                     if(!caught||!result.Equals(&initial))return 4;} }
                """);
        }
        body.Append("""
            {int before=attempts;deny=true;nuint[3] counts=[0,54,(nuint)0-(nuint)1];
             for(nuint i=0;i<3;i++){IpEndpoint value=initial;if(IpEndpoint::TryParse(((nuint)1).reinterpret<readonly(char)*>(),counts[i],&value)||!value.Equals(&zero))return 5;}
             IpEndpoint value=initial;nuint written=99;if(value.TryFormat(((nuint)1).reinterpret<char*>(),0,ref written)||written!=0)return 6;
             readonly(char)* embedded="192.0.2.1:80\0suffix";value=initial;
             if(IpEndpoint::TryParse(embedded,19,&value)||!value.Equals(&zero))return 7;
             if(!IpEndpoint::TryParse(embedded,&value)||value.Port!=(ushort)80)return 8;
             char[9] exact=['1','.','2','.','3','.','4',':','0'];
             if(!IpEndpoint::TryParse(exact,9,&value)||value.Port!=(ushort)0)return 9;
             char[10] nonAscii=['1','.','2','.','3','.','4',':',(char)255,'\0'];
             if(IpEndpoint::TryParse(nonAscii,9,&value)||!value.Equals(&zero))return 10;
             char[54] unterminated;for(nuint i=0;i<54;i++)unterminated[i]='a';
             if(IpEndpoint::TryParse(unterminated,&value)||!value.Equals(&zero))return 10;
             deny=false;if(attempts!=before)return 11;}
            if(allocated!=released)return 12;return 0;}
            """);
        Run(body.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BackedAliasesFamilyPortEqualityAndBorrowedWritersFollowValueContracts(int optimization)
        => Run(Counters + """
            struct Sink:ITextWriter{
                public char* Data;public bool Fail;public int Calls;public nuint Count;
                public Sink(char* data,bool fail){Data=data;Fail=fail;Calls=0;Count=0;}
                public void Write(readonly(char)* data,nuint count){Calls++;Count=count;if(count!=0)Data[0]=data[0];
                    if(Fail)throw new Exception("endpoint sink failure",73);for(nuint i=1;i<count;i++)Data[i]=data[i];}
            }
            int main(){
                IpEndpoint zero=new();IpAddress zeroAddress=zero.Address;if(!zeroAddress.IsIpv4||zero.Port!=(ushort)0)return 1;
                IpEndpoint value=IpEndpoint::Parse("[2001:0DB8::1]:00443");IpEndpoint same=IpEndpoint::Parse("[2001:db8::1]:443");
                IpAddress address=value.Address;IpEndpoint typed=new(address,(ushort)443);
                IpEndpoint otherPort=new(address,(ushort)444);
                IpEndpoint v4=IpEndpoint::Parse("192.0.2.1:443");IpEndpoint mapped=IpEndpoint::Parse("[::ffff:192.0.2.1]:443");
                int before=attempts;deny=true;
                if(!value.Equals(&same)||!value.Equals(&typed)||value.GetHashCode()!=same.GetHashCode()||value.Equals(&otherPort)||v4.Equals(&mapped))return 2;
                IpEndpoint[8] backing;char* bytes=(&backing[0]).reinterpret<char*>();nuint extent=(nuint)sizeof(IpEndpoint)*8;
                if(extent<80||extent>512)return 3;
                readonly(char)* input="[2001:0DB8::1]:00443";readonly(char)* expected="[2001:db8::1]:443";
                nuint[3] offsets=[0,1,(nuint)sizeof(IpEndpoint)-1];
                for(nuint sample=0;sample<3;sample++){
                    nuint offset=offsets[sample];
                    for(nuint i=0;i<extent;i++)bytes[i]='!';for(nuint i=0;i<20;i++)bytes[offset+i]=input[i];
                    if(!IpEndpoint::TryParse(bytes+offset,20,&backing[0])||!backing[0].Equals(&value))return 4;
                    for(nuint i=0;i<extent;i++)bytes[i]='!';for(nuint i=0;i<20;i++)bytes[offset+i]=input[i];bytes[offset+20]='\0';
                    if(!IpEndpoint::TryParse(bytes+offset,&backing[0])||!backing[0].Equals(&value))return 5;
                    backing[0]=value;char[512] snapshot;for(nuint i=0;i<extent;i++)snapshot[i]=bytes[i];nuint written=99;
                    if(!backing[0].TryFormat(bytes+offset,17,ref written)||written!=17)return 6;
                    for(nuint i=0;i<extent;i++){if(i>=offset&&i<offset+17){if(bytes[i]!=expected[i-offset])return 7;}else if(bytes[i]!=snapshot[i])return 8;}
                    backing[0]=value;for(nuint i=0;i<extent;i++)snapshot[i]=bytes[i];written=99;
                    if(backing[0].TryFormat(bytes+offset,16,ref written)||written!=0)return 9;
                    for(nuint i=0;i<extent;i++)if(bytes[i]!=snapshot[i])return 10;
                    readonly(char)* invalid="[::1]:65536";for(nuint i=0;i<11;i++)bytes[offset+i]=invalid[i];
                    if(IpEndpoint::TryParse(bytes+offset,11,&backing[0])||!backing[0].Equals(&zero))return 11;
                    for(nuint i=0;i<11;i++)bytes[offset+i]=invalid[i];bytes[offset+11]='\0';
                    if(IpEndpoint::TryParse(bytes+offset,&backing[0])||!backing[0].Equals(&zero))return 11;
                }
                deny=false;if(attempts!=before)return 12;
                char[20] output;for(nuint i=0;i<20;i++)output[i]='!';
                Sink sink=new(&output[1],false);IFormattable* formattable=&value;formattable.Format(&sink);
                if(sink.Calls!=1||sink.Count!=17||output[0]!='!'||output[18]!='!'||output[19]!='!')return 13;
                for(nuint i=0;i<17;i++)if(output[i+1]!=expected[i])return 14;
                output[1]='!';Sink failure=new(&output[1],true);bool caught=false;
                try{formattable.Format(&failure);}catch(ref Exception error){caught=true;if(error.GetCode()!=73)return 15;}
                if(!caught||failure.Calls!=1||failure.Count!=17||output[1]!='['||!value.Equals(&same))return 16;
                if(allocated!=released)return 17;return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StringInterpolationAndHashMapUseFormattingEqualityAndHashInterfaces(int optimization)
    {
        const string program = """
            using Standard;using Standard::Net;using Standard::Text;using Standard::Collections;
            int main(){
                IpEndpoint endpoint=IpEndpoint::Parse("[2001:0DB8:0:0:0:0:0:1]:00443");
                String message=s$"Upstream: {endpoint}";
                if(!message.Equals("Upstream: [2001:db8::1]:443"))return 1;
                IpEndpoint equivalent=IpEndpoint::Parse("[2001:db8::1]:443");
                IpEndpoint differentPort=IpEndpoint::Parse("[2001:db8::1]:444");
                IpEndpoint ipv4=IpEndpoint::Parse("192.0.2.1:443");
                IpEndpoint mapped=IpEndpoint::Parse("[::ffff:192.0.2.1]:443");
                HashMap<IpEndpoint,int> values=new();values.Add(endpoint,42);
                int*? found=values.Find(&equivalent);
                if(found==null||*found! !=42||values.TryAdd(equivalent,99)||values.Count!=1)return 2;
                if(!values.TryAdd(differentPort,7)||!values.TryAdd(ipv4,8)||!values.TryAdd(mapped,9)||values.Count!=4)return 3;
                if(values[&endpoint]!=42||values[&equivalent]!=42||values[&differentPort]!=7||values[&ipv4]!=8||values[&mapped]!=9)return 4;
                return 0;
            }
            """;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("ip-endpoint-integration.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"IpEndpoint integration exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void SelectiveSourceTryObserversRemainNothrowWithoutStringCollectorOrNativeImplementation()
    {
        string ir = Compiler.Emit(Sources("""
            using Standard;using Standard::Net;
            namespace Memory{public void*? Allocate(nuint bytes)=>null;public void Free(void* storage)nothrow{}}
            bool Observe(immutable(IpEndpoint)* source,readonly(char)* text,nuint count,char* output,nuint capacity,ref nuint written)nothrow{
                IpEndpoint parsed=new();bool success=IpEndpoint::TryParse(text,count,&parsed);IpAddress address=source.Address;
                IpEndpoint typed=new(address,source.Port);bool equal=typed.Equals(source);nuint hash=source.GetHashCode();
                return source.TryFormat(output,capacity,ref written)&&success&&equal;
            }
            """), options: new() { NoStdLib = true, NoBuiltins = true });
        string definition = ir.Split('\n').Single(line => line.StartsWith("define ") && line.Contains("$Observe("));
        Assert.Contains("nounwind", definition);
    }

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] names = ["Net/IpEndpoint.gf", "Net/IpAddress.gf", "ITextWriter.gf", "IFormattable.gf", "IEquatable.gf", "IComparable.gf", "IHashable.gf", "Hashing/primitive_hashing.gf", "Exception.gf"];
        return names.Select(name => {
            string path = Path.Combine(AppContext.BaseDirectory, "Standard/core", name);
            return new SourceFile(path, File.ReadAllText(path));
        })
            .Append(new SourceFile("ip-endpoint-tests.gf", program));
    }
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"IpEndpoint exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
