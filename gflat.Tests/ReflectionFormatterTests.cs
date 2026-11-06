namespace gflat.Tests;

public sealed class ReflectionFormatterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShippedExamplePrintsPublicFieldNamesAndValues(int optimization)
    {
        var sources = StandardSources().Concat(new[] { "field_formatting.gf", "main.gf" }
            .Select(name => ReadExample(name)));
        ExecutionResult result = Run(sources, optimization);
        Assert.Equal("Count = 42" + Environment.NewLine + "Name = Ada" + Environment.NewLine,
            result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldsBorrowOwningStringsAndPreserveEmbeddedZeros(int optimization)
        => RunProgram("""
            using Standard;
            using Standard::Text;

            extern "C" void*? malloc(nuint size);
            extern "C" void free(void* storage);
            bool denyAllocation = false;
            int allocationAttempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint size)
                {
                    allocationAttempts++;
                    if (denyAllocation) return null;
                    return malloc(size);
                }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Unformattable { public int Value; }
            struct Record
            {
                public int Count;
                public String Name = new();
                private Unformattable hidden;
            }
            int main()
            {
                Record record = new();
                record.Count = -17;
                record.Name = String::From("A\0B", 3);
                readonly(char)* originalData = record.Name.Data;
                nuint originalCapacity = record.Name.Capacity;
                char[64] storage;
                BufferWriter output = new(storage, 64);
                int before = allocationAttempts;
                denyAllocation = true;
                WriteFields(ref record, &output);
                denyAllocation = false;
                if (allocationAttempts != before || record.Name.Data != originalData ||
                    record.Name.Capacity != originalCapacity || record.Name.Length != 3) return 1;
                if (record.Count != -17 || record.Name[0] != 'A' || record.Name[1] != '\0' ||
                    record.Name[2] != 'B') return 2;
                readonly(char)* expected = "Count = -17\nName = A\0B\n";
                if (output.Length != 23 || output.Data[23] != '\0') return 3;
                for (nuint i = 0; i < 23; i++) if (output.Data[i] != expected[i]) return 4;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingFieldLeavesPrefixAndPreservesTheRecordUntilItsScopeEnds(int optimization)
        => RunProgram("""
            using Standard;
            using Standard::Text;

            int destroyed = 0;
            struct FailingValue
            {
                public immutable void Format(ITextWriter* output)
                {
                    output.Write("part", 4);
                    throw new Exception("formatter failed");
                }
                ~FailingValue() { destroyed++; }
            }
            struct Record
            {
                public int First;
                public FailingValue Second;
                public int Last;
            }
            int main()
            {
                char[128] storage;
                BufferWriter output = new(storage, 128);
                {
                    Record record = new();
                    record.First = 7;
                    record.Last = 99;
                    bool caught = false;
                    try { WriteFields(ref record, &output); }
                    catch (ref Exception error) { caught = true; }
                    if (!caught || destroyed != 0 || record.First != 7 || record.Last != 99) return 1;
                    readonly(char)* expected = "First = 7\nSecond = part";
                    nuint length = CString::Length(expected);
                    if (output.Length != length) return 2;
                    for (nuint i = 0; i < length; i++) if (output.Data[i] != expected[i]) return 3;
                }
                if (destroyed != 1) return 4;
                return 0;
            }
            """, optimization);

    private static void RunProgram(string source, int optimization)
        => Run(StandardSources().Append(ReadExample("field_formatting.gf"))
            .Append(new SourceFile("reflection-formatter-test.gf", source)), optimization);

    private static SourceFile ReadExample(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Examples", "reflection", name);
        return new SourceFile(path, File.ReadAllText(path));
    }

    private static IEnumerable<SourceFile> StandardSources()
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)));

    private static ExecutionResult Run(IEnumerable<SourceFile> sources, int optimization)
    {
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        return CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
