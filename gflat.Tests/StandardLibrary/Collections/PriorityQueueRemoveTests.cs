using System.Text;

namespace gflat.Tests.Std.Collections;

public sealed partial class PriorityQueueRemoveTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("PRIORITY_QUEUE_REMOVE.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/PriorityQueue.gf", "core/Collections/EqualityComparer.gf",
            "core/Collections/priority_queue_remove.gf"))
        .Append(new SourceFile("priority-queue-remove.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedDuplicateOperationsMatchDotNetAndMissingRemovalPreservesOutput(int optimization)
    {
        var random = new Random(518293);
        var queue = new PriorityQueue<int, int>();
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
        {
            for (int step = 0; step < 1024; step++)
            {
                int operation = random.Next(10);
                int value = random.Next(-16, 17);
                bool found = false;
                int removed = -777;
                if (operation < 5) queue.Enqueue(value, value);
                else if (operation < 9)
                {
                    found = queue.Remove(value, out int result, out int priority);
                    if (found) { removed = result; Assert.Equal(value, priority); }
                }
                else if (queue.TryDequeue(out int result, out _)) { found = true; removed = result; }
                writer.Write(operation);
                writer.Write(value);
                writer.Write(found ? 1 : 0);
                writer.Write(removed);
                writer.Write(queue.Count);
                writer.Write(queue.TryPeek(out int first, out _) ? first : -999);
            }
            writer.Write(queue.Count);
            while (queue.TryDequeue(out int value, out _)) writer.Write(value);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$"""
            using Standard;
            using Standard::Collections;

            int Compare(immutable(int)* left, immutable(int)* right) => *left - *right;
            bool Equal(immutable(int)* left, immutable(int)* right) => *left == *right;
            int Digit(char value)
            {
                if (value <= '9') return value - '0';
                return value - 'A' + 10;
            }
            int Read(readonly(char)* data, ref nuint at)
            {
                uint result = 0u;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    int value = Digit(data[at]) * 16 + Digit(data[at + 1]);
                    at += 2;
                    result = result | ((uint)value << shift);
                }
                return (int)result;
            }
            int main()
            {
                PriorityQueue<int> queue = new(Compare);
                readonly(char)* data = "{{hex}}";
                nuint at = 0;
                for (int step = 0; step < 1024; step++)
                {
                    int operation = Read(data, ref at);
                    int value = Read(data, ref at);
                    bool expected = Read(data, ref at) == 1;
                    int expectedOutput = Read(data, ref at);
                    nuint count = (nuint)Read(data, ref at);
                    int top = Read(data, ref at);
                    bool found = false;
                    int output = -777;
                    if (operation < 5) queue.Enqueue(value);
                    else if (operation < 9)
                    {
                        if (step % 4 == 0) found = queue.Remove(&value, ref output);
                        else if (step % 4 == 1) found = queue.Remove(&value, ref output, Equal);
                        else if (step % 4 == 2) found = Standard::Collections::Remove(ref queue, &value, ref output);
                        else found = Standard::Collections::Remove(ref queue, &value, ref output, Equal);
                    }
                    else found = queue.TryDequeue(ref output);
                    if (found != expected || output != expectedOutput || queue.Count != count) return 1;
                    if (!queue.IsEmpty && queue.Top != top) return 2;
                }
                nuint remaining = (nuint)Read(data, ref at);
                if (queue.Count != remaining) return 3;
                for (nuint i = 0; i < remaining; i++)
                    if (queue.Dequeue() != Read(data, ref at)) return 4;
                if (!queue.IsEmpty || at != {{hex.Length}}) return 5;
                return 0;
            }
            """, optimization);
    }
}
