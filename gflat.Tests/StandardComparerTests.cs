namespace gflat.Tests;

public sealed class StandardComparerTests
{
    [Fact]
    public void ContractBridgesServePrimitiveAndStringAlgorithmsAndPriorityQueues()
    {
        string[] files = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Algorithms.gf",
            "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "Collections/Comparer.gf", "Collections/EqualityComparer.gf",
            "IStringConvertable.gf", "String.gf", "ByteSearch.gf", "Collections/Vector.gf", "Span.gf", "Collections/PriorityQueue.gf"];
        var result = CompilerTestHelper.Run(files.Select(file => new SourceFile(file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("main.gf", """
                using std; using std::Queries; using std::Collections;
                int main() {
                    PriorityQueue<int> pending = new(Comparer<int>::Compare);
                    pending.Enqueue(9); pending.Enqueue(2); pending.Enqueue(5);
                    if (pending.Dequeue() != 2 || pending.Dequeue() != 5 || pending.Dequeue() != 9) return 1;
                    Vector<int> numbers = new(); numbers.Add(7); numbers.Add(2); numbers.Add(3);
                    Sort<int>(numbers.AsSpan(), Comparer<int>::Compare);
                    if (!IsSorted<int>(numbers.AsImmutable(), Comparer<int>::Compare)) return 2;
                    if (!SequenceEqual<int>(numbers.AsImmutable(), numbers.AsImmutable(), EqualityComparer<int>::Equals)) return 3;
                    Vector<String> words = new(); words.Add(s"pear"); words.Add(s"apple"); words.Add(s"banana");
                    Sort<String>(words.AsSpan(), Comparer<String>::Compare);
                    String apple = s"apple"; String pear = s"pear";
                    if (!words[0].Equals(&apple) || !words[2].Equals(&pear)) return 4;
                    if (!SequenceEqual<String>(words.AsImmutable(), words.AsImmutable(), EqualityComparer<String>::Equals)) return 5;
                    return 0;
                }
                """)));
        Assert.Equal(0, result.ExitCode);
    }
}
