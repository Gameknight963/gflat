namespace gflat.Tests.Std.Queries;

public sealed class TryExtremaByTests
{
    private const string Allocator = """
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
        private extern void*? malloc(nuint count);
        private extern void free(void* storage);
        int attempts = 0;
        int allocated = 0;
        int released = 0;
        int failAt = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint count)
            {
                attempts++;
                if (attempts == failAt) return null;
                void*? result = malloc(count);
                if (result != null) allocated++;
                return result;
            }
            public replace void Free(void* storage) nothrow { released++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StatefulSourceIsScannedOnceWithFirstTiesAndExactCallbackOrder(int optimization)
        => Run(Allocator + """
            int cursor = 0; int length = 5; int starts = 0; int ends = 0;
            int reads = 0; int selections = 0; int comparisons = 0; int events = 0;
            int[5] data = [8, 4, 7, 1, 5];
            int[32] trace;
            void Event(int value) { trace[(nuint)events] = value; events++; }
            struct Item
            {
                public int Raw; public int Id;
                public Item(int raw, int id) { Raw = raw; Id = id; }
            }
            struct Source
            {
                public struct Iterator
                {
                    private int currentIndex;
                    public Iterator() { currentIndex = -1; starts++; }
                    public bool MoveNext()
                    {
                        Event(10 + cursor);
                        if (cursor == length) return false;
                        currentIndex = cursor; cursor++; return true;
                    }
                    public Item Current
                    {
                        get { reads++; Event(100 + data[currentIndex]); return new Item(data[currentIndex], currentIndex); }
                    }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator() => new Iterator();
            }
            int Key(Item value) { selections++; Event(200 + value.Raw); return value.Raw % 3; }
            int Compare(immutable(int)* current, immutable(int)* best)
            {
                comparisons++; Event(300 + *current * 10 + *best);
                if (*current < *best) return -1;
                if (*current > *best) return 1;
                return 0;
            }
            int Reverse(immutable(int)* current, immutable(int)* best) => Compare(best, current);
            void Reset(int count)
            {
                cursor = 0; length = count; starts = 0; ends = 0;
                reads = 0; selections = 0; comparisons = 0; events = 0;
            }
            int main()
            {
                var query = new Enumerable<Item, Source, Source::Iterator>(new Source());
                int[20] minTrace = [10,108,208,11,104,204,312,12,107,207,311,13,101,201,311,14,105,205,321,15];
                int[20] maxTrace = [10,108,208,11,104,204,312,12,107,207,312,13,101,201,312,14,105,205,322,15];
                for (int operation = 0; operation < 6; operation++)
                {
                    Reset(5);
                    Item result = new(99, 99);
                    bool found = false;
                    if (operation == 0) found = query.TryMinBy(&Key, &Compare, ref result);
                    else if (operation == 1) found = query.TryMaxBy(&Key, &Compare, ref result);
                    else if (operation == 2) found = query.TryMinBy(&Key, ref result);
                    else if (operation == 3) found = query.TryMaxBy(&Key, ref result);
                    else if (operation == 4) found = query.TryMinBy(&Key, &Reverse, ref result);
                    else found = query.TryMaxBy(&Key, &Reverse, ref result);
                    int wanted = 1;
                    if (operation == 1 || operation == 3 || operation == 4) wanted = 0;
                    if (!found || result.Id != wanted || starts != 1 || ends != 1 || cursor != 5 || reads != 5 || selections != 5) return 1;
                    if (operation < 2)
                    {
                        if (comparisons != 4 || events != 20) return 2;
                        for (nuint i = 0; i < 20; i++)
                        {
                            int expected = minTrace[i];
                            if (operation == 1) expected = maxTrace[i];
                            if (trace[i] != expected) return 3;
                        }
                    }
                    // A second enumeration consumes the same exhausted source, without an implicit rewind.
                    events = 0;
                    if (query.TryMinBy(&Key, ref result) || result.Id != wanted || starts != 2 || ends != 2 || reads != 5 || selections != 5) return 4;
                }
                for (int which = 0; which < 2; which++)
                {
                    Reset(0);
                    Item result = new(99, 99);
                    bool found = false;
                    if (which == 0) found = query.TryMinBy(&Key, &Compare, ref result);
                    else found = query.TryMaxBy(&Key, &Compare, ref result);
                    if (found || result.Id != 99 || starts != 1 || ends != 1 || reads != 0 || selections != 0 || comparisons != 0 || events != 1) return 5;
                    Reset(1);
                    if (which == 0) found = query.TryMinBy(&Key, &Compare, ref result);
                    else found = query.TryMaxBy(&Key, &Compare, ref result);
                    if (!found || result.Id != 0 || reads != 1 || selections != 1 || comparisons != 0 || starts != ends) return 6;
                }
                if (attempts != 0) return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningStringResultsAreIndependentAndCachedMoveOnlyKeysAreReleased(int optimization)
        => Run(Allocator + """
            int keys = 0;
            struct Key
            {
                public int Length;
                private String token;
                public Key(int count) { Length = count; token = s"key"; keys++; }
                ~Key() { keys--; }
            }
            Key Length(String text) => new Key((int)text.Length);
            int Compare(immutable(Key)* current, immutable(Key)* best)
            {
                if (current.Length < best.Length) return -1;
                if (current.Length > best.Length) return 1;
                return 0;
            }
            int main()
            {
                {
                    String[3] storage = [s"aaaa", s"b", s"cc"];
                    Span<String> values = new(storage, 3);
                    for (int which = 0; which < 2; which++)
                    {
                        String result = String::From("saved");
                        bool found = false;
                        if (which == 0) found = values.AsEnumerable().TryMinBy(&Length, &Compare, ref result);
                        else found = values.AsEnumerable().TryMaxBy(&Length, &Compare, ref result);
                        nuint index = 1;
                        if (which == 1) index = 0;
                        if (!found || !result.Equals(&storage[index]) || result.Data == storage[index].Data || keys != 0) return 1;
                        result.Append("!", 1);
                        if (storage[0].Length != 4 || storage[1].Length != 1 || storage[2].Length != 2) return 2;
                        readonly(char)* saved = result.Data;
                        nuint count = result.Length;
                        Span<String> empty = new();
                        int before = attempts;
                        failAt = attempts + 1;
                        if (which == 0) found = empty.AsEnumerable().TryMinBy(&Length, &Compare, ref result);
                        else found = empty.AsEnumerable().TryMaxBy(&Length, &Compare, ref result);
                        failAt = 0;
                        if (found || result.Data != saved || result.Length != count || attempts != before || keys != 0) return 3;
                    }
                }
                if (keys != 0 || allocated != released) return 4;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackCopyCurrentAndFinalAdvanceFailuresPreserveOutputAndOriginalException(int optimization)
        => Run(Allocator + """
            int mode = 0; int owners = 0; int keys = 0;
            int selections = 0; int comparisons = 0; int starts = 0; int ends = 0;
            nuint failureAddress = 0;
            void Fail(int code)
            {
                try { throw new Exception("original failure", code); }
                catch (ref Exception error) { failureAddress = (&error).reinterpret<nuint>(); throw; }
            }
            struct Owner
            {
                public int Id;
                private String token;
                public Owner(int id) { Id = id; token = s"owner"; owners++; }
                public copy Owner(immutable(Owner)* source)
                {
                    Id = source.Id; token = source.token;
                    if (mode == 3 && Id == 2) Fail(73);
                    owners++;
                }
                public immutable readonly(char)* Text() => token.Data;
                ~Owner() { owners--; }
            }
            struct Key
            {
                public int Value;
                private String token;
                public Key(int value) { Value = value; token = s"key"; keys++; }
                ~Key() { keys--; }
            }
            struct Source
            {
                public struct Iterator
                {
                    private int position;
                    private String token;
                    public Iterator() { position = 0; token = s"iterator"; starts++; }
                    public bool MoveNext()
                    {
                        if (mode == 4 && position == 3) Fail(74);
                        if (position == 3) return false;
                        position++; return true;
                    }
                    public Owner Current
                    {
                        get { if (mode == 5 && position == 2) Fail(75); return new Owner(position); }
                    }
                    ~Iterator() { ends++; }
                }
                public Iterator GetEnumerator() => new Iterator();
            }
            Key Select(Owner value)
            {
                selections++;
                if (mode == 1 && selections == 2) Fail(71);
                return new Key(value.Id);
            }
            int Compare(immutable(Key)* current, immutable(Key)* best)
            {
                comparisons++;
                if (mode == 2 && comparisons == 2) Fail(72);
                return current.Value - best.Value;
            }
            int main()
            {
                var query = new Enumerable<Owner, Source, Source::Iterator>(new Source());
                for (int which = 0; which < 2; which++) for (int failure = 1; failure <= 5; failure++)
                {
                    {
                        Owner result = new(99);
                        readonly(char)* saved = result.Text();
                        int live = allocated - released;
                        selections = 0; comparisons = 0; starts = 0; ends = 0;
                        mode = failure; bool caught = false;
                        try
                        {
                            if (which == 0) query.TryMinBy(&Select, &Compare, ref result);
                            else query.TryMaxBy(&Select, &Compare, ref result);
                        }
                        catch (ref Exception error)
                        {
                            caught = true;
                            if (error.GetCode() != 70 + failure || (&error).reinterpret<nuint>() != failureAddress) return 1;
                            if (!CString::Equals(error.GetMessage(), "original failure")) return 2;
                        }
                        mode = 0;
                        if (!caught || result.Id != 99 || result.Text() != saved || !CString::Equals(result.Text(), "owner")) return 3;
                        if (owners != 1 || keys != 0 || starts != ends || allocated - released != live) return 4;
                    }
                    if (owners != 0 || keys != 0 || allocated != released) return 5;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryStringCopyAllocationFailureLeavesInitializedOutputUnchanged(int optimization)
        => Run(Allocator + """
            int Length(String value) => (int)value.Length;
            int main()
            {
                {
                    String[3] storage = [s"aaaa", s"b", s"cc"];
                    Span<String> values = new(storage, 3);
                    for (int which = 0; which < 2; which++)
                    {
                        int copies = 0;
                        {
                            String result = String::From("saved");
                            int before = attempts;
                            bool found = false;
                            if (which == 0) found = values.AsEnumerable().TryMinBy(&Length, ref result);
                            else found = values.AsEnumerable().TryMaxBy(&Length, ref result);
                            copies = attempts - before;
                            if (!found || copies == 0) return 1;
                        }
                        for (int failure = 1; failure <= copies; failure++)
                        {
                            String result = String::From("saved");
                            readonly(char)* saved = result.Data;
                            int live = allocated - released;
                            failAt = attempts + failure;
                            bool caught = false;
                            try
                            {
                                if (which == 0) values.AsEnumerable().TryMinBy(&Length, ref result);
                                else values.AsEnumerable().TryMaxBy(&Length, ref result);
                            }
                            catch (ref BadAllocException error) { caught = true; }
                            failAt = 0;
                            if (!caught || result.Data != saved || !CString::Equals(result.Data, "saved")) return 2;
                            if (allocated - released != live) return 3;
                        }
                    }
                }
                if (allocated != released) return 4;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("query-try-extrema-by.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        var result = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        Assert.Equal(0, result.ExitCode);
    }
}
