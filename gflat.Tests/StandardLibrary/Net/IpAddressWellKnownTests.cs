using System.Net;
using System.Text;

namespace gflat.Tests.Std.Net;

public sealed class IpAddressWellKnownTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("IP_ADDRESS_WELL_KNOWN.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamedValuesMatchDotNetFamiliesBytesFormattingAndLoopbackWithoutAllocation(int optimization)
    {
        var program = new StringBuilder("""
            using Standard;
            using Standard::Net;
            using Standard::Text;

            extern "C" void free(void* storage);
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            int Check(IpAddress value, readonly(char)* expected, readonly(byte)* bytes, nuint count, bool loopback) nothrow
            {
                IpAddress parsed = new();
                if (!IpAddress::TryParse(expected, &parsed) || !value.Equals(&parsed)) return 1;
                if (value.ByteCount != count || value.IsIpv4 != (count == 4) || value.IsIpv6 != (count == 16)) return 2;
                if (IpAddress::IsLoopback(value) != loopback) return 3;
                byte[16] actual;
                nuint written = 0;
                if (!value.TryWriteBytes(actual, 16, ref written) || written != count) return 4;
                for (nuint i = 0; i < count; i++) if (actual[i] != bytes[i]) return 5;
                char[64] text;
                if (!value.TryFormat(text, 64, ref written)) return 6;
                nuint i = 0;
                while (expected[i] != (char)0)
                {
                    if (i >= written || text[i] != expected[i]) return 7;
                    i++;
                }
                if (i != written) return 8;
                return 0;
            }
            int main() nothrow
            {
            """);
        foreach (var (name, address) in new[] { ("Any", IPAddress.Any), ("Loopback", IPAddress.Loopback),
            ("Broadcast", IPAddress.Broadcast), ("None", IPAddress.None), ("Ipv6Any", IPAddress.IPv6Any),
            ("Ipv6Loopback", IPAddress.IPv6Loopback), ("Ipv6None", IPAddress.IPv6None) })
        {
            byte[] bytes = address.GetAddressBytes();
            program.AppendLine($"{{ byte[{bytes.Length}] expected = [{string.Join(",", bytes.Select(value => "(byte)" + value))}];");
            program.AppendLine($"if (Check(IpAddress::{name}, \"{address}\", expected, {bytes.Length}, {IPAddress.IsLoopback(address).ToString().ToLowerInvariant()}) != 0) return 10; }}");
        }
        program.AppendLine("""
                IpAddress original = IpAddress::Loopback;
                original = IpAddress::Any;
                IpAddress unchanged = IpAddress::Loopback;
                if (!IpAddress::IsLoopback(unchanged) || IpAddress::IsLoopback(original)) return 11;
                IpAddress any = IpAddress::Any;
                IpAddress ipv6Any = IpAddress::Ipv6Any;
                if (any.Equals(&ipv6Any)) return 12;
                return attempts;
            }
            """);
        var result = CompilerTestHelper.Run(StandardTestSources.Net.Append(new SourceFile("known-ip-address.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
