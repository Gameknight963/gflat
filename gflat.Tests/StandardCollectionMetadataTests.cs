using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class StandardCollectionMetadataTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => new[] { "Collections/IEnumerable.gf", "Collections/Enumerable.gf", "Collections/Vector.gf", "Span.gf", "Collections/Queue.gf",
            "Collections/Stack.gf", "Collections/PriorityQueue.gf", "Collections/BitSet.gf", "Collections/HashMap.gf", "Collections/HashSet.gf", "Collections/LinkedList.gf",
            "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("metadata.gf", program));

    [Fact]
    public void MetadataGettersUseOrdinaryCallsAndHaveNonthrowingContracts()
    {
        string ir = Compiler.Emit(Sources("""
            using std;
            bool ReadMetadata(immutable(Vector<int>)* vector, Span<int>* span, immutable(Queue<int>)* queue,
                immutable(Stack<int>)* stack, immutable(PriorityQueue<int>)* priority, immutable(BitSet)* bits,
                immutable(HashMap<int,int>)* map, immutable(HashSet<int>)* set, immutable(LinkedList<int>)* list) nothrow {
                return vector.Count == 0 && vector.Capacity == 0 && vector.IsEmpty && span.Length == 0 && span.IsEmpty && span.Data == null
                    && queue.Count == 0 && queue.Capacity == 0 && queue.IsEmpty
                    && stack.Count == 0 && stack.Capacity == 0 && stack.IsEmpty
                    && priority.Count == 0 && priority.Capacity == 0 && priority.IsEmpty && bits.Length == 0
                    && map.Count == 0 && map.Capacity == 0 && map.IsEmpty
                    && set.Count == 0 && set.Capacity == 0 && set.IsEmpty && list.Count == 0 && list.IsEmpty;
            }
            int main() => 0;
            """));
        string body = Regex.Match(ir, @"(?ms)^define [^\n]*@gflat\$ReadMetadata\([^\n]*\{\r?\n(?<body>.*?)^\}")
            .Groups["body"].Value;
        Assert.NotEmpty(body);
        Assert.DoesNotContain("invoke", body);
        var calls = Regex.Matches(body, @"call [^\n]*@(?<name>[^\s(]*\$get\$[^\s(]*)\(");
        Assert.Equal(24, calls.Count);
        foreach (Match call in calls)
        {
            string name = call.Groups["name"].Value;
            string declaration = Assert.Single(ir.Split('\n'), line => line.StartsWith("define ") && line.Contains("@" + name + "("));
            Assert.Contains("nounwind", declaration);
        }
    }

    [Fact]
    public void EmptyAndPopulatedMetadataWorkWhileInvalidAccessesStillThrow()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int Compare(readonly(int)* a, readonly(int)* b) => *a - *b;
            int main() {
                Vector<int> vector = new(); Span<int> span = new(); Queue<int> queue = new(); Stack<int> stack = new();
                PriorityQueue<int> priority = new(Compare); BitSet bits = new(); HashMap<int,int> map = new();
                HashSet<int> set = new(); LinkedList<int> list = new();
                if (vector.Count != 0 || vector.Capacity != 0 || !vector.IsEmpty || span.Length != 0 || !span.IsEmpty || span.Data != null) return 1;
                if (queue.Count != 0 || queue.Capacity != 0 || !queue.IsEmpty || stack.Count != 0 || stack.Capacity != 0 || !stack.IsEmpty) return 2;
                if (priority.Count != 0 || priority.Capacity != 0 || !priority.IsEmpty || bits.Length != 0) return 3;
                if (map.Count != 0 || map.Capacity != 0 || !map.IsEmpty || set.Count != 0 || set.Capacity != 0 || !set.IsEmpty) return 4;
                if (list.Count != 0 || !list.IsEmpty) return 5;
                int caught = 0; int key = 1;
                try { int ignored = vector[0]; } catch { caught++; }
                try { int ignored = span[0]; } catch { caught++; }
                try { int ignored = queue.Front; } catch { caught++; }
                try { int ignored = stack.Top; } catch { caught++; }
                try { int ignored = priority.Top; } catch { caught++; }
                try { bool ignored = bits[0]; } catch { caught++; }
                try { int ignored = map[&key]; } catch { caught++; }
                try { list.RemoveFirst(); } catch { caught++; }
                if (caught != 8) return 6;
                vector.Add(1); span = vector.AsSpan(); queue.Enqueue(2); stack.Push(3); priority.Enqueue(4);
                bits.Resize(65); map.Add(1,5); set.Add(6); list.AddLast(7);
                if (vector.Count != 1 || vector.Capacity < 1 || vector.IsEmpty || span.Length != 1 || span.IsEmpty || span.Data == null) return 7;
                if (queue.Count != 1 || queue.Capacity < 1 || queue.IsEmpty || stack.Count != 1 || stack.Capacity < 1 || stack.IsEmpty) return 8;
                if (priority.Count != 1 || priority.Capacity < 1 || priority.IsEmpty || bits.Length != 65) return 9;
                if (map.Count != 1 || map.Capacity < 1 || map.IsEmpty || set.Count != 1 || set.Capacity < 1 || set.IsEmpty) return 10;
                if (list.Count != 1 || list.IsEmpty) return 11;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
