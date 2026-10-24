using System.Globalization;
using System.Net;
using System.Text;

namespace gflat.Tests;

public sealed class StandardIpNetworkTests
{
    private const string Counters="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);
        int allocated=0;int released=0;int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;void*? value=malloc(count);if(value!=null)allocated++;return value;}
            public replace void Free(void* storage)nothrow{released++;free(storage);}
        }
        """;
    private const string ReadText="""
        String ReadText(File* input){nuint count=(nuint)FileBinary::ReadInt32LittleEndian(input);String text=new();text.Resize(count);
            if(count!=0){Span<char> bytes=text.AsSpan();input.ReadExactly(bytes.Data!,count);}return move text;}
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EveryPrefixMatchesIndependentPerBitMasksAndStrictFamilyMembership(int optimization)
    {
        var rows=new List<(byte[] Address,int Prefix)>();var random=new Random(730149);
        foreach(int length in new[]{4,16})for(int prefix=0;prefix<=length*8;prefix++)for(int sample=0;sample<3;sample++){
            byte[] bytes=new byte[length];random.NextBytes(bytes);rows.Add((bytes,prefix));
        }
        byte[] mapped=[0,0,0,0,0,0,0,0,0,0,255,255,192,0,2,129];
        foreach(int prefix in new[]{0,64,95,96,97,120,128})rows.Add((mapped.ToArray(),prefix));
        foreach(int length in new[]{4,16})foreach(int prefix in new[]{0,1,7,8,31,32,63,64,95,96,127,128}.Where(prefix=>prefix<=length*8))
            rows.Add((Enumerable.Repeat((byte)255,length).ToArray(),prefix));
        await WithDirectory(directory=>{
            string path=Path.Combine(directory,"masks.bin");
            using(var writer=new BinaryWriter(File.Create(path))){
                writer.Write(rows.Count);
                foreach(var row in rows){
                    byte[] network=Mask(row.Address,row.Prefix);byte[] last=network.ToArray();
                    for(int bit=row.Prefix;bit<last.Length*8;bit++)Set(last,bit,true);
                    byte[] outside=network.ToArray();if(row.Prefix!=0)Set(outside,row.Prefix-1,!Get(outside,row.Prefix-1));
                    writer.Write(row.Address.Length);writer.Write(row.Prefix);writer.Write(row.Address.SequenceEqual(network)?1:0);writer.Write(row.Prefix==0?0:1);
                    writer.Write(row.Address);writer.Write(network);writer.Write(last);writer.Write(outside);WriteText(writer,Canonical(network)+"/"+row.Prefix.ToString(CultureInfo.InvariantCulture));
                }
            }
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);byte[16] raw;byte[16] network;byte[16] last;byte[16] outside;byte[16] actual;
                        for(int row=0;row<rows;row++){
                            nuint count=(nuint)FileBinary::ReadInt32LittleEndian(&input);int prefix=FileBinary::ReadInt32LittleEndian(&input);bool canonical=FileBinary::ReadInt32LittleEndian(&input)!=0;bool hasOutside=FileBinary::ReadInt32LittleEndian(&input)!=0;
                            if(count!=4&&count!=16)return 1;
                            input.ReadExactly((char*)&raw[0],count);input.ReadExactly((char*)&network[0],count);input.ReadExactly((char*)&last[0],count);input.ReadExactly((char*)&outside[0],count);String text=ReadText(&input);
                            IpAddress address=IpAddress::FromBytes(raw,count);IpAddress networkAddress=IpAddress::FromBytes(network,count);IpAddress final=IpAddress::FromBytes(last,count);IpAddress other=IpAddress::FromBytes(outside,count);
                            int before=attempts;deny=true;
                            IpNetwork normalized=IpNetwork::FromAddress(address,prefix);IpNetwork strict=new(networkAddress,prefix);IpNetwork parsed=new();
                            if(!IpNetwork::TryParse(text.Data,text.Length,&parsed)||!normalized.Equals(&strict)||!parsed.Equals(&strict)||normalized.PrefixLength!=prefix)return 2;
                            IpAddress emitted=normalized.NetworkAddress;nuint written=0;if(!emitted.TryWriteBytes(actual,16,ref written)||written!=count||emitted.IsIpv4!=(count==4))return 3;
                            for(nuint i=0;i<count;i++)if(actual[i]!=network[i])return 4;
                            if(!normalized.Contains(&address)||!normalized.Contains(&networkAddress)||!normalized.Contains(&final)||(hasOutside&&normalized.Contains(&other)))return 5;
                            byte[16] zero;for(nuint i=0;i<16;i++)zero[i]=0;nuint otherCount=4;if(count==4)otherCount=16;IpAddress otherFamily=IpAddress::FromBytes(zero,otherCount);
                            if(normalized.Contains(&otherFamily))return 6;
                            char[43] output;if(!normalized.TryFormat(output,43,ref written)||written!=text.Length)return 7;
                            for(nuint i=0;i<written;i++)if(output[i]!=text.Data[i])return 8;
                            deny=false;if(attempts!=before)return 9;
                            bool caught=false;try{IpNetwork checked=new(address,prefix);if(!canonical||!checked.Equals(&strict))return 10;}
                            catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"IP network address has host bits"))return 11;caught=true;}
                            if(caught==canonical)return 12;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 13;return 0;
                }
                """,[path],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CountedCStringGrammarAndEveryOutputCapacityFollowTheStrictFixedContracts(int optimization)
    {
        var rows=new List<(byte[] Input,string? Counted,string? CString)>();
        void Good(string input,string canonical)=>rows.Add((Encoding.ASCII.GetBytes(input),canonical,canonical));
        Good("0.0.0.0/0","0.0.0.0/0");Good("0.0.0.0/00","0.0.0.0/0");Good("0.0.0.0/000","0.0.0.0/0");Good("192.0.2.0/024","192.0.2.0/24");
        Good("255.255.255.255/032","255.255.255.255/32");Good("::/000","::/0");Good("::1/128","::1/128");Good("2001:0DB8::/032","2001:db8::/32");
        Good("::ffff:192.0.2.0/120","::ffff:192.0.2.0/120");Good("::ffff:0:0/096","::ffff:0.0.0.0/96");Good("::fffe:0:0/095","::fffe:0:0/95");
        Good("ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255/128","ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff/128");
        foreach(string bad in new[]{"","/","192.0.2.0","192.0.2.1/24","0.0.0.1/0","::1/0","::1/127","192.0.2.0/+24","192.0.2.0/-1","192.0.2.0/ 24","192.0.2.0/24 ",
            " 192.0.2.0/24","192.0.2.0/33","::/129","::/999","::/0000","::/1/2","::/","192.000.2.0/24","192.0.2.0/٢٤","::%1/64"})
            rows.Add((Encoding.UTF8.GetBytes(bad),null,null));
        rows.Add((Encoding.ASCII.GetBytes("192.0.2.0/24\0suffix"),null,"192.0.2.0/24"));rows.Add(([58,58,47,48,255],null,null));rows.Add((Enumerable.Repeat((byte)'a',50).ToArray(),null,null));
        await WithDirectory(directory=>{
            string path=Path.Combine(directory,"grammar.bin");using(var writer=new BinaryWriter(File.Create(path))){writer.Write(rows.Count);foreach(var row in rows){WriteText(writer,row.Input);writer.Write(row.Counted!=null?1:0);writer.Write(row.CString!=null?1:0);WriteText(writer,row.Counted??"");WriteText(writer,row.CString??"");}}
            Run(directory,Counters+ReadText+"""
                int main(int argc,char** argv){
                    {
                        File input=File::OpenRead(argv[1]);int rows=FileBinary::ReadInt32LittleEndian(&input);IpNetwork zero=new();IpNetwork initial=IpNetwork::Parse("10.0.0.0/8");
                        for(int row=0;row<rows;row++){
                            String text=ReadText(&input);bool expectedCounted=FileBinary::ReadInt32LittleEndian(&input)!=0;bool expectedCString=FileBinary::ReadInt32LittleEndian(&input)!=0;
                            String canonicalCounted=ReadText(&input);String canonicalCString=ReadText(&input);IpNetwork counted=initial;IpNetwork terminated=initial;int before=attempts;deny=true;
                            if(IpNetwork::TryParse(text.Data,text.Length,&counted)!=expectedCounted||IpNetwork::TryParse(text.Data,&terminated)!=expectedCString)return 1;
                            if((!expectedCounted&&!counted.Equals(&zero))||(!expectedCString&&!terminated.Equals(&zero)))return 2;
                            if(expectedCString){
                                char[45] output;
                                for(nuint capacity=0;capacity<=43;capacity++){
                                    for(nuint i=0;i<45;i++)output[i]='Z';nuint written=99;bool success=terminated.TryFormat(&output[1],capacity,ref written);
                                    if(success!=(capacity>=canonicalCString.Length))return 3;
                                    if(!success){if(written!=0)return 4;for(nuint i=0;i<45;i++)if(output[i]!='Z')return 5;}
                                    else{if(written!=canonicalCString.Length||output[0]!='Z')return 6;for(nuint i=0;i<written;i++)if(output[i+1]!=canonicalCString.Data[i])return 7;for(nuint i=written+1;i<45;i++)if(output[i]!='Z')return 8;}
                                }
                                nuint written=99;if(terminated.TryFormat((char*)1,0,ref written)||written!=0)return 9;
                            }
                            deny=false;if(attempts!=before)return 10;
                            bool caught=false;IpNetwork result=initial;try{result=IpNetwork::Parse(text.Data,text.Length);if(!expectedCounted||!result.Equals(&counted))return 11;}
                            catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid IP network"))return 12;caught=true;}
                            if(caught==expectedCounted||(!expectedCounted&&!result.Equals(&initial)))return 13;
                            caught=false;result=initial;try{result=IpNetwork::Parse(text.Data);if(!expectedCString||!result.Equals(&terminated))return 14;}
                            catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"Invalid IP network"))return 15;caught=true;}
                            if(caught==expectedCString||(!expectedCString&&!result.Equals(&initial)))return 16;
                        }
                        input.Close();
                    }
                    if(allocated!=released)return 17;return 0;
                }
                """,[path],optimization);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AliasesFamilyBoundariesInvalidConstructionAndFormattedHashKeysKeepTheirContracts(int optimization)
        =>await WithDirectory(directory=>Run(directory,Counters+"""
            struct FailWriter:ITextWriter{public int Calls;public FailWriter(){Calls=0;}public void Write(readonly(char)* data,nuint count){Calls++;if(count!=13||data[0]!='2')throw new Exception("wrong format");throw new Exception("sink",73);}}
            int main(){
                {
                    IpNetwork zero=new();IpAddress zeroAddress=zero.NetworkAddress;if(!zeroAddress.IsIpv4||zero.PrefixLength!=0)return 1;
                    IpNetwork original=IpNetwork::Parse("2001:db8::/032");IpNetwork saved=original;
                    IpNetwork[8] backing;char* storage=(char*)&backing[0];nuint extent=(nuint)sizeof(IpNetwork)*8;if(extent<128||extent>512)return 2;
                    nuint[3] offsets=[0,1,(nuint)sizeof(IpNetwork)-1];readonly(char)* text="2001:db8::/032";nuint count=0;while(text[count]!='\0')count++;
                    int before=attempts;deny=true;
                    for(nuint sample=0;sample<3;sample++){
                        nuint offset=offsets[sample];for(nuint i=0;i<extent;i++)storage[i]='Z';for(nuint i=0;i<count;i++)storage[offset+i]=text[i];
                        if(!IpNetwork::TryParse(storage+offset,count,&backing[0])||!backing[0].Equals(&original))return 3;
                        for(nuint i=0;i<extent;i++)storage[i]='Z';for(nuint i=0;i<count;i++)storage[offset+i]=text[i];storage[offset+count]='\0';
                        if(!IpNetwork::TryParse(storage+offset,&backing[0])||!backing[0].Equals(&original))return 4;
                        backing[0]=original;char[512] snapshot;for(nuint i=0;i<extent;i++)snapshot[i]=storage[i];nuint written=99;
                        if(!backing[0].TryFormat(storage+offset,13,ref written)||written!=13)return 5;readonly(char)* expected="2001:db8::/32";
                        for(nuint i=0;i<extent;i++){if(i>=offset&&i<offset+13){if(storage[i]!=expected[i-offset])return 6;}else if(storage[i]!=snapshot[i])return 7;}
                        backing[0]=original;for(nuint i=0;i<extent;i++)snapshot[i]=storage[i];written=99;
                        if(backing[0].TryFormat(storage+offset,12,ref written)||written!=0)return 8;for(nuint i=0;i<extent;i++)if(storage[i]!=snapshot[i])return 9;
                    }
                    nuint[3] badCounts=[0,50,(nuint)0-(nuint)1];
                    for(nuint i=0;i<3;i++){IpNetwork result=original;if(IpNetwork::TryParse((readonly(char)*)1,badCounts[i],&result)||!result.Equals(&zero))return 10;}
                    readonly(char)* invalid="192.0.2.1/24";nuint invalidCount=0;while(invalid[invalidCount]!='\0')invalidCount++;
                    for(nuint i=0;i<invalidCount;i++)storage[i]=invalid[i];if(IpNetwork::TryParse(storage,invalidCount,&backing[0])||!backing[0].Equals(&zero))return 11;
                    for(nuint i=0;i<invalidCount;i++)storage[i]=invalid[i];storage[invalidCount]='\0';if(IpNetwork::TryParse(storage,&backing[0])||!backing[0].Equals(&zero))return 11;
                    deny=false;if(attempts!=before)return 12;
                    for(int family=0;family<2;family++){
                        IpAddress address=new();int maximum=32;if(family==1){address=IpAddress::Parse("::");maximum=128;}
                        int[4] prefixes=[-1,maximum+1,-2147483648,2147483647];
                        for(nuint i=0;i<4;i++)for(int operation=0;operation<2;operation++){
                            bool caught=false;IpNetwork result=original;try{if(operation==0)result=new IpNetwork(address,prefixes[i]);else result=IpNetwork::FromAddress(address,prefixes[i]);}
                            catch(ref Exception error){if(!CString::Equals(error.GetMessage(),"IP network prefix is out of range"))return 13;caught=true;}
                            if(!caught||!result.Equals(&original))return 14;
                        }
                    }
                    IpAddress native=IpAddress::Parse("192.0.2.1");IpAddress mapped=IpAddress::Parse("::ffff:192.0.2.1");
                    IpNetwork v4=IpNetwork::Parse("192.0.2.0/24");IpNetwork v6=IpNetwork::Parse("::ffff:192.0.2.0/120");IpNetwork world4=new();IpNetwork world6=IpNetwork::Parse("::/0");
                    if(!v4.Contains(&native)||v4.Contains(&mapped)||v6.Contains(&native)||!v6.Contains(&mapped)||world4.Contains(&mapped)||world6.Contains(&native)||!world6.Contains(&mapped))return 15;
                    IpNetwork lower=IpNetwork::FromAddress(mapped,95);IpNetwork lowerExpected=IpNetwork::Parse("::fffe:0:0/95");if(!lower.Equals(&lowerExpected))return 16;
                    IpAddress lowerAddress=lower.NetworkAddress;if(lowerAddress.IsIpv4||lowerAddress.ByteCount!=16)return 17;
                    HashMap<IpNetwork,int> map=new();HashSet<IpNetwork> set=new();map.Add(original,42);if(!set.Add(original))return 18;
                    IpNetwork same=IpNetwork::Parse("2001:db8::/32");if(map[&same]!=42||!set.Contains(&same)||same.GetHashCode()!=original.GetHashCode()||!same.Equals(&original)||world4.Equals(&world6))return 19;
                    IpNetwork zeroOne=IpNetwork::Parse("0.0.0.0/1");if(zero.Equals(&zeroOne)||zeroOne.Equals(&zero))return 24;
                    String label=s$"net={original}";if(!CString::Equals(label.Data,"net=2001:db8::/32"))return 20;
                    FailWriter writer=new();bool caught=false;try{original.Format(&writer);}catch(ref Exception error){if(error.GetCode()!=73)return 21;caught=true;}
                    if(!caught||writer.Calls!=1||!original.Equals(&saved))return 22;
                }
                if(allocated!=released)return 23;return 0;
            }
            """,[],optimization));

    [Fact]
    public void TryObserversCompileNothrowWithoutStringCollectorOrLibcSource()
    {
        string[] names=["IpNetwork.gf","IpAddress.gf","Text.gf","IEquatable.gf","IComparable.gf","IHashable.gf","Hashing.gf","Exception.gf"];
        var sources=names.Select(name=>new SourceFile(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core",name))))
            .Append(new SourceFile("network-contract.gf","""
                using std;namespace Memory{public void*? Allocate(nuint count){return null;}public void Free(void* storage)nothrow{}}
                bool Observe(immutable(IpNetwork)* network,readonly(char)* input,nuint count,char* output,nuint capacity,ref nuint written)nothrow{
                    IpNetwork parsed=new();bool valid=IpNetwork::TryParse(input,count,&parsed);IpAddress address=network.NetworkAddress;int prefix=network.PrefixLength;
                    nuint hash=network.GetHashCode();bool equal=network.Equals(&parsed);bool contains=network.Contains(&address);
                    return network.TryFormat(output,capacity,ref written)&&valid&&contains;
                }
                """));
        string ir=Compiler.Emit(sources,options:new(){NoStdLib=true,NoBuiltins=true});
        string definition=ir.Split('\n').Single(line=>line.StartsWith("define ")&&line.Contains("$Observe("));Assert.Contains("nounwind",definition);
    }

    private static bool Get(byte[] bytes,int bit)=>(bytes[bit/8]&(1<<(7-bit%8)))!=0;
    private static void Set(byte[] bytes,int bit,bool value){byte mask=(byte)(1<<(7-bit%8));if(value)bytes[bit/8]|=mask;else bytes[bit/8]&=(byte)~mask;}
    private static byte[] Mask(byte[] address,int prefix){byte[] result=new byte[address.Length];for(int bit=0;bit<prefix;bit++)Set(result,bit,Get(address,bit));return result;}
    private static string Canonical(byte[] bytes)
    {
        if(bytes.Length==16&&bytes.Take(10).All(value=>value==0)&&bytes[10]==255&&bytes[11]==255)
            return "::ffff:"+string.Join('.',bytes.Skip(12));
        return new IPAddress(bytes).ToString();
    }
    private static void WriteText(BinaryWriter writer,string text)=>WriteText(writer,Encoding.ASCII.GetBytes(text));
    private static void WriteText(BinaryWriter writer,byte[] bytes){writer.Write(bytes.Length);writer.Write(bytes);}
    private static async Task WithDirectory(Action<string> action)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gflat_ip_network_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{action(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory);}
    }
    private static void Run(string directory,string source,string[] arguments,int optimization)
    {
        var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("ip-network.gf",source));
        string input=Path.Combine(directory,"main.ll"),output=Path.Combine(directory,"main.exe");File.WriteAllText(input,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
        var compiled=NativeToolchain.Compile(input,output,optimizationLevel:optimization);Assert.True(compiled.ExitCode==0,compiled.StandardError);
        var result=NativeToolchain.Run(output,arguments,30000);Assert.True(result.ExitCode==0,$"IpNetwork exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
