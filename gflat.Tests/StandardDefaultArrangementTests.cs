namespace gflat.Tests;

public sealed class StandardDefaultArrangementTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveDefaultsAndExplicitNonComparableCallbacksKeepTheirContracts(int optimization)
        => Run("""
            using std; using std::Text; using std::Memory; using std::Collections;
            struct Plain { public int Value; public Plain(int value) { Value = value; } }
            int Compare(immutable(Plain)* first, immutable(Plain)* second) {
                if (first.Value < second.Value) return -1;
                if (first.Value > second.Value) return 1;
                return 0;
            }
            int main() {
                int[5] data = [4, 1, 3, 0, 2];
                Span<int> values = new(data, 5);
                NthElement(values, 2);
                if (values[2] != 2) return 1;
                Sort(values);
                if (!NextPermutation(values) || values[3] != 4 || values[4] != 3) return 2;
                if (!PreviousPermutation(values)) return 3;
                for (nuint i = 0; i < 5; i++) if (values[i] != (int)i) return 4;
                if (PreviousPermutation(values)) return 5;
                for (nuint i = 0; i < 5; i++) if (values[i] != 4 - (int)i) return 6;
                if (NextPermutation(values)) return 7;
                Span<int> empty = new();
                NthElement(empty, 0);
                if (NextPermutation(empty) || PreviousPermutation(empty)) return 8;
                Plain[3] items = [new Plain(3), new Plain(1), new Plain(2)];
                Span<Plain> plain = new(items, 3);
                NthElement(plain, 1, Compare);
                if (plain[1].Value != 2 || !NextPermutation(plain, Compare) || !PreviousPermutation(plain, Compare)) return 9;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeclaredComparableForwardingRelocatesOwnersAndPreservesOriginalFailures(int optimization)
        => Run("""
            using std; using std::Text; using std::Memory; using std::Collections;
            extern void*? malloc(nuint count); extern void free(void* value);
            int attempts = 0; int allocated = 0; int freed = 0;
            int made = 0; int dead = 0; bool deny = false; bool fail = false;
            namespace Memory {
                public replace void*? Allocate(nuint count) {
                    attempts++; if (deny) return null;
                    void*? value = malloc(count); if (value != null) allocated++;
                    return value;
                }
                public replace void Free(void* value) nothrow { freed++; free(value); }
            }
            struct Owner : IComparable<Owner> {
                public int Id; public int* Resource;
                public Owner(int id) { Id = id; Resource = new* int(id); made++; }
                ~Owner() { dead++; delete Resource; }
                public immutable int CompareTo(immutable(Owner)* other) {
                    if (fail) throw new Exception("original comparison", 77);
                    if (Id < other.Id) return -1;
                    if (Id > other.Id) return 1;
                    return 0;
                }
            }
            void Select<T : IComparable<T>>(Span<T> values) { NthElement(values, 2); }
            bool Step<T : IComparable<T>>(Span<T> values, bool forward) {
                if (forward) return NextPermutation(values);
                return PreviousPermutation(values);
            }
            int main() {
                {
                    Vector<Owner> values = new();
                    for (int i = 4; i >= 0; i--) values.Add(new Owner(i));
                    int oldAttempts = attempts; deny = true;
                    Select(values.AsSpan());
                    if (values[2].Id != 2 || dead != 0) return 1;
                    Sort(values.AsSpan());
                    if (!Step(values.AsSpan(), true) || values[3].Id != 4 || values[4].Id != 3) return 2;
                    if (!Step(values.AsSpan(), false)) return 3;
                    deny = false;
                    if (attempts != oldAttempts || made != 5 || dead != 0) return 4;
                    for (nuint i = 0; i < 5; i++) if (values[i].Id != (int)i || *values[i].Resource != (int)i) return 5;
                    for (int mode = 0; mode < 3; mode++) {
                        bool caught = false; fail = true;
                        try {
                            if (mode == 0) Select(values.AsSpan());
                            else Step(values.AsSpan(), mode == 1);
                        }
                        catch (ref Exception error) {
                            if (error.GetCode() != 77 || !CString::Equals(error.GetMessage(), "original comparison")) return 6;
                            caught = true;
                        }
                        fail = false;
                        if (!caught || dead != 0) return 7;
                        for (nuint i = 0; i < 5; i++) if (values[i].Id != (int)i || *values[i].Resource != (int)i) return 8;
                    }
                }
                if (made != dead || allocated != freed) return 9;
                return 0;
            }
            """, optimization);

    [Fact]
    public void MissingComparisonContractsRejectOnlyTheDefaultForms()
    {
        const string declarations = """
            using std; using std::Text; using std::Memory; using std::Collections;
            struct Plain { public int Value; public Plain(int value) { Value = value; } }
            int Compare(immutable(Plain)* first, immutable(Plain)* second) => first.Value - second.Value;
            """;
        string Body(string statement) => declarations + "void Test() { Plain[2] data = [new Plain(2), new Plain(1)]; Span<Plain> values = new(data, 2); " + statement + " }";
        Compiler.Check(Sources(Body("NthElement(values, 0, Compare); NextPermutation(values, Compare); PreviousPermutation(values, Compare);")));
        foreach (string call in new[] { "NthElement(values, 0);", "NextPermutation(values);", "PreviousPermutation(values);" })
        {
            var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(Sources(Body(call))));
            Assert.Contains("No matching overload", error.Message);
            Assert.Contains(call[..call.IndexOf('(')], error.Message);
        }
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("default-arrangement.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
