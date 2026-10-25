using System.Collections;
using System.Text;

namespace gflat.Tests;

public sealed class StandardCollectionTraceTests
{
    private const string OwnedPrelude = """
        using std; using std::Queries; using std::Collections;
        extern int puts(immutable(char*) message);
        int made = 0;
        int destroyed = 0;
        struct Owned {
            public int Value;
            public String Label;
            public Owned(int value) { Value = value; Label = s"item"; made++; }
            ~Owned() { destroyed++; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QueueTraceMatchesDotNetAcrossWraparoundAndGrowth(int optimization)
    {
        var random = new Random(31017);
        var reference = new Queue<int>();
        var trace = new Trace(OwnedPrelude + """
            bool Matches(Queue<Owned>* values, readonly(int)* expected, nuint count) {
                if (values.Count != count) return false;
                Queue<Owned>::Enumerator iterator = values.GetEnumerator();
                for (nuint i = 0; i < count; i++) {
                    if (!iterator.MoveNext() || iterator.Current.Value != expected[i] || iterator.Current.Label.Length != 4) return false;
                }
                return !iterator.MoveNext();
            }
            int main() { { Queue<Owned> values = new(); values.Reserve(3);
            """);
        for (int step = 0; step < 300; step++)
        {
            int operation = random.Next(100);
            if (reference.Count == 0 || operation < 55 && reference.Count < 64)
            {
                int value = random.Next(-20, 21);
                reference.Enqueue(value);
                trace.Add($"values.Enqueue(new Owned({value}));");
                trace.Created++;
            }
            else if (operation < 87)
            {
                int expected = reference.Dequeue();
                trace.Add("{ Owned removed = values.Dequeue();");
                trace.Check($"removed.Value == {expected} && removed.Label.Length == 4", $"queue remove {step}");
                trace.Add("}");
            }
            else if (operation < 97)
                trace.Add($"values.Reserve({reference.Count + random.Next(1, 25)});");
            else { reference.Clear(); trace.Add("values.Clear();"); }

            trace.Check($"values.Count == {reference.Count}", $"queue count {step}");
            if (reference.TryPeek(out int front))
                trace.Check($"values.Front.Value == {front} && values.Front.Label.Length == 4", $"queue front {step}");
            if (step % 7 == 0) trace.CheckSnapshot(reference.ToArray(), $"queue sequence {step}");
            trace.CheckOwners(reference.Count, $"queue ownership {step}");
        }
        Run(trace.Complete(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void VectorTraceMatchesDotNetAcrossOrderedAndUnorderedMutations(int optimization)
    {
        var random = new Random(87129);
        var reference = new List<int>();
        var trace = new Trace(OwnedPrelude + """
            bool Matches(Vector<Owned>* values, readonly(int)* expected, nuint count) {
                if (values.Count != count) return false;
                for (nuint i = 0; i < count; i++)
                    if ((*values)[i].Value != expected[i] || (*values)[i].Label.Length != 4) return false;
                return true;
            }
            int main() { { Vector<Owned> values = new();
            """);
        for (int step = 0; step < 300; step++)
        {
            int operation = random.Next(100);
            if (reference.Count == 0 || operation < 50 && reference.Count < 64)
            {
                int position = random.Next(reference.Count + 1), value = random.Next(-100, 101);
                reference.Insert(position, value);
                trace.Add($"values.Insert({position}, new Owned({value}));");
                trace.Created++;
            }
            else if (operation < 70)
            {
                int position = random.Next(reference.Count), expected = reference[position];
                reference.RemoveAt(position);
                trace.Add($"{{ Owned removed = values.RemoveAt({position});");
                trace.Check($"removed.Value == {expected} && removed.Label.Length == 4", $"vector remove {step}");
                trace.Add("}");
            }
            else if (operation < 88)
            {
                int position = random.Next(reference.Count), expected = reference[position];
                reference[position] = reference[^1]; reference.RemoveAt(reference.Count - 1);
                trace.Add($"{{ Owned removed = values.SwapRemoveAt({position});");
                trace.Check($"removed.Value == {expected} && removed.Label.Length == 4", $"vector swap-remove {step}");
                trace.Add("}");
            }
            else if (operation < 96)
            {
                int position = random.Next(reference.Count), value = random.Next(-100, 101);
                reference[position] = value; trace.Add($"values[{position}].Value = {value};");
            }
            else
            {
                int length = random.Next(reference.Count + 1);
                reference.RemoveRange(length, reference.Count - length);
                trace.Add($"values.Truncate({length});");
            }
            if (step % 17 == 0) trace.Add($"values.Reserve({reference.Count + random.Next(1, 25)});");
            trace.CheckSnapshot(reference.ToArray(), $"vector sequence {step}");
            trace.CheckOwners(reference.Count, $"vector ownership {step}");
        }
        Run(trace.Complete(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PriorityQueueTraceMatchesDotNetAndRecoversFromDeepComparerFailures(int optimization)
    {
        var random = new Random(42123);
        var reference = new PriorityQueue<int, int>();
        var trace = new Trace(OwnedPrelude + """
            int compareBudget = -1;
            int Compare(immutable(Owned)* left, immutable(Owned)* right) {
                if (compareBudget == 0) throw new Exception("comparison failed");
                if (compareBudget > 0) compareBudget--;
                if (left.Value < right.Value) return -1;
                if (left.Value > right.Value) return 1;
                return 0;
            }
            int main() { { PriorityQueue<Owned> values = new(Compare);
            """);
        for (int value = 0; value < 64; value++)
        {
            trace.Add($"values.Enqueue(new Owned({value}));");
            trace.Created++; reference.Enqueue(value, value);
        }
        foreach (int budget in new[] { 0, 1, 3, 5, 8 })
        {
            trace.Add($"{{ compareBudget = {budget}; bool caught = false;");
            trace.Add("try { Owned removed = values.Dequeue(); } catch (ref Exception error) { caught = true; }");
            trace.Add("compareBudget = -1;");
            trace.Check("caught && values.Count == 64 && values.Top.Value == 0", $"priority failed removal budget {budget}");
            trace.Add("}");
            trace.CheckOwners(64, $"priority ownership after failed removal {budget}");
        }
        foreach (int budget in new[] { 0, 1, 2, 3, 4, 5 })
        {
            trace.Add($"{{ compareBudget = {budget}; bool caught = false;");
            trace.Add("try { values.Enqueue(new Owned(-100)); } catch (ref Exception error) { caught = true; }");
            trace.Created++;
            trace.Add("compareBudget = -1;");
            trace.Check("caught && values.Count == 64 && values.Top.Value == 0", $"priority failed insertion budget {budget}");
            trace.Add("}");
            trace.CheckOwners(64, $"priority ownership after failed insertion {budget}");
        }
        for (int step = 0; step < 240; step++)
        {
            int operation = random.Next(100);
            if (reference.Count == 0 || operation < 55 && reference.Count < 100)
            {
                int value = random.Next(-8, 9);
                reference.Enqueue(value, value); trace.Add($"values.Enqueue(new Owned({value}));"); trace.Created++;
            }
            else if (operation < 98)
            {
                int expected = reference.Dequeue();
                trace.Add("{ Owned removed = values.Dequeue();");
                trace.Check($"removed.Value == {expected} && removed.Label.Length == 4", $"priority removal {step}");
                trace.Add("}");
            }
            else { reference.Clear(); trace.Add("values.Clear();"); }
            trace.Check($"values.Count == {reference.Count}", $"priority count {step}");
            if (reference.TryPeek(out int top, out _))
                trace.Check($"values.Top.Value == {top} && values.Top.Label.Length == 4", $"priority top {step}");
            trace.CheckOwners(reference.Count, $"priority ownership {step}");
        }
        while (reference.TryDequeue(out int expected, out _))
        {
            trace.Add("{ Owned removed = values.Dequeue();");
            trace.Check($"removed.Value == {expected}", "priority final drain"); trace.Add("}");
        }
        trace.CheckOwners(0, "priority drained ownership");
        Run(trace.Complete(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BitSetTraceMatchesBitArrayAtWordBoundaries(int optimization)
    {
        var random = new Random(99183);
        int[] lengths = [0, 1, 2, 7, 31, 32, 33, 63, 64, 65, 95, 96, 97, 127, 128, 129];
        var reference = new BitArray(0);
        var trace = new Trace("""
            using std; using std::Queries; using std::Collections;
            extern int puts(immutable(char*) message);
            bool MatchesBits(immutable(BitSet)* values, readonly(uint)* words, nuint length, nuint count) {
                if (values.Length != length || values.CountSetBits() != count) return false;
                for (nuint i = 0; i < length; i++) {
                    bool expected = (words[i / 32] & ((uint)1 << (int)(i % 32))) != (uint)0;
                    if ((*values)[i] != expected) return false;
                }
                return true;
            }
            int main() { { BitSet values = new();
            """);
        for (int step = 0; step < 240; step++)
        {
            int operation = random.Next(9);
            if (reference.Length == 0 || operation == 0)
            {
                reference.Length = lengths[random.Next(lengths.Length)]; trace.Add($"values.Resize({reference.Length});");
            }
            else if (operation <= 2)
            {
                int index = random.Next(reference.Length); reference[index] = !reference[index]; trace.Add($"values.Flip({index});");
            }
            else if (operation == 3)
            {
                bool value = random.Next(2) == 0; reference.SetAll(value); trace.Add($"values.SetAll({Boolean(value)});");
            }
            else if (operation == 4) { reference.SetAll(false); trace.Add("values.Clear();"); }
            else
            {
                var other = new BitArray(reference.Length);
                trace.Add($"{{ BitSet other = new({reference.Length});");
                for (int i = 0; i < other.Length; i++)
                    if (random.Next(3) == 0) { other[i] = true; trace.Add($"other[{i}] = true;"); }
                if (operation == 5) { reference.Or(other); trace.Add("values.UnionWith(&other);"); }
                else if (operation == 6) { reference.And(other); trace.Add("values.IntersectWith(&other);"); }
                else if (operation == 7) { reference.Xor(other); trace.Add("values.SymmetricExceptWith(&other);"); }
                else { reference.And(other.Not()); trace.Add("values.ExceptWith(&other);"); }
                trace.Add("}");
            }
            uint[] words = new uint[5]; int count = 0;
            for (int i = 0; i < reference.Length; i++)
                if (reference[i]) { words[i / 32] |= 1u << (i % 32); count++; }
            trace.Add("{ uint[5] expected = [" + string.Join(",", words.Select(w => "(uint)" + w)) + "];");
            trace.Check($"MatchesBits(&values, (readonly(uint)*)expected, {reference.Length}, {count})", $"bitset state {step}");
            trace.Add("}");
        }
        trace.Add("} return 0; }");
        Run(trace.ToString(), optimization);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void HashMapTraceMatchesDictionaryAcrossTombstonesAndCollisions(int optimization, bool collisions)
    {
        var random = new Random(51071);
        var reference = new Dictionary<int, int>();
        var trace = new Trace(OwnedPrelude + """
            struct Key : IHashable, IEquatable<Key> {
                public int Value;
                public Key(int value) { Value = value; }
                public immutable nuint GetHashCode() =>
            """ + (collisions ? " 1;" : " (nuint)(Value * 17);") + """
                public immutable bool Equals(immutable(Key)* other) => Value == other.Value;
            }
            bool Matches(HashMap<Key, Owned>* values, readonly(int)* expected, nuint count) {
                if (values.Count != count) return false;
                for (int i = 0; i < 96; i++) {
                    Key key = new(i); Owned*? found = values.Find(&key);
                    if (expected[i] == -2147483647) {
                        if (found != null || values.ContainsKey(&key)) return false;
                    } else {
                        if (found == null || found!.Value != expected[i] || found!.Label.Length != 4 || !values.ContainsKey(&key)) return false;
                    }
                }
                nuint seen = 0;
                HashMap<Key, Owned>::Enumerator iterator = values.GetEnumerator();
                while (iterator.MoveNext()) {
                    int key = iterator.Current.Key.Value;
                    if (key < 0 || key >= 96 || iterator.Current.ValueImmutable.Value != expected[key]) return false;
                    seen++;
                }
                return seen == count;
            }
            int main() { { HashMap<Key, Owned> values = new(); values.Reserve(24);
            """);
        // With distinct hashes modulo48, new keys consume empty buckets rather
        // than deleted buckets, forcing the table's tombstone cleanup threshold.
        for (int key = 0; key < 24; key++) Set(key, key);
        for (int key = 0; key < 16; key++) Remove(key, "initial tombstones");
        for (int key = 24; key < 40; key++) Set(key, key);
        Snapshot("after tombstone cleanup");
        for (int step = 0; step < 300; step++)
        {
            int key = random.Next(96), value = random.Next(-100, 101), operation = random.Next(100);
            if (operation < 42) Set(key, value);
            else if (operation < 67)
            {
                bool expected = reference.TryAdd(key, value);
                trace.Check($"values.TryAdd(new Key({key}), new Owned({value})) == {Boolean(expected)}", $"map TryAdd {step}");
                trace.Created++;
            }
            else if (operation < 94) Remove(key, $"map remove {step}");
            else if (operation < 98) trace.Add($"values.Reserve({random.Next(4, 110)});");
            else { reference.Clear(); trace.Add("values.Clear();"); }
            if (step % 3 == 0) Snapshot($"map state {step}");
            trace.Check($"values.Count == {reference.Count}", $"map count {step}");
            trace.CheckOwners(reference.Count, $"map ownership {step}");
        }
        Snapshot("map final state");
        Run(trace.Complete(), optimization);

        void Set(int key, int value)
        {
            reference[key] = value; trace.Add($"values.Set(new Key({key}), new Owned({value}));"); trace.Created++;
        }
        void Remove(int key, string label)
        {
            bool expected = reference.Remove(key);
            trace.Add($"{{ Key key = new({key});");
            trace.Check($"values.Remove(&key) == {Boolean(expected)}", label); trace.Add("}");
        }
        void Snapshot(string label)
        {
            int[] expected = Enumerable.Range(0, 96).Select(key => reference.GetValueOrDefault(key, -2147483647)).ToArray();
            trace.CheckSnapshot(expected, label, reference.Count);
            trace.CheckOwners(reference.Count, label + " ownership");
        }
    }

    private static string Boolean(bool value) => value ? "true" : "false";

    private sealed class Trace(string prelude)
    {
        private readonly StringBuilder source = new(prelude);
        public int Created { get; set; }
        public void Add(string code) => source.AppendLine(code);
        public void Check(string condition, string label)
            => Add($"if (!({condition})) {{ puts(\"{label}\"); return 1; }}");
        public void CheckOwners(int count, string label)
            => Check($"made == {Created} && destroyed == {Created - count}", label);
        public void CheckSnapshot(int[] values, string label, int? count = null)
        {
            Add($"{{ int[{Math.Max(1, values.Length)}] expected = [" + string.Join(",", values.Length == 0 ? [0] : values) + "];");
            Check($"Matches(&values, (readonly(int)*)expected, {count ?? values.Length})", label);
            Add("}");
        }
        public string Complete()
        {
            Add("}");
            Check($"made == {Created} && destroyed == {Created}", "final ownership");
            Add("return 0; }");
            return ToString();
        }
        public override string ToString() => source.ToString();
    }

    private static void Run(string program, int optimization)
    {
        string[] files = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Vector.gf", "Span.gf", "Collections/Queue.gf",
            "Collections/PriorityQueue.gf", "Collections/BitSet.gf", "Collections/HashMap.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf",
            "IComparable.gf", "Hashing.gf", "String.gf", "ByteSearch.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("trace.gf", program));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_collection_trace_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "trace.ll"), executable = Path.Combine(directory, "trace.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native trace exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
