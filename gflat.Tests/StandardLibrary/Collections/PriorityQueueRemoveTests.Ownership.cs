namespace gflat.Tests.Std.Collections;

public sealed partial class PriorityQueueRemoveTests
{
    [Theory]
    [InlineData("up", 0)]
    [InlineData("up", 2)]
    [InlineData("down", 0)]
    [InlineData("down", 2)]
    [InlineData("one-child", 0)]
    [InlineData("one-child", 2)]
    [InlineData("last", 0)]
    [InlineData("last", 2)]
    public void ComparisonFailuresPreserveEveryOwnerAndSuccessfulRepairNeedsNoCopiesOrAllocations(string scenario, int optimization)
    {
        int[] input = scenario == "up" ? [0, 10, 1, 11, 12, 2, 3]
            : Enumerable.Range(0, scenario == "one-child" ? 5 : 31).ToArray();
        int target = scenario switch { "up" => 12, "one-child" => 1, "last" => 30, _ => 0 };
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int made = 0;
            int dead = 0;
            int allocations = 0;
            int releases = 0;
            int comparisons = 0;
            int equalities = 0;
            int compareFail = -1;
            int equalFail = -1;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocations++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            struct Item
            {
                public int Priority;
                public String Text;
                public Item(int priority) { Priority = priority; Text = s"owned"; made++; }
                ~Item() { dead++; }
            }
            int Compare(immutable(Item)* left, immutable(Item)* right)
            {
                int index = comparisons;
                comparisons++;
                if (index == compareFail) throw new Exception("compare", 22);
                return left.Priority - right.Priority;
            }
            bool Equal(immutable(Item)* left, immutable(Item)* right)
            {
                int index = equalities;
                equalities++;
                if (index == equalFail) throw new Exception("equal", 21);
                return left.Priority == right.Priority;
            }
            void Fill(PriorityQueue<Item>* queue)
            {
                int[{{input.Length}}] values = [{{string.Join(", ", input)}}];
                queue.Reserve(64);
                for (nuint i = 0; i < {{input.Length}}; i++) queue.Enqueue(new Item(values[i]));
            }
            bool Drain(PriorityQueue<Item>* queue, bool removed)
            {
                int[{{input.Length}}] sorted = [{{string.Join(", ", input.Order())}}];
                for (nuint i = 0; i < {{input.Length}}; i++)
                {
                    if (removed && sorted[i] == {{target}}) continue;
                    Item item = queue.Dequeue();
                    if (item.Priority != sorted[i] || !item.Text.Equals("owned")) return false;
                }
                return queue.IsEmpty;
            }
            int main()
            {
                int measuredEqual = 0;
                int measuredCompare = 0;
                {
                    PriorityQueue<Item> queue = new(Compare);
                    Fill(&queue);
                    Item key = new({{target}});
                    Item output = new(999);
                    comparisons = 0;
                    equalities = 0;
                    int before = allocations;
                    int destroyed = dead;
                    nuint capacity = queue.Capacity;
                    deny = true;
                    if (!queue.Remove(&key, ref output, Equal)) return 1;
                    deny = false;
                    measuredEqual = equalities;
                    measuredCompare = comparisons;
                    if (output.Priority != {{target}} || !output.Text.Equals("owned")
                        || dead != destroyed + 1 || allocations != before
                        || queue.Count != {{input.Length - 1}} || queue.Capacity != capacity) return 2;
                    if (!Drain(&queue, true)) return 3;
                }
                if (made != dead || allocations != releases || measuredEqual == 0) return 4;
                for (int kind = 0; kind < 2; kind++)
                {
                    int count = measuredEqual;
                    if (kind == 1) count = measuredCompare;
                    for (int failure = 0; failure < count; failure++)
                    {
                        {
                            PriorityQueue<Item> queue = new(Compare);
                            Fill(&queue);
                            Item key = new({{target}});
                            Item output = new(999);
                            readonly(char)* oldOutput = output.Text.Data!;
                            nuint[64] addresses = default(nuint[64]);
                            nuint[64] texts = default(nuint[64]);
                            int[64] values = default(int[64]);
                            var before = queue.UnorderedItems.GetEnumerator();
                            nuint index = 0;
                            while (before.MoveNext())
                            {
                                addresses[index] = before.Current.reinterpret<nuint>();
                                texts[index] = before.Current.Text.Data!.reinterpret<nuint>();
                                values[index] = before.Current.Priority;
                                index++;
                            }
                            int live = made - dead;
                            int memory = allocations - releases;
                            comparisons = 0;
                            equalities = 0;
                            if (kind == 0) equalFail = failure;
                            else compareFail = failure;
                            deny = true;
                            int code = 0;
                            try { queue.Remove(&key, ref output, Equal); }
                            catch (Exception* error) { code = error.GetCode(); }
                            deny = false;
                            equalFail = -1;
                            compareFail = -1;
                            if (code != 21 + kind || queue.Count != {{input.Length}} || queue.Capacity != 64
                                || made - dead != live || allocations - releases != memory
                                || output.Priority != 999 || output.Text.Data != oldOutput) return 5;
                            var after = queue.UnorderedItems.GetEnumerator();
                            index = 0;
                            while (after.MoveNext())
                            {
                                if (after.Current.reinterpret<nuint>() != addresses[index]
                                    || after.Current.Text.Data!.reinterpret<nuint>() != texts[index]
                                    || after.Current.Priority != values[index]) return 6;
                                index++;
                            }
                            if (index != {{input.Length}} || !Drain(&queue, false)) return 7;
                        }
                        if (made != dead || allocations != releases) return 8;
                    }
                }
                return 0;
            }
            """, optimization);
    }
}
