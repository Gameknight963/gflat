using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class StoredKeyLookupTests
{
    private static readonly (string Declaration, string Method, bool Map, bool Hash, string StoredKey)[] Forms = [
        ("HashMap<Key,int> values = new();", "FindKey", true, true, "iterator.Current.Key"),
        ("HashSet<Key> values = new();", "Find", false, true, "iterator.Current"),
        ("SortedMap<Key,int> values = new(Compare);", "FindKey", true, false, "iterator.Current.Key"),
        ("SortedSet<Key> values = new(Compare);", "Find", false, false, "iterator.Current")
    ];
    private const string Allocator = """
        using std; using std::Text; using std::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocations = 0; int releases = 0; bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++; if (deny) return null;
                void*? result = malloc(bytes); if (result != null) allocations++; return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;
    private const string PlainKey = """
        struct Key : IHashable, IEquatable<Key> {
            public int Id; public int Payload;
            public Key(int id, int payload) { Id = id; Payload = payload; }
            public immutable nuint GetHashCode() => 1;
            public immutable bool Equals(immutable(Key)* other) => Id == other.Id;
        }
        int Compare(immutable(Key)* left, immutable(Key)* right) {
            if (left.Id < right.Id) return -1; if (left.Id > right.Id) return 1; return 0;
        }
        """;
    private const string OwnerKey = """
        int made = 0; int dead = 0; int hashCalls = 0; int equalityCalls = 0; int comparisonCalls = 0;
        int hashBudget = -1; int equalityBudget = -1; int comparisonBudget = -1;
        struct Key : IHashable, IEquatable<Key> {
            public int Id; public int Payload; public String Token;
            public Key(int id, int payload) { Id = id; Payload = payload; Token = s"owner"; made++; }
            ~Key() { dead++; }
            public immutable nuint GetHashCode() {
                hashCalls++; if (hashBudget == 0) throw new Exception("hash", 71);
                if (hashBudget > 0) hashBudget--; return 1;
            }
            public immutable bool Equals(immutable(Key)* other) {
                equalityCalls++; if (equalityBudget == 0) throw new Exception("equal", 72);
                if (equalityBudget > 0) equalityBudget--; return Id == other.Id;
            }
        }
        int Compare(immutable(Key)* left, immutable(Key)* right) {
            comparisonCalls++; if (comparisonBudget == 0) throw new Exception("compare", 73);
            if (comparisonBudget > 0) comparisonBudget--;
            if (left.Id < right.Id) return -1; if (left.Id > right.Id) return 1; return 0;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StoredPayloadsMatchIndependentHostCanonicalSetLookupsWithoutAllocation(int optimization)
    {
        var comparer = new HostComparer();
        var hashed = new HashSet<HostKey>(comparer); var sorted = new SortedSet<HostKey>(comparer);
        var random = new Random(381715);
        var input = new List<HostKey> { new(2, 20), new(1, 10), new(2, 99), new(3, 30) };
        for (int i = 0; i < 20; i++) input.Add(new(random.Next(0, 11), 100 + i));
        foreach (var key in input) { hashed.Add(key); sorted.Add(key); }
        var expected = new List<int>();
        for (int id = 0; id < 15; id++)
        {
            bool first = hashed.TryGetValue(new(id, -99), out var canonical);
            bool second = sorted.TryGetValue(new(id, -99), out var ordered);
            Assert.Equal(first, second); if (first) Assert.Equal(canonical.Payload, ordered.Payload);
            expected.Add(first ? canonical.Payload : -1);
        }
        var code = new StringBuilder(Allocator + PlainKey + "int main() {\n");
        foreach (var form in Forms)
        {
            code.AppendLine("{" + form.Declaration);
            code.AppendLine($"{{ Key emptyQuery = new(99,-99); int before = attempts; deny = true; if (values.{form.Method}(&emptyQuery) != null || attempts != before) return 8; deny = false; }}");
            foreach (var key in input) code.AppendLine(Insert(form, key.Id.ToString(), key.Payload.ToString()) + ";");
            code.AppendLine($$"""
                int[15] expected = [{{string.Join(',', expected)}}]; nuint capacity = values.Capacity;
                int before = attempts; deny = true;
                for (int id = 0; id < 15; id++) {
                    Key query = new(id, -99); immutable(Key)*? result = values.{{form.Method}}(&query);
                    if (expected[(nuint)id] < 0) { if (result != null) return 1; }
                    else {
                        if (result == null || result!.Id != id || result!.Payload != expected[(nuint)id] || result == &query) return 2;
                        if (values.{{form.Method}}(result!) != result) return 3;
                        immutable(Key)*? enumerated = null; var iterator = values.GetEnumerator();
                        while (iterator.MoveNext()) if ({{form.StoredKey}}.Id == id) { enumerated = {{form.StoredKey}}; break; }
                        if (result != enumerated) return 4;
                    }
                    if (query.Id != id || query.Payload != -99) return 5;
                }
                deny = false;
                if (attempts != before || values.Count != {{hashed.Count}} || values.Capacity != capacity) return 6;
                }
                """);
        }
        code.AppendLine("if (allocations != releases) return 7; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableStoredOwnersOutliveQueriesAndDuplicatesKeepOriginalIdentity(int optimization)
    {
        var code = new StringBuilder(Allocator + OwnerKey + "int main() {\n");
        foreach (var form in Forms)
        {
            code.AppendLine("{" + form.Declaration);
            foreach (int id in new[] { 2, 1, 3 }) code.AppendLine(Insert(form, id.ToString(), (id * 10).ToString()) + ";");
            code.AppendLine(Insert(form, "1", "77") + ";");
            code.AppendLine($$"""
                immutable(Key)*? held = null;
                { Key query = new(1, 99); int before = attempts; deny = true;
                  held = values.{{form.Method}}(&query);
                  if (held == null || held!.Payload != 10 || held == &query) return 1;
                  immutable(Key)*? enumerated = null; var iterator = values.GetEnumerator();
                  while (iterator.MoveNext()) if ({{form.StoredKey}}.Id == 1) { enumerated = {{form.StoredKey}}; break; }
                  if (held != enumerated || values.{{form.Method}}(held!) != held || attempts != before) return 2;
                  deny = false;
                }
                if (held == null || held!.Id != 1 || held!.Payload != 10 || held!.Token.Length != 5) return 3;
                """);
            if (form.Map)
                code.AppendLine($"values.Set(new Key(1,88),222); Key query = new(1,0); if (values.{form.Method}(&query) != held || held!.Payload != 10 || values[&query] != 222) return 4;");
            code.AppendLine("}");
        }
        code.AppendLine("if (made != dead || allocations != releases) return 5; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryObservedCallbackFailurePreservesCanonicalOwnersAndContainerState(int optimization)
    {
        var code = new StringBuilder(Allocator + OwnerKey + "int main() {\n");
        foreach (var form in Forms)
        {
            code.AppendLine("{" + form.Declaration);
            for (int id = 0; id < 8; id++) code.AppendLine(Insert(form, id.ToString(), (id * 10).ToString()) + ";");
            code.AppendLine($$"""
                Key query = new(3, 99); nuint capacity = values.Capacity; int deadBefore = dead;
                hashCalls = 0; equalityCalls = 0; comparisonCalls = 0;
                immutable(Key)*? held = values.{{form.Method}}(&query);
                if (held == null || held!.Payload != 30) return 1;
                int stages = {{(form.Hash ? "equalityCalls" : "comparisonCalls")}};
                for (int failure = {{(form.Hash ? "-1" : "0")}}; failure < stages; failure++) {
                    int expectedCode = {{(form.Hash ? "72" : "73")}};
                    readonly(char)* expectedMessage = "{{(form.Hash ? "equal" : "compare")}}";
                """);
            if (form.Hash) code.AppendLine("if (failure < 0) { hashBudget = 0; expectedCode = 71; expectedMessage = \"hash\"; } else equalityBudget = failure;");
            else code.AppendLine("comparisonBudget = failure;");
            code.AppendLine($$"""
                    bool caught = false; immutable(Key)*? result = &query;
                    try { result = values.{{form.Method}}(&query); }
                    catch (ref Exception error) { caught = error.GetCode() == expectedCode && CString::Equals(error.GetMessage(), expectedMessage); }
                    hashBudget = -1; equalityBudget = -1; comparisonBudget = -1;
                    if (!caught || result != &query || dead != deadBefore || values.Count != 8 || values.Capacity != capacity) return 2;
                    if (values.{{form.Method}}(&query) != held || held!.Payload != 30 || query.Payload != 99) return 3;
                    int seen = 0; var iterator = values.GetEnumerator();
                    while (iterator.MoveNext()) {
                        immutable(Key)* key = {{form.StoredKey}};
                        if (key.Id < 0 || key.Id >= 8 || key.Payload != key.Id * 10 || key.Token.Length != 5) return 4;
                        seen++;
                    }
                    if (seen != 8) return 5;
                }
                }
                """);
        }
        code.AppendLine("if (made != dead || allocations != releases) return 6; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Fact]
    public void ReturnedKeysProtectPointerTargetsFromMutation()
    {
        foreach (var form in Forms)
        {
            string insert = form.Map ? "values.TryAdd(new Key(1,&payload),1);" : "values.Add(new Key(1,&payload));";
            string source = $$"""
                using std; using std::Collections;
                struct Key : IHashable, IEquatable<Key> {
                    public int Id; public int* Payload;
                    public Key(int id, int* payload) { Id = id; Payload = payload; }
                    public immutable nuint GetHashCode() => 1;
                    public immutable bool Equals(immutable(Key)* other) => Id == other.Id;
                }
                int Compare(immutable(Key)* left, immutable(Key)* right) => left.Id - right.Id;
                int main() {
                    int payload = 5; {{form.Declaration}} {{insert}}
                    Key query = new(1,&payload); immutable(Key)*? stored = values.{{form.Method}}(&query);
                    *(stored!.Payload) = 7; return 0;
                }
                """;
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(source), options: new() { NoStdLib = true }));
            Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Insert((string Declaration, string Method, bool Map, bool Hash, string StoredKey) form, string id, string payload)
        => form.Map ? $"values.TryAdd(new Key({id},{payload}),{id})" : $"values.Add(new Key({id},{payload}))";
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("stored-key-lookup.gf", program));
    private static void Run(string program, int optimization)
    {
        string ir = Compiler.Emit(Sources(program), options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
    private readonly record struct HostKey(int Id, int Payload);
    private sealed class HostComparer : IEqualityComparer<HostKey>, IComparer<HostKey>
    {
        public bool Equals(HostKey left, HostKey right) => left.Id == right.Id;
        public int GetHashCode(HostKey value) => value.Id;
        public int Compare(HostKey left, HostKey right) => left.Id.CompareTo(right.Id);
    }
}
