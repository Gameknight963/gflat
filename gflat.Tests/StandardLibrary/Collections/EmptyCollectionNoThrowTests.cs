using System.Text.RegularExpressions;

namespace gflat.Tests.Std.Collections;

public sealed class EmptyCollectionNoThrowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyOwningHeadersDoNotAllocateConstructElementsOrInvokeCallbacks(int optimization)
    {
        const string program = """
            using Standard;
            using Standard::Collections;
            int allocations = 0;
            int frees = 0;
            int constructed = 0;
            int copied = 0;
            int destroyed = 0;
            int hashed = 0;
            int compared = 0;
            int equated = 0;
            namespace Memory
            {
                public void*? Allocate(nuint count) { allocations++; return null; }
                public void Free(void* storage) nothrow { frees++; }
            }
            struct Owner : IHashable, IEquatable<Owner>
            {
                public Owner() { constructed++; }
                public copy Owner(immutable(Owner)* source) { copied++; }
                ~Owner() { destroyed++; }
                public immutable nuint GetHashCode() { hashed++; return 0; }
                public immutable bool Equals(immutable(Owner)* other) { equated++; return true; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right) { compared++; return 0; }

            int EmptyHashMap() nothrow
            {
                HashMap<Owner, Owner> value = new();
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 1;
                return 0;
            }
            int EmptyHashSet() nothrow
            {
                HashSet<Owner> value = new();
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 2;
                return 0;
            }
            int EmptyQueue() nothrow
            {
                Queue<Owner> value = new();
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 3;
                return 0;
            }
            int EmptyDeque() nothrow
            {
                Deque<Owner> value = new();
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 4;
                return 0;
            }
            int EmptyStack() nothrow
            {
                Stack<Owner> value = new();
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 5;
                return 0;
            }
            int EmptyLinkedList() nothrow
            {
                LinkedList<Owner> value = new();
                if (value.Count != 0 || !value.IsEmpty) return 6;
                return 0;
            }
            int EmptyBitSet() nothrow
            {
                BitSet value = new();
                if (value.Length != 0 || value.CountSetBits() != 0 || value.HasAnySet() || !value.HasAllSet()) return 7;
                return 0;
            }
            int EmptySortedMap() nothrow
            {
                SortedMap<Owner, Owner> value = new(Compare);
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 8;
                return 0;
            }
            int EmptySortedSet() nothrow
            {
                SortedSet<Owner> value = new(Compare);
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 9;
                return 0;
            }
            int EmptyPriorityQueue() nothrow
            {
                PriorityQueue<Owner> value = new(Compare);
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 10;
                return 0;
            }
            int EmptyVector() nothrow
            {
                Vector<Owner> value = new();
                if (value.Count != 0 || value.Capacity != 0 || !value.IsEmpty) return 11;
                return 0;
            }
            int EmptyDisjointSet() nothrow
            {
                DisjointSet value = new();
                if (value.ElementCount != 0 || value.SetCount != 0) return 12;
                return 0;
            }
            int main() nothrow
            {
                int result = EmptyHashMap(); if (result != 0) return result;
                result = EmptyHashSet(); if (result != 0) return result;
                result = EmptyQueue(); if (result != 0) return result;
                result = EmptyDeque(); if (result != 0) return result;
                result = EmptyStack(); if (result != 0) return result;
                result = EmptyLinkedList(); if (result != 0) return result;
                result = EmptyBitSet(); if (result != 0) return result;
                result = EmptySortedMap(); if (result != 0) return result;
                result = EmptySortedSet(); if (result != 0) return result;
                result = EmptyPriorityQueue(); if (result != 0) return result;
                result = EmptyVector(); if (result != 0) return result;
                result = EmptyDisjointSet(); if (result != 0) return result;
                if (allocations != 0 || frees != 0 || constructed != 0 || copied != 0
                    || destroyed != 0 || hashed != 0 || equated != 0 || compared != 0) return 13;
                return 0;
            }
            """;
        string[] names = ["Exception.gf", "BadAllocException.gf", "IHashable.gf", "IEquatable.gf",
            "Hashing/primitive_hashing.gf", "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Memory/Span.gf",
            "Collections/Vector.gf", "Collections/HashMap.gf", "Collections/HashSet.gf", "Collections/Queue.gf",
            "Collections/Deque.gf", "Collections/Stack.gf", "Collections/LinkedList.gf", "Collections/BitSet.gf",
            "Collections/SortedMap.gf", "Collections/SortedSet.gf", "Collections/PriorityQueue.gf", "Collections/DisjointSet.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("empty-collections.gf", program));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Match[] definitions = Regex.Matches(ir,
            @"(?ms)^(?<definition>define [^@\r\n]*@(?<name>[^\s(]+)\([^\r\n]*\{)\r?\n(?<body>.*?)^\}")
            .Cast<Match>().ToArray();
        foreach (string type in new[] { "HashMap", "HashSet", "Queue", "Deque", "Stack", "LinkedList", "BitSet",
            "SortedMap", "SortedSet", "PriorityQueue", "Vector", "DisjointSet" })
        {
            Match probe = Assert.Single(definitions, entry => entry.Groups["name"].Value == "gflat$Empty" + type);
            Match selected = Assert.Single(Regex.Matches(probe.Groups["body"].Value,
                @"\b(?:call|invoke) void @(?<symbol>[^\s(]+\$ctor\$[^\s(]+)\(").Cast<Match>());
            string symbol = selected.Groups["symbol"].Value;
            Assert.Contains(type, symbol);
            Match constructor = Assert.Single(definitions, entry => entry.Groups["name"].Value == symbol);
            Assert.Contains(" nounwind", constructor.Groups["definition"].Value);
            foreach (string forbidden in new[] { "invoke ", "landingpad", "__gflat_eh_", "gflat$Memory$Allocate" })
                Assert.False(constructor.Groups["body"].Value.Contains(forbidden, StringComparison.Ordinal),
                    $"Unexpected '{forbidden}' in empty constructor {symbol}:\n{constructor.Groups["body"].Value}");
        }
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
