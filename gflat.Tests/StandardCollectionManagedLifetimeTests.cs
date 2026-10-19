namespace gflat.Tests;

public sealed class StandardCollectionManagedLifetimeTests
{
    [Fact]
    public void OwningContainersAndSortingKeepManagedElementsAliveAndReleaseThem()
    {
        string[] files = [
            "core/Collections/IEnumerable.gf", "core/Collections/Enumerable.gf", "core/Collections/Algorithms.gf",
            "core/Vector.gf", "core/Span.gf", "core/Queue.gf", "core/Stack.gf", "core/PriorityQueue.gf",
            "libc/GC.gf", "libc/TracingCollector.gf"
        ];
        var result = CompilerTestHelper.Run(files.Select(file => new SourceFile("std/" + file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))))
            .Append(new SourceFile("main.gf", """
            using std;
            class Node {
                public int Value;
                public Node^? Next;
                public Node(int value) { Value = value; }
            }
            int Compare(readonly(Node^)* left, readonly(Node^)* right) {
                GC::Collect();
                return (*left).Value - (*right).Value;
            }
            void Check() {
                Queue<Node^> queue = new(); Stack<Node^> stack = new();
                PriorityQueue<Node^> heap = new(Compare); Vector<Node^> values = new();
                for (int i = 31; i >= 0; i--) {
                    Node^ node = new^ Node(i); node.Next = node;
                    queue.Enqueue(node); stack.Push(node); heap.Enqueue(node); values.Add(node);
                }
                GC::Collect();
                Sort<Node^>(values.AsSpan(), Compare);
                for (int i = 0; i < 16; i++) {
                    if (queue.Dequeue().Value != 31 - i) throw new Exception("queue order");
                    if (stack.Pop().Value != i) throw new Exception("stack order");
                    if (heap.Dequeue().Value != i) throw new Exception("heap order");
                }
                for (nuint i = 0; i < values.Count; i++)
                    if (values[i].Value != (int)i) throw new Exception("sort order");
                queue.Clear(); stack.Clear(); heap.Clear();
                GC::Collect();
                if (TracingCollector::ManagedCount != 32) throw new Exception("premature collection");
                values.Clear();
                if (GC::Collect() != 32) throw new Exception("leaked references");
            }
            int main() { Check(); GC::Collect(); return (int)TracingCollector::ManagedCount; }
            """)));
        Assert.Equal(0, result.ExitCode);
    }
}
