using System.Net;
using System.Text;

namespace gflat.Tests.Std.Net;

public sealed class IpAddressSpanTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Net/IpAddress.gf"))
            .Append(new SourceFile("ip-address-spans.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"IP span exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("IP_ADDRESS_SPANS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SlicedInputAndNetworkBytesMatchDotNetWithGuardedDestinations(int optimization)
    {
        var inputs = new List<string> { "0.0.0.0", "255.255.255.255", "192.0.2.1", "::", "::1",
            "::ffff:192.0.2.1", "2001:0DB8:0000:0000:0000:0000:0000:0001",
            "ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255" };
        var random = new Random(812077);
        for (int i = 0; i < 96; i++)
        {
            byte[] bytes = new byte[i % 2 == 0 ? 4 : 16];
            random.NextBytes(bytes);
            if (bytes.Length == 16 && i % 3 == 0) Array.Clear(bytes, 2, 8);
            inputs.Add(new IPAddress(bytes).ToString());
        }
        var checks = new StringBuilder();
        foreach (string input in inputs)
        {
            IPAddress address = IPAddress.Parse(input);
            byte[] bytes = address.GetAddressBytes();
            checks.AppendLine($$"""
                {
                    byte[{{bytes.Length}}] expected = [{{string.Join(",", bytes.Select(value => "(byte)" + value))}}];
                    if (Check("{{input}}", {{input.Length}}, "{{address}}", &expected[0], {{bytes.Length}})) return 1;
                    IpAddress parsed = IpAddress::Parse(new Span<readonly(char)>("{{input}}", {{input.Length}}));
                    IpAddress binary = IpAddress::FromBytes(new Span<readonly(byte)>(&expected[0], {{bytes.Length}}));
                    if (!parsed.Equals(&binary)) return 2;
                }
                """);
        }
        Run(Support + """
            bool Check(readonly(char)* text, nuint count, readonly(char)* canonical,
                readonly(byte)* expected, nuint byteCount) nothrow
            {
                char[48] input;
                for (nuint i = 0; i < count; i++) input[i + 1] = text[i];
                input[count + 1] = '#';
                var span = new Span<readonly(char)>(&input[1], count);
                IpAddress value = new();
                if (!IpAddress::TryParse(span, &value)) return true;
                IpAddress binary = new();
                byte[18] bytes;
                for (nuint i = 0; i < byteCount; i++) bytes[i + 1] = expected[i];
                if (!IpAddress::TryFromBytes(new Span<readonly(byte)>(&bytes[1], byteCount), &binary) || !binary.Equals(&value)) return true;
                nuint length = 0;
                while (canonical[length] != '\0') length++;
                for (nuint capacity = 0; capacity <= length + 1; capacity++)
                {
                    char[48] output;
                    for (nuint i = 0; i < 48; i++) output[i] = '#';
                    nuint written = 999;
                    bool success = value.TryFormat(new Span<char>(&output[1], capacity), ref written);
                    if (success != (capacity >= length)) return true;
                    nuint wanted = 0;
                    if (success) wanted = length;
                    if (written != wanted) return true;
                    for (nuint i = 0; i < 48; i++)
                    {
                        char wantedByte = '#';
                        if (success && i >= 1 && i <= length) wantedByte = canonical[i - 1];
                        if (output[i] != wantedByte) return true;
                    }
                }
                for (nuint capacity = 0; capacity <= 17; capacity++)
                {
                    byte[20] output;
                    for (nuint i = 0; i < 20; i++) output[i] = (byte)90;
                    nuint written = 999;
                    bool success = value.TryWriteBytes(new Span<byte>(&output[1], capacity), ref written);
                    if (success != (capacity >= byteCount)) return true;
                    nuint wanted = 0;
                    if (success) wanted = byteCount;
                    if (written != wanted) return true;
                    for (nuint i = 0; i < 20; i++)
                    {
                        byte wantedByte = (byte)90;
                        if (success && i >= 1 && i <= byteCount) wantedByte = expected[i - 1];
                        if (output[i] != wantedByte) return true;
                    }
                }
                for (nuint i = 0; i < count; i++)
                {
                    char saved = input[i + 1];
                    input[i + 1] = '\0';
                    if (IpAddress::TryParse(span, &value) || !value.IsIpv4 || value.ByteCount != 4) return true;
                    input[i + 1] = saved;
                }
                return false;
            }
            """ + $$"""
            int main()
            {
                {{checks}}
                return attempts;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidAndEmptyViewsPreservePreflightAndFailureContracts(int optimization)
        => Run(Support + """
            int main()
            {
                IpAddress value = IpAddress::Loopback;
                IpAddress baseline = new();
                if (IpAddress::TryParse(new Span<readonly(char)>(), &value) || !value.Equals(&baseline)) return 1;
                nuint written = 999;
                if (value.TryFormat(new Span<char>(), ref written) || written != 0) return 2;
                if (value.TryWriteBytes(new Span<byte>(), ref written) || written != 0) return 3;
                for (nuint count = 0; count <= 20; count++)
                {
                    if (count == 4 || count == 16) continue;
                    var input = new Span<readonly(byte)>(((nuint)1).reinterpret<readonly(byte)*>(), count);
                    value = IpAddress::Loopback;
                    if (IpAddress::TryFromBytes(input, &value) || !value.Equals(&baseline)) return 4;
                    if (attempts != 0) return 5;
                }
                for (int form = 0; form < 3; form++)
                {
                    value = IpAddress::Loopback;
                    bool caught = false;
                    try
                    {
                        if (form == 0) value = IpAddress::Parse(new Span<readonly(char)>());
                        if (form == 1) value = IpAddress::Parse(new Span<readonly(char)>("01.2.3.4", 8));
                        if (form == 2) value = IpAddress::FromBytes(new Span<readonly(byte)>());
                    }
                    catch (Exception* error) { caught = true; }
                    IpAddress original = IpAddress::Loopback;
                    if (!caught || !value.Equals(&original)) return 6;
                }
                return 0;
            }
            """, optimization);

    private const string Support = """
        using Standard;
        using Standard::Memory;
        using Standard::Net;

        int attempts = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes) { attempts++; return null; }
            public replace void Free(void* storage) nothrow { }
        }
        """;
}
