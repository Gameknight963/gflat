using System.Numerics;

namespace gflat.Tests.Std.Time;

public sealed class TimeSpanConstructorTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TIME_SPAN_CONSTRUCTION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComponentsMatchDotNetAndWideIntegerOracle(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_duration_ctor_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (arity, parts) in Cases())
            {
                long expected = 0;
                bool valid = true;
                try
                {
                    expected = arity switch
                    {
                        1 => new TimeSpan(parts[0]).Ticks,
                        3 => new TimeSpan((int)parts[1], (int)parts[2], (int)parts[3]).Ticks,
                        4 => new TimeSpan((int)parts[0], (int)parts[1], (int)parts[2], (int)parts[3]).Ticks,
                        5 => new TimeSpan((int)parts[0], (int)parts[1], (int)parts[2], (int)parts[3], (int)parts[4]).Ticks,
                        _ => new TimeSpan((int)parts[0], (int)parts[1], (int)parts[2], (int)parts[3], (int)parts[4], (int)parts[5]).Ticks
                    };
                }
                catch (ArgumentException) { valid = false; }
                if (arity != 1)
                {
                    BigInteger total = 0;
                    long[] scales = [864000000000, 36000000000, 600000000, 10000000, 10000, 10];
                    for (int i = 0; i < 6; i++) total += (BigInteger)parts[i] * scales[i];
                    bool exactValid = total >= long.MinValue && total <= long.MaxValue;
                    // Older runtimes can wrap their microsecond intermediate for huge inputs.
                    // The exact oracle remains authoritative outside that intermediate range.
                    if (total / 10 >= long.MinValue && total / 10 <= long.MaxValue)
                    {
                        Assert.Equal(exactValid, valid);
                        if (exactValid) Assert.Equal((long)total, expected);
                    }
                    valid = exactValid;
                    expected = valid ? (long)total : 0;

                }
                writer.Write((long)arity);
                foreach (long part in parts) writer.Write(part);
                writer.Write(expected); writer.Write(valid ? 1L : 0L);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Time/TimeSpan.gf"))
                .Append(new SourceFile("duration-constructors.gf", $$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                TimeSpan Exact(long value) nothrow => new TimeSpan(value);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long[9] row = default(long[9]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 72, file!);
                        if (received == 0) break;
                        if (received != 72) return 2;
                        TimeSpan output = new(123l);
                        bool caught = false;
                        try
                        {
                            if (row[0] == 1) output = Exact(row[1]);
                            if (row[0] == 3) output = new TimeSpan((int)row[2], (int)row[3], (int)row[4]);
                            if (row[0] == 4) output = new TimeSpan((int)row[1], (int)row[2], (int)row[3], (int)row[4]);
                            if (row[0] == 5) output = new TimeSpan((int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5]);
                            if (row[0] == 6) output = new TimeSpan((int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5], (int)row[6]);
                        }
                        catch (Exception* error) { caught = true; }
                        if (row[8] == 1l) { if (caught || output.Ticks != row[7]) return 3; }
                        else if (!caught || output.Ticks != 123l) return 4;
                    }
                    if (ferror(file!) != 0) return 5;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Duration construction exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<(int, long[])> Cases()
    {
        foreach (long ticks in new[] { long.MinValue, long.MinValue + 1, -1, 0, 1, long.MaxValue })
            yield return (1, [ticks, 0, 0, 0, 0, 0]);
        foreach (int arity in new[] { 3, 4, 5, 6 })
        {
            yield return (arity, [0, 0, 0, 0, 0, 0]);
            foreach (int boundary in new[] { int.MinValue, -1, 1, int.MaxValue })
            for (int field = arity == 3 ? 1 : 0; field < (arity == 3 ? 4 : arity); field++)
            {
                long[] parts = new long[6]; parts[field] = boundary;
                yield return (arity, parts);
            }
        }
        foreach (int days in new[] { -10675200, -10675199, 10675199, 10675200 })
        foreach (int hours in new[] { -24, -3, -2, 0, 2, 3, 24 })
            yield return (6, [days, hours, 48, -3600, 1001, -1001]);
        // Large opposing components must be combined before checking the final range.
        yield return (6, [89478485, -2147483640, 0, 0, 0, 0]);
        yield return (6, [-89478485, 2147483640, 0, 0, 0, 0]);
        var random = new Random(127483);
        foreach (int arity in new[] { 3, 4, 5, 6 })
        for (int sample = 0; sample < 200; sample++)
        {
            long[] parts = new long[6];
            for (int field = arity == 3 ? 1 : 0; field < (arity == 3 ? 4 : arity); field++)
                parts[field] = random.Next(int.MinValue, int.MaxValue);
            if (arity != 3 && sample % 2 == 0) parts[0] = random.Next(-10675199, 10675200);
            yield return (arity, parts);
        }
    }
}
