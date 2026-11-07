namespace gflat.Tests.Std.Time;

public sealed class TimeSpanScalingTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TIME_SPAN_SCALING.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulScalingAndRatiosDoNotAllocate(int optimization)
    {
        const string program = """
            using Standard;
            using Standard::Time;

            extern "C" void free(void* storage);
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            double Ratio(TimeSpan value, TimeSpan divisor) nothrow => value.Divide(divisor);
            int main()
            {
                immutable(TimeSpan) value = TimeSpan::FromTicks(-5l);
                if (value.Duration().Ticks != 5l || value.Multiply(0.5).Ticks != -2l || value.Divide(2.0).Ticks != -2l) return 1;
                if (Multiply(value, -0.5).Ticks != 2l || Divide(value, -2.0).Ticks != 2l) return 2;
                if (Ratio(value, TimeSpan::FromTicks(2l)) != -2.5) return 3;
                double infinity = Ratio(TimeSpan::FromTicks(1l), new TimeSpan());
                double nan = Ratio(new TimeSpan(), new TimeSpan());
                if (infinity.reinterpret<ulong>() != 0x7ff0000000000000ul || nan == nan) return 4;
                if (attempts != 0) return 5;
                return 0;
            }
            """;
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Time/TimeSpan.gf", "core/Time/time_span_scaling.gf"))
            .Append(new SourceFile("timespan-scaling-noallocation.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"TimeSpan allocation check exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DurationScalingAndRatiosMatchDotNetAcrossSpecialValuesAndTickBoundaries(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_timespan_scaling_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (var row in Cases())
                {
                    writer.Write(row.Operation);
                    writer.Write(row.Ticks);
                    writer.Write(row.Operand);
                    writer.Write(row.Expected);
                    writer.Write(row.Valid ? 1 : 0);
                }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string program = $$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                double Ratio(immutable(TimeSpan)* value, TimeSpan divisor) nothrow
                    => value.Divide(divisor);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    byte operation = (byte)0;
                    long ticks = 0l;
                    ulong operand = 0ul;
                    ulong expected = 0ul;
                    int valid = 0;
                    while (true)
                    {
                        nuint read = fread(&operation, 1, 1, file!);
                        if (read == 0) break;
                        if (fread(&ticks, 1, 8, file!) != 8 || fread(&operand, 1, 8, file!) != 8
                            || fread(&expected, 1, 8, file!) != 8 || fread(&valid, 1, 4, file!) != 4) return 2;
                        immutable(TimeSpan) value = TimeSpan::FromTicks(ticks);
                        double scalar = operand.reinterpret<double>();
                        if (operation == (byte)2)
                        {
                            TimeSpan divisor = TimeSpan::FromTicks((long)operand);
                            double actual = Ratio(&value, divisor);
                            double predicted = expected.reinterpret<double>();
                            if (predicted != predicted)
                            {
                                if (actual == actual) return 3;
                            }
                            else if (actual.reinterpret<ulong>() != expected) return 4;
                            double freeRatio = Divide(value, divisor);
                            if (actual == actual && freeRatio.reinterpret<ulong>() != expected) return 5;
                            continue;
                        }
                        for (int form = 0; form < 2; form++)
                        {
                            TimeSpan output = TimeSpan::FromTicks(123l);
                            bool caught = false;
                            try
                            {
                                if (operation == (byte)3) output = value.Duration();
                                else if (operation == (byte)0)
                                {
                                    if (form == 0) output = value.Multiply(scalar);
                                    else output = Multiply(value, scalar);
                                }
                                else
                                {
                                    if (form == 0) output = value.Divide(scalar);
                                    else output = Divide(value, scalar);
                                }
                            }
                            catch (Exception* error) { caught = true; }
                            if (caught != (valid == 0)) return 6;
                            if (!caught && output.Ticks != (long)expected) return 7;
                            if (caught && output.Ticks != 123l) return 8;
                            if (value.Ticks != ticks) return 9;
                        }
                    }
                    if (ferror(file!) != 0) return 10;
                    return 0;
                }
                """;
            var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
                "core/Time/TimeSpan.gf", "core/Time/time_span_scaling.gf"))
                .Append(new SourceFile("timespan-scaling.gf", program));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"TimeSpan scaling exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<(byte Operation, long Ticks, ulong Operand, ulong Expected, bool Valid)> Cases()
    {
        List<long> ticks = [long.MinValue, long.MinValue + 1, long.MaxValue - 1, long.MaxValue,
            -9007199254740993, -9007199254740992, -4503599627370497, -4503599627370496, -4503599627370495, -10000001, -9, -5, -3, -2, -1,
            0, 1, 2, 3, 5, 9, 10000001, 4503599627370495, 4503599627370496, 4503599627370497, 9007199254740992, 9007199254740993];
        List<double> factors = [0, -0.0, -1, 1, 0.5, -0.5, 1.5, -1.5, 2, -2, 3, -3,
            Math.BitDecrement(1d), Math.BitIncrement(1d), double.Epsilon, -double.Epsilon,
            1e300, -1e300, 1e-300, -1e-300, double.MaxValue, double.MinValue,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            BitConverter.UInt64BitsToDouble(0x7ff0000000000001), BitConverter.UInt64BitsToDouble(0xfff0000000000001)];
        var random = new Random(719324);
        for (int i = 0; i < 24; i++) ticks.Add(random.NextInt64(long.MinValue, long.MaxValue));
        for (int i = 0; i < 64; i++)
        {
            factors.Add(random.NextDouble() * 6 - 3);
            factors.Add(BitConverter.UInt64BitsToDouble((ulong)random.NextInt64() | ((ulong)random.Next(2) << 63)));
        }
        foreach (long tick in ticks)
        {
            var value = new TimeSpan(tick);
            bool valid = tick != long.MinValue;
            yield return (3, tick, 0, valid ? (ulong)value.Duration().Ticks : 0, valid);
            foreach (double factor in factors)
            for (byte operation = 0; operation < 2; operation++)
            {
                valid = true;
                long expected = 0;
                try { expected = (operation == 0 ? value.Multiply(factor) : value.Divide(factor)).Ticks; }
                catch (ArgumentException) { valid = false; }
                catch (OverflowException) { valid = false; }
                yield return (operation, tick, BitConverter.DoubleToUInt64Bits(factor), unchecked((ulong)expected), valid);
            }
            foreach (long divisor in ticks)
                yield return (2, tick, unchecked((ulong)divisor),
                    BitConverter.DoubleToUInt64Bits(value.Divide(new TimeSpan(divisor))), true);
        }
    }
}
