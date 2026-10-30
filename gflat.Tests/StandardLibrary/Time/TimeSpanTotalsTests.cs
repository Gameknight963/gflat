using System.Globalization;
using System.Text;

namespace gflat.Tests.Std.Time;

public sealed class TimeSpanTotalsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ApproximateTotalsMatchIndependentRationalBoundsAcrossSignedTicks(int optimization)
    {
        (string Name, long Scale)[] units =
        [
            ("TotalMilliseconds", 10000),
            ("TotalSeconds", 10000000),
            ("TotalMinutes", 600000000),
            ("TotalHours", 36000000000),
            ("TotalDays", 864000000000)
        ];
        var ticks = new SortedSet<long>
        {
            long.MinValue, long.MinValue + 1, long.MaxValue - 1, long.MaxValue, 0, 1, -1
        };
        foreach (var unit in units)
        foreach (long factor in new long[] { -25, -2, -1, 0, 1, 2, 25 })
        foreach (long delta in new long[] { -1, 0, 1 })
            ticks.Add(unit.Scale * factor + delta);

        var random = new Random(301481);
        for (int i = 0; i < 24; i++)
            ticks.Add(random.NextInt64(long.MinValue, long.MaxValue));

        var body = new StringBuilder();
        foreach (long tick in ticks)
        {
            string literal = tick == long.MinValue
                ? "(-9223372036854775807l-1l)"
                : tick.ToString(CultureInfo.InvariantCulture) + "l";
            body.AppendLine("{immutable(TimeSpan) value=TimeSpan::FromTicks(" + literal + ");");
            foreach (var unit in units)
            {
                // Compute the rational independently before one double conversion.
                // Product converts long to double before dividing. Two adjacent
                // representable values cover those two rounding steps.
                double expected = (double)((decimal)tick / unit.Scale);
                double low = Math.BitDecrement(Math.BitDecrement(expected));
                double high = Math.BitIncrement(Math.BitIncrement(expected));
                ulong lowBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(low));
                ulong highBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(high));
                body.AppendLine($"if(!Within(value.{unit.Name},{lowBits}ul,{highBits}ul))return 1;");
                if (tick == 0)
                    body.AppendLine($"{{double total=value.{unit.Name};if((total).reinterpret<ulong>()!=0ul)return 2;}}");
                else if (tick < 0)
                    body.AppendLine($"if(value.{unit.Name}>=0d)return 3;");
                else
                    body.AppendLine($"if(value.{unit.Name}<=0d)return 4;");

                // Away from its legacy millisecond clamp, TimeSpan is an extra
                // independent API oracle. Endpoint expectations remain unclamped.
                if (tick > -9000000000000000000L && tick < 9000000000000000000L)
                {
                    var span = new TimeSpan(tick);
                    double host = unit.Name switch
                    {
                        "TotalMilliseconds" => span.TotalMilliseconds,
                        "TotalSeconds" => span.TotalSeconds,
                        "TotalMinutes" => span.TotalMinutes,
                        "TotalHours" => span.TotalHours,
                        _ => span.TotalDays
                    };
                    Assert.InRange(host, low, high);
                }
            }
            body.AppendLine("if(value.Ticks!=" + literal + ")return 5;}");
        }

        string program = """
            using std; using std::Time;
            int attempts=0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { attempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            bool Within(double value,ulong lowBits,ulong highBits) nothrow {
                return value>=(lowBits).reinterpret<double>() && value<=(highBits).reinterpret<double>();
            }
            double Observe(immutable(TimeSpan)* value) nothrow => value.TotalMilliseconds;
            int main() {
            """ + body + """
                TimeSpan oneTick=TimeSpan::FromTicks(-1l);
                if(Observe(&oneTick)>=0d) return 6;
                TimeSpan day=TimeSpan::FromDays(1l);
                if(day.TotalDays!=1d || day.TotalHours!=24d || day.TotalMinutes!=1440d || day.TotalMilliseconds!=86400000d) return 7;
                TimeSpan half=TimeSpan::FromTicks(5000l);
                if(half.TotalMilliseconds!=0.5d || half.Milliseconds!=0) return 8;
                if(attempts!=0) return 9;
                return 0;
            }
            """;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("time-span-totals.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"TimeSpan totals exit {result.ExitCode}: {result.StandardError}");
    }
}
