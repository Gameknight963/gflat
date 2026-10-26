using System.Text;

namespace gflat.Tests;

public sealed class StandardHashSetRelationTests
{
    private const string Prelude = """
        using std; using std::Collections;
        extern int puts(immutable(char*) message);
        int made = 0; int destroyed = 0; int allocations = 0; int releases = 0;
        bool failHash = false; bool failEquality = false;
        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint bytes) { allocations++; return malloc(bytes); }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        struct Key : IHashable, IEquatable<Key> {
            public int Value;
            public String Label;
            public Key(int value) { Value = value; Label = s"owned"; made++; }
            ~Key() { destroyed++; }
            public immutable nuint GetHashCode() {
                if (failHash) throw new Exception("hash failed");
                return 1;
            }
            public immutable bool Equals(immutable(Key)* other) {
                if (failEquality) throw new Exception("equality failed");
                return Value == other.Value;
            }
        }
        """;

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void ReadOnlyRelationsMatchDotNetWithoutCopyingOrAllocating(int optimization, bool collisions)
    {
        var random = new Random(73119);
        var packet = new List<byte>();
        for (int scenario = 0; scenario < 80; scenario++)
        {
            int[] leftValues = scenario == 0 ? [] : Enumerable.Range(0, random.Next(24)).Select(_ => random.Next(16)).ToArray();
            int[] rightValues = scenario == 1 ? [] : (scenario % 4) switch
            {
                0 => leftValues,
                1 => leftValues.Where(value => value % 2 == 0).ToArray(),
                2 => Enumerable.Range(16, random.Next(12)).ToArray(),
                _ => Enumerable.Range(0, random.Next(24)).Select(_ => random.Next(16)).ToArray()
            };
            HashSet<int> left = [.. leftValues], right = [.. rightValues];
            packet.Add(checked((byte)leftValues.Length));
            packet.Add(checked((byte)rightValues.Length));
            packet.Add(checked((byte)left.Count));
            packet.Add(checked((byte)right.Count));
            packet.AddRange(leftValues.Select(value => checked((byte)value)));
            packet.AddRange(rightValues.Select(value => checked((byte)value)));
            packet.Add(RelationMask(left, right));
            packet.Add(RelationMask(right, left));
            packet.Add(RelationMask(left, left));
        }

        string hex = Convert.ToHexString(packet.ToArray());
        string prelude = collisions ? Prelude : Prelude.Replace("return 1;", "return (nuint)(Value * 17);", StringComparison.Ordinal);
        string source = prelude + $$$"""
            int Nibble(char value) {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            int ReadByte(readonly(char)* data, ref nuint position) {
                int value = Nibble(data[position]) * 16 + Nibble(data[position + 1]);
                position += 2;
                return value;
            }
            bool CheckRelations(immutable(HashSet<Key>)* left, immutable(HashSet<Key>)* right, int expected) {
                if (left.SetEquals(right) != ((expected & 1) != 0)) return false;
                if (left.IsSubsetOf(right) != ((expected & 2) != 0)) return false;
                if (left.IsSupersetOf(right) != ((expected & 4) != 0)) return false;
                if (left.IsProperSubsetOf(right) != ((expected & 8) != 0)) return false;
                if (left.IsProperSupersetOf(right) != ((expected & 16) != 0)) return false;
                if (left.Overlaps(right) != ((expected & 32) != 0)) return false;
                return true;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                int created = 0;
                for (int scenario = 0; scenario < 80; scenario++) {
                    int leftCount = ReadByte(packet, ref position);
                    int rightCount = ReadByte(packet, ref position);
                    int leftUnique = ReadByte(packet, ref position);
                    int rightUnique = ReadByte(packet, ref position);
                    {
                        HashSet<Key> left = new();
                        HashSet<Key> right = new();
                        for (int i = 0; i < leftCount; i++) left.Add(new Key(ReadByte(packet, ref position)));
                        for (int i = 0; i < rightCount; i++) right.Add(new Key(ReadByte(packet, ref position)));
                        created += leftCount + rightCount;
                        int forward = ReadByte(packet, ref position);
                        int backward = ReadByte(packet, ref position);
                        int self = ReadByte(packet, ref position);
                        immutable(HashSet<Key>)* a = &left;
                        immutable(HashSet<Key>)* b = &right;
                        int beforeAllocations = allocations;
                        int beforeReleases = releases;
                        if (!CheckRelations(a, b, forward) || !CheckRelations(b, a, backward) || !CheckRelations(a, a, self)) {
                            puts("relation mismatch"); return scenario + 1;
                        }
                        if (made != created || destroyed != created - leftUnique - rightUnique) {
                            puts("keys changed"); return scenario + 1;
                        }
                        if (allocations != beforeAllocations || releases != beforeReleases) {
                            puts("relations allocated or released"); return scenario + 1;
                        }
                    }
                    if (made != created || destroyed != created) {
                        puts("keys not destroyed"); return scenario + 1;
                    }
                }
                if (position != {{{hex.Length}}} || allocations != releases) return 81;
                return 0;
            }
            """;
        Run(source, optimization);

        static byte RelationMask(HashSet<int> left, HashSet<int> right)
        {
            int mask = 0;
            if (left.SetEquals(right)) mask |= 1;
            if (left.IsSubsetOf(right)) mask |= 2;
            if (left.IsSupersetOf(right)) mask |= 4;
            if (left.IsProperSubsetOf(right)) mask |= 8;
            if (left.IsProperSupersetOf(right)) mask |= 16;
            if (left.Overlaps(right)) mask |= 32;
            return checked((byte)mask);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingHashAndEqualityLeaveBothSetsIntact(bool equality)
    {
        string failFlag = equality ? "failEquality" : "failHash";
        var source = new StringBuilder(Prelude).AppendLine("""
            int main() { {
                HashSet<Key> smaller = new(); HashSet<Key> larger = new();
                smaller.Add(new Key(1)); smaller.Add(new Key(2));
                larger.Add(new Key(1)); larger.Add(new Key(2)); larger.Add(new Key(3));
                immutable(HashSet<Key>)* a = &smaller; immutable(HashSet<Key>)* b = &larger;
                int caught = 0;
            """);
        foreach (string call in new[] { "a.SetEquals(a)", "a.IsSubsetOf(b)", "b.IsSupersetOf(a)",
            "a.IsProperSubsetOf(b)", "b.IsProperSupersetOf(a)", "a.Overlaps(b)" })
        {
            source.AppendLine($"{failFlag} = true; try {{ bool ignored = {call}; }} catch (ref Exception error) {{ caught++; }} {failFlag} = false;");
            source.AppendLine("if (smaller.Count != 2 || larger.Count != 3 || made != 5 || destroyed != 0) return 1;");
            source.AppendLine("if (!a.IsSubsetOf(b) || !b.IsSupersetOf(a)) return 2;");
        }
        source.AppendLine("""
                if (caught != 6) return 3;
                HashSet<Key>::Enumerator iterator = a.GetEnumerator(); int sum = 0;
                while (iterator.MoveNext()) { sum += iterator.Current.Value; if (iterator.Current.Label.Length != 5) return 4; }
                if (sum != 3) return 5;
            }
            if (made != 5 || destroyed != 5 || allocations != releases) return 6;
            return 0; }
            """);
        Run(source.ToString(), 0);
    }

    private static void Run(string program, int optimization)
    {
        string[] files = ["IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "Collections/HashMap.gf", "Collections/HashSet.gf",
            "IStringConvertable.gf", "String.gf", "Text/ByteSearch.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("relations.gf", program));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_set_relations_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "relations.ll"), executable = Path.Combine(directory, "relations.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native relations exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
