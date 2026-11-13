namespace gflat.Tests.Std.Time;

public sealed class TimeSpanFractionalTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load("core/Time/TimeSpan.gf"))
            .Append(new SourceFile("fractional-timespan.gf", program));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TIME_SPAN_FRACTIONAL.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SubmillisecondComponentsAndTotalsMatchDotNetAcrossSignedTicks(int optimization)
    {
        var ticks = new SortedSet<long> { long.MinValue, long.MinValue + 1, long.MaxValue - 1, long.MaxValue, 0 };
        foreach (long scale in new long[] { 1, 10, 10000, 10000000 })
        foreach (long factor in new long[] { -1001, -1000, -999, -1, 0, 1, 999, 1000, 1001 })
        foreach (long delta in new long[] { -1, 0, 1 }) ticks.Add(scale * factor + delta);
        var random = new Random(741887);
        for (int i = 0; i < 256; i++) ticks.Add(random.NextInt64(long.MinValue, long.MaxValue));
        string path = Path.Combine(Path.GetTempPath(), "gflat_timespan_submillisecond_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (long tick in ticks)
                {
                    var value = new TimeSpan(tick);
                    writer.Write(tick);
                    writer.Write(value.Microseconds);
                    writer.Write(value.Nanoseconds);
                    writer.Write(BitConverter.DoubleToUInt64Bits(value.TotalMicroseconds));
                    writer.Write(BitConverter.DoubleToUInt64Bits(value.TotalNanoseconds));
                }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long ticks = 0l;
                    int microseconds = 0;
                    int nanoseconds = 0;
                    ulong expectedMicro = 0ul;
                    ulong expectedNano = 0ul;
                    while (true)
                    {
                        nuint read = fread(&ticks, 1, 8, file!);
                        if (read == 0) break;
                        if (read != 8 || fread(&microseconds, 1, 4, file!) != 4 || fread(&nanoseconds, 1, 4, file!) != 4
                            || fread(&expectedMicro, 1, 8, file!) != 8 || fread(&expectedNano, 1, 8, file!) != 8) return 2;
                        immutable(TimeSpan) value = TimeSpan::FromTicks(ticks);
                        if (value.Microseconds != microseconds || value.Nanoseconds != nanoseconds) return 3;
                        double totalMicro = value.TotalMicroseconds;
                        double totalNano = value.TotalNanoseconds;
                        if (totalMicro.reinterpret<ref immutable(ulong)>() != expectedMicro || totalNano.reinterpret<ref immutable(ulong)>() != expectedNano) return 4;
                        if (TimeSpan::FromMicroseconds(ticks / 10l).Ticks != ticks / 10l * 10l) return 5;
                    }
                    if (ferror(file!) != 0) return 6;
                    long minimum = -9223372036854775807l - 1l;
                    long maximum = 9223372036854775807l;
                    TimeSpan output = TimeSpan::FromTicks(123l);
                    bool caught = false;
                    try { output = TimeSpan::FromMicroseconds(minimum / 10l - 1l); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || output.Ticks != 123l) return 7;
                    caught = false;
                    try { output = TimeSpan::FromMicroseconds(maximum / 10l + 1l); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || output.Ticks != 123l) return 8;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Submillisecond TimeSpan exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FractionalFactoriesMatchDotNetAtTickAndOverflowBoundaries(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_timespan_fractional_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (byte unit = 0; unit < 6; unit++)
            foreach (double value in Inputs(unit))
            {
                bool valid = true;
                long ticks = 0;
                try { ticks = Create(unit, value).Ticks; }
                catch (ArgumentException) { valid = false; }
                catch (OverflowException) { valid = false; }
                writer.Write(unit);
                writer.Write(BitConverter.DoubleToUInt64Bits(value));
                writer.Write(ticks);
                writer.Write(valid ? 1 : 0);
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string program = $$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                TimeSpan Create(byte unit, double value)
                {
                    if (unit == (byte)0) return TimeSpan::FromMicroseconds(value);
                    if (unit == (byte)1) return TimeSpan::FromMilliseconds(value);
                    if (unit == (byte)2) return TimeSpan::FromSeconds(value);
                    if (unit == (byte)3) return TimeSpan::FromMinutes(value);
                    if (unit == (byte)4) return TimeSpan::FromHours(value);
                    return TimeSpan::FromDays(value);
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    byte unit = (byte)0;
                    ulong bits = 0ul;
                    long expected = 0l;
                    int valid = 0;
                    while (true)
                    {
                        nuint read = fread(&unit, 1, 1, file!);
                        if (read == 0) break;
                        if (fread(&bits, 1, 8, file!) != 8 || fread(&expected, 1, 8, file!) != 8 || fread(&valid, 1, 4, file!) != 4) return 2;
                        TimeSpan result = TimeSpan::FromTicks(123l);
                        bool caught = false;
                        try { result = Create(unit, bits.reinterpret<ref immutable(double)>()); }
                        catch (Exception* error) { caught = true; }
                        if (caught != (valid == 0)) return 3;
                        if (!caught && result.Ticks != expected) return 4;
                        if (caught && result.Ticks != 123l) return 5;
                    }
                    if (ferror(file!) != 0) return 6;
                    if (TimeSpan::FromSeconds(1).Ticks != 10000000l || TimeSpan::FromSeconds(0.5f).Ticks != 5000000l) return 7;
                    return 0;
                }
                """;
            var result = CompilerTestHelper.Run(Sources(program), optimization);
            Assert.True(result.ExitCode == 0, $"Fractional TimeSpan exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static TimeSpan Create(byte unit, double value) => unit switch
    {
        0 => TimeSpan.FromMicroseconds(value),
        1 => TimeSpan.FromMilliseconds(value),
        2 => TimeSpan.FromSeconds(value),
        3 => TimeSpan.FromMinutes(value),
        4 => TimeSpan.FromHours(value),
        _ => TimeSpan.FromDays(value)
    };

    private static IEnumerable<double> Inputs(byte unit)
    {
        double scale = new double[] { 10, 10000, 10000000, 600000000, 36000000000, 864000000000 }[unit];
        double[] fixedValues = [0, -0.0, 0.1, -0.1, 0.5, -0.5, 1, -1, 1.5, -1.5, double.Epsilon,
            -double.Epsilon, double.MaxValue, double.MinValue, double.PositiveInfinity, double.NegativeInfinity,
            double.NaN, BitConverter.UInt64BitsToDouble(0x7ff0000000000001), BitConverter.UInt64BitsToDouble(0xfff0000000000001)];
        foreach (double value in fixedValues) yield return value;
        foreach (double ticks in new[] { -12345.6789, -1.9, -1.5, -1.1, -1, -0.9, -0.5, -0.1, 0, 0.1, 0.5, 0.9, 1, 1.1, 1.5, 1.9, 12345.6789 })
        {
            double value = ticks / scale;
            yield return Math.BitDecrement(value);
            yield return value;
            yield return Math.BitIncrement(value);
        }
        double limit = 9223372036854775808d / scale;
        foreach (double sign in new[] { -1d, 1d })
        {
            double lower = sign * limit;
            double upper = lower;
            yield return lower;
            for (int i = 0; i < 8; i++)
            {
                lower = Math.BitDecrement(lower);
                upper = Math.BitIncrement(upper);
                yield return lower;
                yield return upper;
            }
        }
        var random = new Random(42319 + unit);
        for (int i = 0; i < 128; i++)
        {
            yield return (random.NextDouble() * 2.2 - 1.1) * limit;
            ulong bits = (ulong)random.NextInt64() | ((ulong)random.Next(2) << 63);
            yield return BitConverter.UInt64BitsToDouble(bits);
        }
    }
}
