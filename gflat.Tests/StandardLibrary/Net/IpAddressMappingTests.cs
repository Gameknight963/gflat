using System.Net;
using System.Net.Sockets;

namespace gflat.Tests.Std.Net;

public sealed class IpAddressMappingTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("IP_ADDRESS_MAPPING.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MappingAndClassificationsMatchDotNetAcrossPrefixBoundaries(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_ip_mapping_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (byte[] bytes in Inputs())
            {
                IPAddress address = new(bytes);
                ulong flags = 0;
                if (address.AddressFamily == AddressFamily.InterNetwork) flags |= 1;
                else flags |= 2;
                if (address.IsIPv4MappedToIPv6) flags |= 4;
                if (address.IsIPv6Multicast) flags |= 8;
                if (address.IsIPv6LinkLocal) flags |= 16;
                if (address.IsIPv6SiteLocal) flags |= 32;
                if (address.IsIPv6UniqueLocal) flags |= 64;
                if (address.IsIPv6Teredo) flags |= 128;
                if (IPAddress.IsLoopback(address)) flags |= 256;
                writer.Write((ulong)bytes.Length); writer.Write(flags);
                writer.Write(bytes); writer.Write(new byte[16 - bytes.Length]);
                writer.Write(address.MapToIPv4().GetAddressBytes());
                writer.Write(address.MapToIPv6().GetAddressBytes());
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Net/IpAddress.gf"))
                .Append(new SourceFile("ip-mapping.gf", $$"""
                using Standard;
                using Standard::Net;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                ulong Classify(IpAddress value) nothrow
                {
                    ulong flags = 0ul;
                    if (value.IsIpv4) flags = flags | 1ul;
                    if (value.IsIpv6) flags = flags | 2ul;
                    if (value.IsIpv4MappedToIpv6) flags = flags | 4ul;
                    if (value.IsIpv6Multicast) flags = flags | 8ul;
                    if (value.IsIpv6LinkLocal) flags = flags | 16ul;
                    if (value.IsIpv6SiteLocal) flags = flags | 32ul;
                    if (value.IsIpv6UniqueLocal) flags = flags | 64ul;
                    if (value.IsIpv6Teredo) flags = flags | 128ul;
                    if (IpAddress::IsLoopback(value)) flags = flags | 256ul;
                    return flags;
                }
                bool Maps(IpAddress source, byte* v4, byte* v6) nothrow
                {
                    IpAddress first = source.MapToIpv4();
                    IpAddress second = source.MapToIpv6();
                    if (!first.IsIpv4 || !second.IsIpv6) return false;
                    byte[16] actual = default(byte[16]);
                    nuint written = 0;
                    if (!first.TryWriteBytes(&actual[0], 16, ref written) || written != 4) return false;
                    for (nuint i = 0; i < 4; i++) { if (actual[i] != v4[i]) return false; }
                    if (!second.TryWriteBytes(&actual[0], 16, ref written) || written != 16) return false;
                    for (nuint i = 0; i < 16; i++) { if (actual[i] != v6[i]) return false; }
                    if (source.IsIpv4 && !second.IsIpv4MappedToIpv6) return false;
                    return true;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[2] header = default(ulong[2]);
                    byte[16] input = default(byte[16]);
                    byte[4] v4 = default(byte[4]);
                    byte[16] v6 = default(byte[16]);
                    while (true)
                    {
                        nuint received = fread(&header[0], 1, 16, file!);
                        if (received == 0) break;
                        if (received != 16 || fread(&input[0], 1, 16, file!) != 16 || fread(&v4[0], 1, 4, file!) != 4 || fread(&v6[0], 1, 16, file!) != 16) return 2;
                        IpAddress address = IpAddress::FromBytes(&input[0], (nuint)header[0]);
                        IpAddress original = address;
                        if (Classify(address) != header[1] || !Maps(address, &v4[0], &v6[0])) return 3;
                        if (!address.Equals(&original)) return 4;
                    }
                    if (ferror(file!) != 0) return 5;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"IP mapping exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<byte[]> Inputs()
    {
        foreach (string text in new[] { "0.0.0.0", "127.0.0.1", "127.1.2.3", "126.255.255.255", "128.0.0.0", "255.255.255.255",
            "::", "::1", "::2", "::ffff:127.0.0.1", "::ffff:127.1.2.3", "::ffff:192.0.2.1", "::192.0.2.1", "2001:db8::1234" })
            yield return IPAddress.Parse(text).GetAddressBytes();
        // Every leading 16-bit prefix exercises all classification boundaries.
        for (int prefix = 0; prefix <= ushort.MaxValue; prefix++)
        {
            byte[] bytes = new byte[16];
            bytes[0] = (byte)(prefix >> 8); bytes[1] = (byte)prefix;
            bytes[15] = 1;
            yield return bytes;
        }
        var random = new Random(927381);
        foreach (int size in new[] { 4, 16 })
        for (int i = 0; i < 1024; i++)
        {
            byte[] bytes = new byte[size]; random.NextBytes(bytes);
            yield return bytes;
            if (size == 4) yield return new IPAddress(bytes).MapToIPv6().GetAddressBytes();
        }
    }
}
