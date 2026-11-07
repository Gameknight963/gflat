namespace gflat.Tests.Std.Binary;

public sealed class ReverseEndiannessBufferTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("REVERSE_ENDIANNESS_BUFFERS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryIntegerWidthSupportsOverlapsAndRejectsShortDestinationsBeforeWriting(int optimization)
    {
        string[] types = ["byte", "sbyte", "ushort", "short", "uint", "int", "ulong", "long", "nuint", "nint", "extralong"];
        string template = """
                int Check_ELEMENT(T sample)
                {
                    T[12] original = default(T[12]);
                    T[12] actual = default(T[12]);
                    for (nuint i = 0; i < 12; i++) original[i] = sample ^ (T)(i * (nuint)0x12345679u);
                    for (nuint source = 0; source < 6; source++)
                    {
                        for (nuint destination = 0; destination < 6; destination++)
                        {
                            for (nuint length = 0; length <= 7; length++)
                            {
                                for (int form = 0; form < 2; form++)
                                {
                                    for (nuint i = 0; i < 12; i++) actual[i] = original[i];
                                    if (form == 0)
                                        { if (!BinaryPrimitives::TryReverseEndianness(&actual[source], length, &actual[destination], 12 - destination)) return 4; }
                                    else
                                        ReverseEndianness(new Span<readonly(T)>(&actual[source], length), new Span<T>(&actual[destination], 12 - destination));
                                    for (nuint i = 0; i < 12; i++)
                                    {
                                        T expected = original[i];
                                        if (i >= destination && i - destination < length)
                                            expected = BinaryPrimitives::ReverseEndianness(original[source + i - destination]);
                                        if (actual[i] != expected) return 1;
                                    }
                                    if (length == 0) continue;
                                    for (nuint i = 0; i < 12; i++) actual[i] = original[i];
                                    bool caught = false;
                                    try
                                    {
                                        if (form == 0) caught = !BinaryPrimitives::TryReverseEndianness(&actual[source], length, &actual[destination], length - 1);
                                        else ReverseEndianness(new Span<readonly(T)>(&actual[source], length), new Span<T>(&actual[destination], length - 1));
                                    }
                                    catch (Exception* error) { caught = true; }
                                    if (!caught) return 2;
                                    for (nuint i = 0; i < 12; i++) { if (actual[i] != original[i]) return 3; }
                                }
                            }
                        }
                    }
                    Span<readonly(T)> emptySource = new();
                    Span<T> emptyDestination = new();
                    ReverseEndianness(emptySource, emptyDestination);
                    return 0;
                }
            """;
        string checks = string.Join("\n", types.Select(type => System.Text.RegularExpressions.Regex.Replace(template.Replace("Check_ELEMENT", "Check_" + type), @"\bT\b", type)));
        string calls = string.Join("\n", types.Select(type => $"if (Check_{type}(({type})-1) != 0) return {Array.IndexOf(types, type) + 1};"));
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Binary/BinaryPrimitives.gf", "core/Binary/reverse_endianness_span.gf"))
            .Append(new SourceFile("reverse-endian-buffers.gf", $$"""
                using Standard;
                using Standard::Memory;
                using Standard::Binary;

                {{checks}}
                int main()
                {
                    {{calls}}
                    return 0;
                }
                """)), optimization);
        Assert.True(result.ExitCode == 0, $"Endian buffer exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
