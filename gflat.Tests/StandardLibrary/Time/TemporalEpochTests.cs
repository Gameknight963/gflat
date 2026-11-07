namespace gflat.Tests.Std.Time;

public sealed class TemporalEpochTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_EPOCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EpochValuesAndTickOffsetConstructionMatchDotNetAcrossBoundaries(int optimization)
    {
        long maximum = DateTime.MaxValue.Ticks;
        long[] ticks = [long.MinValue, -1, 0, 1, TimeSpan.TicksPerMinute - 1,
            TimeSpan.TicksPerMinute, DateTime.UnixEpoch.Ticks - 1, DateTime.UnixEpoch.Ticks,
            maximum - TimeSpan.TicksPerHour, maximum - 1, maximum, maximum + 1, long.MaxValue];
        long[] offsets = [long.MinValue, -14 * TimeSpan.TicksPerHour - 1, -14 * TimeSpan.TicksPerHour,
            -TimeSpan.TicksPerMinute, -1, 0, 1, TimeSpan.TicksPerMinute,
            14 * TimeSpan.TicksPerHour, 14 * TimeSpan.TicksPerHour + 1, long.MaxValue];
        var cases = (from tick in ticks from offset in offsets select (Ticks: tick, Offset: offset)).ToList();
        var random = new Random(519837);
        for (int i = 0; i < 1024; i++)
        {
            long clock = random.NextInt64(maximum + 1);
            long offset = random.Next(-841, 842) * TimeSpan.TicksPerMinute;
            if (i % 9 == 0) offset++;
            cases.Add((clock, offset));
        }
        string path = Path.Combine(Path.GetTempPath(), "gflat_temporal_epoch_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var row in cases)
            {
                bool valid = false;
                long utc = 0, seconds = 0;
                try
                {
                    var value = new DateTimeOffset(row.Ticks, TimeSpan.FromTicks(row.Offset));
                    valid = true;
                    utc = value.UtcTicks;
                    seconds = value.ToUnixTimeSeconds();
                }
                catch (ArgumentException) { }
                writer.Write(row.Ticks); writer.Write(row.Offset); writer.Write(valid ? 1 : 0);
                writer.Write(utc); writer.Write(seconds);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Time.Append(new SourceFile("temporal-epoch.gf", $$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int requests = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes) { requests++; return null; }
                    public replace void Free(void* storage) nothrow { }
                }
                bool Epochs() nothrow
                {
                    DateTime date = DateTime::UnixEpoch;
                    DateTimeOffset offset = DateTimeOffset::UnixEpoch;
                    if (date.Ticks != {{DateTime.UnixEpoch.Ticks}}l || date.Year != 1970 || date.Month != 1 || date.Day != 1) return false;
                    if (offset.Ticks != date.Ticks || offset.UtcTicks != date.Ticks || offset.Offset.Ticks != 0l
                        || offset.ToUnixTimeSeconds() != 0l || offset.ToUnixTimeMilliseconds() != 0l) return false;
                    DateTimeOffset zero = DateTimeOffset::FromUnixTimeSeconds(0l);
                    return offset.EqualsExact(&zero);
                }
                int main()
                {
                    requests = 0;
                    if (!Epochs() || requests != 0) return 1;
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 2;
                    defer { fclose(file!); }
                    for (int row = 0; row < {{cases.Count}}; row++)
                    {
                        long ticks = 0l;
                        long offset = 0l;
                        int expected = 0;
                        long utc = 0l;
                        long seconds = 0l;
                        if (fread(&ticks, 1, 8, file!) != 8 || fread(&offset, 1, 8, file!) != 8
                            || fread(&expected, 1, 4, file!) != 4 || fread(&utc, 1, 8, file!) != 8
                            || fread(&seconds, 1, 8, file!) != 8) return 3;
                        DateTimeOffset value = DateTimeOffset::UnixEpoch;
                        int before = requests;
                        bool caught = false;
                        try { value = new DateTimeOffset(ticks, TimeSpan::FromTicks(offset)); }
                        catch (Exception* error) { caught = true; }
                        if (caught == (expected != 0)) return 4;
                        if (caught)
                        {
                            DateTimeOffset epoch = DateTimeOffset::UnixEpoch;
                            if (!value.EqualsExact(&epoch)) return 5;
                            continue;
                        }
                        DateTimeOffset converted = value.ToUniversalTime();
                        if (value.Ticks != ticks || value.Offset.Ticks != offset || value.UtcTicks != utc
                            || value.ToUnixTimeSeconds() != seconds) return 6;
                        if (converted.Offset.Ticks != 0l || converted.Ticks != utc || converted.UtcTicks != utc
                            || !converted.Equals(&value) || converted.EqualsExact(&value) != (offset == 0l)) return 7;
                        DateTimeOffset restored = converted.ToOffset(value.Offset);
                        if (!restored.EqualsExact(&value) || value.Ticks != ticks || value.Offset.Ticks != offset) return 8;
                        if (requests != before) return 9;
                    }
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
