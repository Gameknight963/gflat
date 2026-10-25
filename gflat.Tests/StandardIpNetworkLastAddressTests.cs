using System.Net;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class StandardIpNetworkLastAddressTests
{
    private const string Allocator="""
        using std;extern void*? malloc(nuint count);extern void free(void* storage);int attempts=0;bool deny=false;
        namespace Memory{
            public replace void*? Allocate(nuint count){attempts++;if(deny)return null;return malloc(count);}
            public replace void Free(void* storage)nothrow{free(storage);}
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryPrefixMatchesIndependentUnsignedNetworkRangeOracles(int optimization)
    {
        var body=new StringBuilder();var random=new Random(184953);
        foreach(int count in new[]{4,16})for(int prefix=0;prefix<=count*8;prefix++){
            byte[] raw=new byte[count];random.NextBytes(raw);
            AddRow(body,new IPAddress(raw).GetAddressBytes(),prefix);
        }
        byte[] mapped=[0,0,0,0,0,0,0,0,0,0,255,255,192,0,2,129];
        foreach(int prefix in new[]{0,31,32,63,64,65,95,96,97,127,128})AddRow(body,mapped,prefix);
        Run(Allocator+"int main(){int before=attempts;deny=true;"+body+"deny=false;if(attempts!=before)return 20;return 0;}",optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImmutableAndCopiedEndpointsKeepTheFamilyAndOriginalNetworkUnchanged(int optimization)
        =>Run(Allocator+"""
            IpAddress Observe(immutable(IpNetwork)* value)nothrow=>value.LastAddress;
            int main(){
                IpNetwork zero=new();IpAddress first=zero.NetworkAddress;IpAddress expected4=IpAddress::Parse("255.255.255.255");
                immutable(IpNetwork) v4=IpNetwork::Parse("192.0.2.128/31");immutable(IpNetwork) v6=IpNetwork::Parse("2001:db8::/65");
                IpAddress all6=IpAddress::Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");IpAddress zero6=IpAddress::Parse("::");IpNetwork entire6=IpNetwork::FromAddress(zero6,0);
                IpAddress single4=IpAddress::Parse("203.0.113.9");IpNetwork one4=new(single4,32);IpNetwork one6=new(all6,128);
                IpNetwork before4=v4;IpNetwork before6=v6;nuint hash4=v4.GetHashCode();nuint hash6=v6.GetHashCode();
                int before=attempts;deny=true;
                IpAddress last4=Observe(&v4);IpAddress last6=Observe(&v6);IpAddress defaultEnd=zero.LastAddress;IpAddress entireEnd=entire6.LastAddress;
                IpAddress only4=one4.LastAddress;IpAddress only6=one6.LastAddress;
                if(!defaultEnd.Equals(&expected4)||!defaultEnd.IsIpv4||!entireEnd.Equals(&all6)||entireEnd.IsIpv4)return 1;
                if(!only4.Equals(&single4)||!only6.Equals(&all6)||!last4.IsIpv4||last6.IsIpv4)return 2;
                if(!v4.Contains(&last4)||!v6.Contains(&last6)||v4.Contains(&last6)||v6.Contains(&last4))return 3;
                last4=new IpAddress();last6=new IpAddress();
                if(!v4.Equals(&before4)||!v6.Equals(&before6)||v4.GetHashCode()!=hash4||v6.GetHashCode()!=hash6)return 4;
                IpAddress repeated=Observe(&v4);if(repeated.Equals(&last4)||v4.PrefixLength!=31||v6.PrefixLength!=65)return 5;
                IpAddress repeatedFirst=zero.NetworkAddress;if(!repeatedFirst.Equals(&first))return 6;
                deny=false;if(attempts!=before)return 7;return 0;
            }
            """,optimization);

    private static void AddRow(StringBuilder body,byte[] raw,int prefix)
    {
        BigInteger value=new(raw,isUnsigned:true,isBigEndian:true);BigInteger block=BigInteger.One<<(raw.Length*8-prefix);
        BigInteger start=value/block*block,end=start+block-1;
        byte[] expected=FixedBytes(end,raw.Length),network=FixedBytes(start,raw.Length);
        body.Append("{byte["+raw.Length+"] input=["+string.Join(',',raw)+"];byte["+raw.Length+"] wanted=["+string.Join(',',expected)+"];byte["+raw.Length+"] first=["+string.Join(',',network)+"];");
        body.Append($"IpAddress address=IpAddress::FromBytes(input,{raw.Length});IpNetwork net=IpNetwork::FromAddress(address,{prefix});IpNetwork original=net;IpAddress last=net.LastAddress;IpAddress beginning=net.NetworkAddress;");
        body.Append("byte[18] bytes;for(nuint i=0;i<18;i++)bytes[i]=(byte)165;nuint written=99;");
        body.Append($"if(!last.TryWriteBytes(&bytes[1],16,ref written)||written!={raw.Length}||last.IsIpv4!={(raw.Length==4?"true":"false")})return 1;for(nuint i=0;i<{raw.Length};i++)if(bytes[i+1]!=wanted[i])return 2;");
        body.Append($"for(nuint i={raw.Length+1};i<18;i++)if(bytes[i]!=(byte)165)return 3;if(bytes[0]!=(byte)165)return 4;if(!net.Contains(&last)||!net.Equals(&original)||net.PrefixLength!={prefix})return 5;");
        body.Append($"if(!beginning.TryWriteBytes(&bytes[1],16,ref written)||written!={raw.Length})return 6;for(nuint i=0;i<{raw.Length};i++)if(bytes[i+1]!=first[i])return 7;}}");
    }
    private static byte[] FixedBytes(BigInteger value,int count){byte[] encoded=value.ToByteArray(isUnsigned:true,isBigEndian:true);var result=new byte[count];encoded.CopyTo(result,count-encoded.Length);return result;}
    private static void Run(string program,int optimization){var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories).Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("ip-network-last-address.gf",program));var result=CompilerTestHelper.Run(sources,optimization);Assert.True(result.ExitCode==0,$"IP network endpoint exit {result.ExitCode}: {result.StandardError}");}
}
