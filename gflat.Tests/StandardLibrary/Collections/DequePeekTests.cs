namespace gflat.Tests.Std.Collections;

public sealed class DequePeekTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void WrappedEndsCopyIndependentlyAndPreserveOutputOnFailure(int optimization, bool last)
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
            int EmptyContainer(immutable(Deque<Owner>)* source)
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
            int CheckContainer(immutable(Deque<Owner>)* source, immutable(Owner)* stored)
            {
                Owner output = new(77);
                readonly(char)* sourceText = stored.Text.Data!;
                nuint count = source.Count;
                failCompare = true;
                if (!source.TryPeek(ref output) || output.Id != stored.Id || output.Text.Data == sourceText) return 2;
                output.Text.Append("changed");
                if (!stored.Text.Equals("owned")) return 3;
                {
                    Owner peeked = source.Peek();
                    if (peeked.Id != stored.Id || peeked.Text.Data == sourceText) return 13;
                    peeked.Text.Append("changed");
                    if (!stored.Text.Equals("owned")) return 14;
                }
                readonly(char)* original = output.Text.Data!;
                int living = live;
                int memory = allocated - released;
                failCopy = true;
                int code = 0;
                try { source.TryPeek(ref output); }
                catch (Exception* error) { code = error.GetCode(); }
                int peekCode = 0;
                try { source.Peek(); }
                catch (Exception* error) { peekCode = error.GetCode(); }
                failCopy = false;
                if (peekCode != 17) return 15;
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
                    attempts = 0; failAt = failure;
                    caught = false;
                    try { source.Peek(); }
                    catch (Exception* error) { caught = true; }
                    failAt = 0;
                    if (!caught || allocated - released != memory || live != living) return 16;
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
                    Deque<Owner> deque = new();
                    int result = EmptyContainer(&deque);
                    if (result != 0) return result;
                    bool caught = false;
                    try { deque.PeekEND(); }
                    catch (Exception* error) { caught = true; }
                    if (!caught) return 20;
                    deque.Reserve(4);
                    for (int i = 0; i < 4; i++) deque.AddLast(new Owner(i));
                    { Owner removed = deque.RemoveFirst(); }
                    { Owner removed = deque.RemoveFirst(); }
                    deque.AddLast(new Owner(4));
                    deque.AddLast(new Owner(5));
                    result = CheckContainer(&deque, &deque.PROP);
                    if (result != 0) return result;
                    if (!deque.TryPeekEND(ref deque.PROP) || live != 4) return 21;
                    Owner freeCopy = PeekEND(&deque);
                    Owner output = new(77);
                    if (!TryPeekEND(&deque, ref output) || output.Id != deque.PROP.Id || freeCopy.Id != output.Id) return 22;
                    deque.Clear();
                    if (!output.Text.Equals("owned") || !freeCopy.Text.Equals("owned")) return 23;
                }
                if (live != 0 || allocated != released) return 24;
                return 0;
            }
            """;
        string end = last ? "Last" : "First";
        string program = prelude + checks.Replace("TryPeek", "TryPeek" + end).Replace("source.Peek()", "source.Peek" + end + "()")
            + body.Replace("END", end).Replace("PROP", last ? "Back" : "Front");
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf", "core/Collections/deque_peek.gf"))
            .Append(new SourceFile("deque-peek.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Deque peek exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("DEQUE_PEEK.md", 0);
}
