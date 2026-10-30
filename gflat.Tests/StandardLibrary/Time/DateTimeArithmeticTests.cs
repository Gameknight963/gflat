using System.Text;

namespace gflat.Tests.Std.Time;

public sealed class DateTimeArithmeticTests
{
    private const long Maximum = 3155378975999999999L;
    private const string Support = """
        using Standard; using Standard::Text; using Standard::Time;
        extern void*? malloc(nuint count); extern void free(void* storage);
        int attempts = 0; int allocated = 0; int released = 0; bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++; if (deny) return null;
                void*? result = malloc(count); if (result != null) allocated++; return result;
            }
            public replace void Free(void* storage) nothrow { released++; free(storage); }
        }
        int Digit(char value) nothrow { if (value <= '9') return value - '0'; return value - 'A' + 10; }
        long ReadLong(readonly(char)* data, ref nuint at) nothrow {
            ulong value = 0ul;
            for (int shift = 0; shift < 64; shift += 8) {
                int next = (Digit(data[at]) << 4) | Digit(data[at + 1]); at += 2;
                value = value | ((ulong)next << shift);
            }
            return (long)value;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedDurationSubtractionMatchesDotNetWithoutMutationOrLibraryAllocation(int optimization)
    {
        long[] clocks = [0, 1, 9999999, 10000000, 864000000000L - 1, 864000000000L,
            new System.DateTime(2000, 2, 29, 23, 59, 59).Ticks + 1234567,
            new System.DateTime(2024, 1, 31, 12, 34, 56).Ticks, Maximum - 1, Maximum];
        var rows = new List<(long Clock, long Delta)>();
        foreach (long clock in clocks)
        {
            // Exact allowed bounds reach the first and last representable tick.
            rows.Add((clock, clock)); rows.Add((clock, clock - Maximum)); rows.Add((clock, 0));
            if (clock != 0) rows.Add((clock, 1));
            if (clock != Maximum) rows.Add((clock, -1));
        }
        var random = new Random(604217);
        for (int i = 0; i < 32; i++)
        {
            long clock = random.NextInt64(Maximum + 1), target = random.NextInt64(Maximum + 1);
            rows.Add((clock, clock - target));
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            foreach (var row in rows)
            {
                long expected = new System.DateTime(row.Clock, DateTimeKind.Unspecified)
                    .Subtract(System.TimeSpan.FromTicks(row.Delta)).Ticks;
                writer.Write(row.Clock); writer.Write(row.Delta); writer.Write(expected);
            }
        string hex = Convert.ToHexString(bytes.ToArray());
        Run(Support + $$"""
            int main() {
                readonly(char)* packet = "{{hex}}"; nuint at = 0;
                int before = attempts; deny = true;
                for (int row = 0; row < {{rows.Count}}; row++) {
                    long ticks = ReadLong(packet, ref at); long delta = ReadLong(packet, ref at);
                    long expected = ReadLong(packet, ref at);
                    DateTime value = DateTime::FromTicks(ticks); DateTime saved = value;
                    immutable(DateTime)* view = &value; TimeSpan duration = TimeSpan::FromTicks(delta);
                    DateTime result = view.Subtract(duration);
                    if (result.Ticks != expected || result.Difference(value).Ticks != -delta) return 1;
                    DateTime recovered = result.Add(duration); if (recovered.Ticks != ticks) return 2;
                    if (!value.Equals(&saved) || value.Date.DayNumber != saved.Date.DayNumber || value.TimeOfDay.Ticks != saved.TimeOfDay.Ticks || duration.Ticks != delta) return 3;
                }
                deny = false;
                if (at != {{hex.Length}} || attempts != before || allocated != released) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OutOfRangeAndExtremeDurationsThrowBeforeChangingReceiverOrAssignmentTarget(int optimization)
    {
        var rows = new List<(long Clock, long Delta)>();
        foreach (long clock in new[] { 0L, 1L, Maximum / 2, Maximum - 1, Maximum })
        {
            rows.Add((clock, clock + 1)); rows.Add((clock, clock - Maximum - 1));
            rows.Add((clock, long.MinValue)); rows.Add((clock, long.MaxValue));
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            foreach (var row in rows)
            {
                var value = new System.DateTime(row.Clock, DateTimeKind.Unspecified);
                Assert.Throws<ArgumentOutOfRangeException>(() => value.Subtract(System.TimeSpan.FromTicks(row.Delta)));
                writer.Write(row.Clock); writer.Write(row.Delta);
            }
        string hex = Convert.ToHexString(bytes.ToArray());
        Run(Support + $$"""
            int main() {
                readonly(char)* packet = "{{hex}}"; nuint at = 0;
                for (int row = 0; row < {{rows.Count}}; row++) {
                    long ticks = ReadLong(packet, ref at); long delta = ReadLong(packet, ref at);
                    DateTime value = DateTime::FromTicks(ticks); DateTime saved = value;
                    DateTime target = DateTime::FromTicks(1234567l); DateTime targetSaved = target;
                    immutable(DateTime)* view = &value; TimeSpan duration = TimeSpan::FromTicks(delta);
                    bool caught = false;
                    try { target = view.Subtract(duration); }
                    catch (ref Exception error) {
                        caught = CString::Equals(error.GetMessage(), "DateTime subtraction is outside years 1 through 9999");
                    }
                    if (!caught || !target.Equals(&targetSaved) || !value.Equals(&saved) || duration.Ticks != delta) return 1;
                    if (value.Date.DayNumber != saved.Date.DayNumber || value.TimeOfDay.Ticks != saved.TimeOfDay.Ticks) return 2;
                    if (allocated != released) return 3;
                }
                if (at != {{hex.Length}}) return 4;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("date-time-arithmetic.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        var result = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        Assert.True(result.ExitCode == 0, $"DateTime arithmetic exit {result.ExitCode}: {result.StandardError}");
    }
}
