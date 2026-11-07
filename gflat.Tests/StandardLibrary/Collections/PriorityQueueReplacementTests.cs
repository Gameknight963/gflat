namespace gflat.Tests.Std.Collections;

public sealed partial class PriorityQueueFailureTests
{
    [Theory]
    [InlineData("EnqueueDequeue", 0)]
    [InlineData("EnqueueDequeue", 2)]
    [InlineData("DequeueEnqueue", 0)]
    [InlineData("DequeueEnqueue", 2)]
    public void RootReplacementPreflightsEveryComparisonAndMovesOwnersWithoutGrowth(string operation, int optimization)
        => Run(Prelude + $$"""
            int main()
            {
                int comparisons = 0;
                {
                    PriorityQueue<Item> probe = new(Compare);
                    Fill(&probe);
                    calls = 0;
                    Item removed = probe.{{operation}}(new Item(63));
                    if (removed.Priority != 0 || removed.Text.Length != 5) return 1;
                    comparisons = calls;
                }
                if (comparisons < 6) return 2;
                for (int failure = 0; failure < comparisons; failure++)
                {
                    PriorityQueue<Item> items = new(Compare);
                    Fill(&items);
                    immutable(Item)* saved = &items.Top;
                    int memory = allocations - releases;
                    int destroyed = dead;
                    nuint capacity = items.Capacity;
                    calls = 0;
                    failAt = failure;
                    bool caught = false;
                    try { items.{{operation}}(new Item(63)); }
                    catch { caught = true; }
                    failAt = -1;
                    if (!caught || calls != failure + 1 || items.Count != 31 || items.Capacity != capacity) return 3;
                    if (&items.Top != saved || saved.Priority != 0) return 4;
                    if (dead != destroyed + 1 || made - dead != 31 || allocations - releases != memory) return 5;
                    if (!Drain(&items)) return 6;
                }
                {
                    PriorityQueue<Item> items = new(Compare);
                    items.Reserve(31);
                    Fill(&items);
                    Item incoming = new Item(63);
                    int memory = allocations - releases;
                    failAllocation = true;
                    {
                        Item removed = items.{{operation}}(move incoming);
                        if (removed.Priority != 0 || removed.Text.Length != 5) return 7;
                        if (items.Count != 31 || items.Capacity != 31 || allocations - releases != memory) return 8;
                    }
                    failAllocation = false;
                    for (int expected = 1; expected < 31; expected++)
                    {
                        Item removed = items.Dequeue();
                        if (removed.Priority != expected || removed.Text.Length != 5) return 9;
                    }
                    Item last = items.Dequeue();
                    if (last.Priority != 63 || last.Text.Length != 5) return 10;
                }
                if (made != dead || allocations != releases) return 11;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptySingletonAndEqualPriorityReplacementKeepCorrectOwner(int optimization)
        => Run(Prelude + """
            int main()
            {
                {
                    PriorityQueue<Item> items = new(Compare);
                    Item incoming = new Item(1);
                    calls = 0;
                    failAllocation = true;
                    {
                        Item returned = items.EnqueueDequeue(move incoming);
                        if (returned.Priority != 1 || returned.Text.Length != 5 || !items.IsEmpty || calls != 0) return 1;
                    }
                    failAllocation = false;
                    int destroyed = dead;
                    bool caught = false;
                    try { items.DequeueEnqueue(new Item(2)); }
                    catch { caught = true; }
                    if (!caught || !items.IsEmpty || dead != destroyed + 1 || calls != 0) return 2;
                    items.Enqueue(new Item(4));
                    readonly(char)* original = items.Top.Text.Data!;
                    {
                        Item same = new Item(4);
                        readonly(char)* replacement = same.Text.Data!;
                        Item returned = items.EnqueueDequeue(move same);
                        if (items.Top.Text.Data != original || returned.Text.Data != replacement) return 3;
                    }
                    {
                        Item same = new Item(4);
                        readonly(char)* replacement = same.Text.Data!;
                        Item returned = items.DequeueEnqueue(move same);
                        if (items.Top.Text.Data != replacement || returned.Text.Data != original) return 4;
                    }
                    {
                        Item returned = items.EnqueueDequeue(new Item(-10));
                        if (returned.Priority != -10 || items.Top.Priority != 4) return 5;
                    }
                    {
                        Item returned = items.DequeueEnqueue(new Item(-10));
                        if (returned.Priority != 4 || items.Top.Priority != -10) return 6;
                    }
                }
                if (made != dead || allocations != releases) return 7;
                return 0;
            }
            """, optimization);
}

public sealed class PriorityQueueReplacementTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("PRIORITY_QUEUE_REPLACEMENT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedOperationsMatchDotNetPriorityQueue(int optimization)
    {
        var oracle = new PriorityQueue<int, int>();
        var random = new Random(148752);
        var packet = new List<byte>();
        for (int step = 0; step < 1000; step++)
        {
            int choice = random.Next(30);
            int operation = choice < 10 ? 0 : choice < 15 ? 1 : choice < 22 ? 2 : choice < 29 ? 3 : 4;
            if (oracle.Count == 0 && (operation == 1 || operation == 3)) operation = 0;
            int value = random.Next(-50, 51);
            int result = 155;
            switch (operation)
            {
                case 0: oracle.Enqueue(value, value); break;
                case 1: result = oracle.Dequeue(); break;
                case 2: result = oracle.EnqueueDequeue(value, value); break;
                case 3: result = oracle.DequeueEnqueue(value, value); break;
                case 4: oracle.Clear(); break;
            }
            packet.AddRange([(byte)operation, (byte)(value + 100), (byte)(result + 100),
                (byte)oracle.Count, (byte)(oracle.Count == 0 ? 255 : oracle.Peek() + 100)]);
        }
        string hex = Convert.ToHexString(packet.ToArray()).ToLowerInvariant();
        string program = $$"""
            using Standard;
            using Standard::Collections;

            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -1;
                if (*left > *right) return 1;
                return 0;
            }
            int Next(readonly(char)* packet, ref nuint position)
            {
                int result = 0;
                for (int i = 0; i < 2; i++)
                {
                    char digit = packet[position];
                    position++;
                    int value = (int)digit - (int)'0';
                    if (digit >= 'a') value = (int)digit - (int)'a' + 10;
                    result = result * 16 + value;
                }
                return result;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                PriorityQueue<int> queue = new(Compare);
                for (int step = 0; step < 1000; step++)
                {
                    int operation = Next(packet, ref position);
                    int value = Next(packet, ref position) - 100;
                    int expected = Next(packet, ref position) - 100;
                    int count = Next(packet, ref position);
                    int top = Next(packet, ref position) - 100;
                    int result = 155;
                    if (operation == 0) queue.Enqueue(value);
                    if (operation == 1) result = queue.Dequeue();
                    if (operation == 2) result = queue.EnqueueDequeue(value);
                    if (operation == 3) result = queue.DequeueEnqueue(value);
                    if (operation == 4) queue.Clear();
                    if (result != expected || queue.Count != (nuint)count) return 1;
                    if (count != 0 && queue.Top != top) return 2;
                }
                return 0;
            }
            """;
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/PriorityQueue.gf"))
            .Append(new SourceFile("priority-replacement.gf", program));
        var nativeResult = CompilerTestHelper.Run(sources, optimization);
        Assert.True(nativeResult.ExitCode == 0, $"Priority replacement exit {nativeResult.ExitCode}: {nativeResult.StandardError}");
    }
}
