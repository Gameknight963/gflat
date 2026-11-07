namespace gflat.Tests.Std.Queries;

public sealed class NumericSumTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load("core/Queries/enumerable_sum.gf"))
            .Append(new SourceFile("sum-tests.gf", program));

    [Fact]
    public void GuideExampleCompiles()
    {
        StandardLibraryGuideTests.CheckProgram("QUERY_SUM.md", 0);
        StandardLibraryGuideTests.CheckProgram("QUERY_SUM.md", 1);
    }

    [Theory]
    [InlineData("int", 0)]
    [InlineData("int", 2)]
    [InlineData("long", 0)]
    [InlineData("long", 2)]
    [InlineData("float", 0)]
    [InlineData("float", 2)]
    [InlineData("double", 0)]
    [InlineData("double", 2)]
    public void DirectAndSelectorFormsMatchSequentialLinq(string scalar, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_sum_" + Guid.NewGuid().ToString("N"));
        try
        {
            var rows = Cases(scalar).ToArray();
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (ulong[] values in rows)
            {
                bool rejected = false;
                double expected = 0;
                try
                {
                    expected = scalar switch
                    {
                        "int" => values.Select(x => (long)(int)x).Sum(),
                        "long" => values.Select(x => (long)x).Sum(),
                        "float" => values.Select(x => BitConverter.UInt32BitsToSingle((uint)x)).Sum(),
                        _ => values.Select(BitConverter.UInt64BitsToDouble).Sum()
                    };
                }
                catch (InvalidOperationException) { rejected = true; }
                catch (OverflowException) { rejected = true; }
                writer.Write((ulong)values.Length);
                writer.Write(rejected ? 1ul : 0ul);
                writer.Write(BitConverter.DoubleToUInt64Bits(expected));
                foreach (ulong value in values) writer.Write(value);
            }
            string conversion = scalar switch
            {
                "int" => "(int)bits", "long" => "(long)bits",
                "float" => "((uint)bits).reinterpret<float>()", _ => "bits.reinterpret<double>()"
            };
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard;
                using Standard::Memory;
                using Standard::Queries;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                {{scalar}} Identity({{scalar}} value) => value;
                {{scalar}} IdentityReadonly(readonly({{scalar}}) value) => ({{scalar}})value;
                {{scalar}} IdentityImmutable(immutable({{scalar}}) value) => ({{scalar}})value;
                bool Same(double value, ulong bits) nothrow
                {
                    double expected = bits.reinterpret<double>();
                    if (expected != expected) return value != value;
                    return value.reinterpret<ulong>() == bits;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    {{scalar}}[32] data = default({{scalar}}[32]);
                    for (int row = 0; row < {{rows.Length}}; row++)
                    {
                        ulong[3] header = default(ulong[3]);
                        if (fread(&header[0], 8, 3, file!) != 3) return 2;
                        for (nuint i = 0; i < (nuint)header[0]; i++)
                        {
                            ulong bits = 0ul;
                            if (fread(&bits, 8, 1, file!) != 1) return 3;
                            data[i] = {{conversion}};
                        }
                        Span<{{scalar}}> span = new(&data[0], (nuint)header[0]);
                        for (int form = 0; form < 4; form++)
                        {
                            bool caught = false;
                            double actual = 123.0;
                            try
                            {
                                if (form == 0) actual = (double)span.AsEnumerable().Sum();
                                else if (form == 1) actual = (double)Sum(span.AsEnumerable(), Identity);
                                else if (form == 2) actual = (double)span.AsReadOnly().AsEnumerable().Sum(IdentityReadonly);
                                else actual = (double)span.AsImmutable().AsEnumerable().Sum(IdentityImmutable);
                            }
                            catch (Exception* error) { caught = true; }
                            if (header[1] == 1ul)
                            {
                                if (!caught || actual != 123.0) return 4;
                            }
                            else if (caught || !Same(actual, header[2])) return 5;
                        }
                    }
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Sum {scalar} exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshOwnedCurrentsAndIteratorFailuresReleaseEveryOwner(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Queries;

            int live = 0;
            int starts = 0;
            int ends = 0;
            int moves = 0;
            int reads = 0;
            int calls = 0;
            int mode = 0;
            int length = 3;
            struct Owner
            {
                public long Value;
                public Owner(long value) { Value = value; live++; }
                ~Owner() { live--; }
            }
            struct Source
            {
                public struct Iterator
                {
                    private int position;
                    public Iterator()
                    {
                        position = 0;
                        if (mode == 1) throw new Exception("construction", 71);
                        starts++;
                    }
                    public bool MoveNext()
                    {
                        moves++;
                        if (mode == 2 && position == 1) throw new Exception("advance", 72);
                        if (position == length) return false;
                        position++;
                        return true;
                    }
                    public Owner Current
                    {
                        get
                        {
                            reads++;
                            if (mode == 3 && position == 2) throw new Exception("current", 73);
                            return new Owner((long)position);
                        }
                    }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator() => new Iterator();
            }
            long Project(Owner value)
            {
                calls++;
                if (mode == 4) throw new Exception("selector", 74);
                return value.Value;
            }
            void Reset() { moves = 0; reads = 0; calls = 0; }
            int main()
            {
                var query = new Enumerable<Owner, Source, Source::Iterator>(new Source());
                if (query.Sum(Project) != 6l || moves != 4 || reads != 3 || calls != 3 || starts != ends || live != 0) return 1;
                length = 0; Reset();
                bool caught = false;
                if (query.Sum(Project) != 0l || moves != 1 || reads != 0 || calls != 0 || starts != ends || live != 0) return 2;
                length = 3;
                for (int failure = 1; failure <= 4; failure++)
                {
                    mode = failure; Reset(); caught = false;
                    try { query.Sum(Project); }
                    catch (Exception* error)
                    {
                        if (error.GetCode() != 70 + failure) return 3;
                        caught = true;
                    }
                    if (!caught || starts != ends || live != 0) return 4;
                    if (failure == 4 && (moves != 1 || reads != 1 || calls != 1)) return 5;
                    if (failure == 3 && (moves != 2 || reads != 2 || calls != 1)) return 6;
                    if (failure == 2 && (moves != 2 || reads != 1 || calls != 1)) return 7;
                }
                mode = 0;
                if (query.Sum(Project) != 6l || starts != ends || live != 0) return 8;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Sum effects exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private static IEnumerable<ulong[]> Cases(string scalar)
    {
        yield return [];
        yield return [0];
        if (scalar is "int" or "long")
        {
            ulong maximum = scalar == "int" ? (ulong)int.MaxValue : (ulong)long.MaxValue;
            ulong minimum = scalar == "int" ? unchecked((ulong)int.MinValue) : unchecked((ulong)long.MinValue);
            yield return [maximum, maximum];
            yield return [minimum, minimum];
            yield return [maximum, 1, minimum];
            yield return [minimum, ulong.MaxValue, maximum];
            yield return [maximum, minimum];
        }
        else
        {
            ulong sign = scalar == "float" ? 0x80000000ul : 0x8000000000000000ul;
            ulong infinity = scalar == "float" ? 0x7f800000ul : 0x7ff0000000000000ul;
            yield return [sign];
            yield return [sign, sign];
            yield return [infinity];
            yield return [infinity, infinity | sign];
            yield return [infinity + 1, 0];
            yield return [infinity - 1, infinity - 1];
            yield return [1, 2, 3, sign | 1];
        }
        var random = new Random(928173);
        for (int row = 0; row < 160; row++)
        {
            var values = new ulong[row % 33];
            for (int i = 0; i < values.Length; i++)
            {
                double value = (random.NextDouble() - 0.5) * 100000;
                values[i] = scalar switch
                {
                    "int" => unchecked((ulong)random.Next(int.MinValue, int.MaxValue)),
                    "long" => unchecked((ulong)random.NextInt64(long.MinValue, long.MaxValue)),
                    "float" => BitConverter.SingleToUInt32Bits((float)value),
                    _ => BitConverter.DoubleToUInt64Bits(value)
                };
            }
            yield return values;
        }
    }
}

