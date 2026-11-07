namespace gflat.Tests.Std.Collections;

public sealed partial class PriorityQueueRemoveTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedKeysMayAliasStoredValuesButOutputMustBeOutsideTheWholeAllocation(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            int calls = 0;
            bool forbidden = false;
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (forbidden) throw new Exception("unexpected comparison");
                return *left - *right;
            }
            bool Equal(immutable(int)* left, immutable(int)* right) { calls++; return *left == *right; }
            struct Job : IEquatable<Job>
            {
                public int Id;
                public int Priority;
                public Job(int id, int priority) { Id = id; Priority = priority; }
                public immutable bool Equals(immutable(Job)* other) => Id == other.Id;
            }
            int CompareJobs(immutable(Job)* left, immutable(Job)* right) => left.Priority - right.Priority;
            int main()
            {
                PriorityQueue<int> queue = new(Compare);
                queue.Reserve(32);
                int key = 7;
                int output = 99;
                if (queue.Remove(&key, ref output, Equal) || output != 99 || calls != 0) return 1;
                for (int i = 1; i <= 7; i++) queue.Enqueue(i);
                int* top = (int*)(&queue.Top);
                for (int slot = 0; slot < 2; slot++)
                {
                    int* destination = top;
                    if (slot == 1) destination = top + 31;
                    bool caught = false;
                    calls = 0;
                    try { queue.Remove(&key, ref *destination, Equal); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || calls != 0 || queue.Count != 7 || queue.Top != 1) return 2;
                }
                forbidden = true;
                key = 88;
                if (queue.Remove(&key, ref output, Equal) || output != 99 || calls != 7) return 3;
                forbidden = false;
                if (!queue.Remove(&queue.Top, ref output) || output != 1 || queue.Count != 6) return 4;
                key = 4;
                if (!queue.Remove(&key, ref key) || key != 4 || queue.Count != 5) return 5;
                int[5] expected = [2, 3, 5, 6, 7];
                for (nuint i = 0; i < 5; i++) if (queue.Dequeue() != expected[i]) return 6;
                queue.Enqueue(42);
                forbidden = true;
                key = 42;
                if (!queue.Remove(&key, ref output) || output != 42 || !queue.IsEmpty) return 7;
                PriorityQueue<Job> jobs = new(CompareJobs);
                jobs.Enqueue(new Job(1, 7));
                jobs.Enqueue(new Job(2, 7));
                Job requested = new(2, 99);
                if (!jobs.Remove(&requested, ref requested) || requested.Id != 2 || requested.Priority != 7
                    || jobs.Count != 1 || jobs.Top.Id != 1) return 8;
                return 0;
            }
            """, optimization);
}
