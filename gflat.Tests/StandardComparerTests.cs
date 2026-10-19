namespace gflat.Tests;

public sealed class StandardComparerTests
{
    [Fact]
    public void ContractBridgesServePrimitiveAndStringAlgorithmsAndPriorityQueues()
    {
        string[] files = ["Collections/IEnumerable.gf", "Collections/Enumerable.gf", "Collections/Algorithms.gf",
            "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "Comparer.gf", "EqualityComparer.gf",
            "IStringConvertable.gf", "String.gf", "Vector.gf", "Span.gf", "PriorityQueue.gf"];
        var result = CompilerTestHelper.Run(files.Select(file => new SourceFile(file,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("main.gf", """
                using std;
                int main() {
                    PriorityQueue<int> pending = new(Comparer<int>::Compare);
                    pending.Enqueue(9); pending.Enqueue(2); pending.Enqueue(5);
                    if (pending.Dequeue() != 2 || pending.Dequeue() != 5 || pending.Dequeue() != 9) return 1;
                    Vector<int> numbers = new(); numbers.Add(7); numbers.Add(2); numbers.Add(3);
                    Sort<int>(numbers.AsSpan(), Comparer<int>::Compare);
                    if (!IsSorted<int>(numbers.AsReadOnly(), Comparer<int>::Compare)) return 2;
                    if (!SequenceEqual<int>(numbers.AsReadOnly(), numbers.AsReadOnly(), EqualityComparer<int>::Equals)) return 3;
                    Vector<String> words = new(); words.Add(s"pear"); words.Add(s"apple"); words.Add(s"banana");
                    Sort<String>(words.AsSpan(), Comparer<String>::Compare);
                    String apple = s"apple"; String pear = s"pear";
                    if (!words[0].Equals(&apple) || !words[2].Equals(&pear)) return 4;
                    if (!SequenceEqual<String>(words.AsReadOnly(), words.AsReadOnly(), EqualityComparer<String>::Equals)) return 5;
                    return 0;
                }
                """)));
        Assert.Equal(0, result.ExitCode);
    }
}
