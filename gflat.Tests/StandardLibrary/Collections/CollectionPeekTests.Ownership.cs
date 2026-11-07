namespace gflat.Tests.Std.Collections;

public sealed partial class CollectionPeekTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningPeeksCopyIndependentlyAndPreserveOutputAcrossCopyAndAllocationFailures(int optimization)
    {
        const string prelude = """
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            int failAt = 0;
            int live = 0;
            int copies = 0;
            bool deny = false;
            bool failCopy = false;
            bool failCompare = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny || attempts == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; live++; }
                public copy Owner(immutable(Owner)* source)
                {
                    Id = source.Id; Text = source.Text; copies++;
                    if (failCopy) throw new Exception("copy", 17);
                    live++;
                }
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right)
            {
                if (failCompare) throw new Exception("unexpected comparison");
                return left.Id - right.Id;
            }
            """;
        const string checks = """
            int EmptyContainer(immutable(Container<Owner>)* source)
            {
                Owner output = new(77);
                readonly(char)* original = output.Text.Data!;
                int before = attempts;
                int oldCopies = copies;
                deny = true; failCopy = true; failCompare = true;
                bool found = source.TryPeek(ref output);
                deny = false; failCopy = false; failCompare = false;
                if (found || attempts != before || copies != oldCopies || output.Text.Data != original || output.Id != 77) return 1;
                return 0;
            }
            int CheckContainer(immutable(Container<Owner>)* source, immutable(Owner)* stored)
            {
                Owner output = new(77);
                readonly(char)* sourceText = stored.Text.Data!;
                nuint count = source.Count;
                failCompare = true;
                if (!source.TryPeek(ref output) || output.Id != stored.Id || output.Text.Data == sourceText) return 2;
                output.Text.Append("changed");
                if (!stored.Text.Equals("owned")) return 3;
                readonly(char)* original = output.Text.Data!;
                int living = live;
                int memory = allocated - released;
                failCopy = true;
                int code = 0;
                try { source.TryPeek(ref output); }
                catch (Exception* error) { code = error.GetCode(); }
                failCopy = false;
                if (code != 17 || output.Text.Data != original || output.Id != stored.Id || allocated - released != memory || live != living) return 4;
                attempts = 0;
                if (!source.TryPeek(ref output)) return 5;
                int observed = attempts;
                if (observed == 0) return 6;
                for (int failure = 1; failure <= observed; failure++)
                {
                    memory = allocated - released;
                    original = output.Text.Data!;
                    attempts = 0; failAt = failure;
                    bool caught = false;
                    try { source.TryPeek(ref output); }
                    catch (Exception* error) { caught = true; }
                    failAt = 0;
                    if (!caught || output.Text.Data != original || output.Id != stored.Id || allocated - released != memory || live != living) return 7;
                    if (source.Count != count || stored.Text.Data != sourceText || !stored.Text.Equals("owned")) return 8;
                }
                failCompare = false;
                return 0;
            }
            """;
        const string body = """
            int main()
            {
                {
                    Queue<Owner> queue = new();
                    Stack<Owner> stack = new();
                    PriorityQueue<Owner> priority = new(Compare);
                    int result = EmptyQueue(&queue);
                    if (result != 0) return result;
                    result = EmptyStack(&stack);
                    if (result != 0) return result;
                    result = EmptyPriorityQueue(&priority);
                    if (result != 0) return result;
                    for (int i = 1; i <= 3; i++)
                    {
                        queue.Enqueue(new Owner(i)); stack.Push(new Owner(i)); priority.Enqueue(new Owner(i));
                    }
                    result = CheckQueue(&queue, &queue.Front);
                    if (result != 0) return result;
                    result = CheckStack(&stack, &stack.Top);
                    if (result != 0) return result;
                    result = CheckPriorityQueue(&priority, &priority.Top);
                    if (result != 0) return result;
                    if (!queue.TryPeek(ref queue.Front) || !stack.TryPeek(ref stack.Top) || live != 9) return 9;
                    readonly(char)* original = priority.Top.Text.Data!;
                    int before = copies;
                    bool caught = false;
                    Owner* storedOutput = (Owner*)(&priority.Top);
                    try { priority.TryPeek(ref *storedOutput); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || copies != before || priority.Top.Text.Data != original || live != 9) return 10;
                    for (int i = 1; i <= 3; i++)
                    {
                        Owner first = queue.Dequeue();
                        Owner top = stack.Pop();
                        Owner minimum = priority.Dequeue();
                        if (first.Id != i || top.Id != 4 - i || minimum.Id != i) return 11;
                    }
                }
                if (live != 0 || allocated != released) return 12;
                return 0;
            }
            """;
        string functions = string.Join("\n", new[] { "Queue", "Stack", "PriorityQueue" }
            .Select(kind => checks.Replace("Container", kind)));
        Run(prelude + functions + body, optimization);
    }
}
