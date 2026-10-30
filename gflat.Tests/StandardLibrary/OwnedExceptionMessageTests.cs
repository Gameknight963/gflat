namespace gflat.Tests.Std;

public sealed class OwnedExceptionMessageTests
{
    private const string Prelude = """
        using std;
        using std::Text;

        private extern void*? malloc(nuint bytes);
        private extern void free(void* storage);
        int allocations = 0;
        int releases = 0;
        int sourceScopesDestroyed = 0;
        nuint watchedSource = 0;
        nuint watchedMessage = 0;
        int sourceReleases = 0;
        int messageReleases = 0;

        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                void*? storage = malloc(bytes);
                if (storage != null) allocations++;
                return storage;
            }

            public replace void Free(void* storage) nothrow
            {
                if ((nuint)storage == watchedSource) sourceReleases++;
                if ((nuint)storage == watchedMessage) messageReleases++;
                releases++;
                free(storage);
            }
        }

        struct SourceScope
        {
            ~SourceScope() { sourceScopesDestroyed++; }
        }

        class MessageError : Exception
        {
            private String text;

            public MessageError(readonly(char)* message)
            {
                text = String::From(message);
            }

            public immutable override readonly(char)* GetMessage() => text.Data;
            public void Replace(readonly(char)* message) { text = String::From(message); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshThrowOwnsTextAfterSourceUnwindsAndBareRethrowPreservesObject(int optimization)
        => Run(Prelude + """
            void Fail()
            {
                SourceScope scope = new();
                char[6] bytes = ['o', 'w', 'n', 'e', 'd', '\0'];
                String local = String::From(bytes);
                watchedSource = (local.Data).reinterpret<nuint>();
                bytes[0] = 'X';
                throw new MessageError(local.Data);
            }

            int main()
            {
                nuint firstObject = 0;
                readonly(char)*? firstMessage = null;
                int firstAllocations = 0;
                int firstReleases = 0;
                bool caught = false;
                try
                {
                    try { Fail(); }
                    catch (ref Exception error)
                    {
                        if (sourceScopesDestroyed != 1 || sourceReleases != 1) return 1;
                        if (!CString::Equals(error.GetMessage(), "owned")) return 2;
                        if (!CString::Equals(error.message, "")) return 3;
                        firstObject = (&error).reinterpret<nuint>();
                        firstMessage = error.GetMessage();
                        watchedMessage = (nuint)error.GetMessage();
                        if (watchedMessage == watchedSource || messageReleases != 0) return 8;
                        firstAllocations = allocations;
                        firstReleases = releases;
                        throw;
                    }
                }
                catch (ref MessageError error)
                {
                    caught = true;
                    if ((&error).reinterpret<nuint>() != firstObject || error.GetMessage() != firstMessage) return 4;
                    if (!CString::Equals(error.GetMessage(), "owned")) return 5;
                    if (messageReleases != 0) return 9;
                    if (allocations != firstAllocations || releases != firstReleases) return 6;
                }
                if (!caught || messageReleases != 1 || allocations == 0 || allocations != releases) return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamedDerivedCopiesAndThrowCopiesOwnIndependentTextAndBalanceCleanup(int optimization)
        => Run(Prelude + """
            bool badCopy = false;

            MessageError CopyAfterSourceDies()
            {
                SourceScope scope = new();
                MessageError source = new("original");
                MessageError copy = source;
                if (copy.GetMessage() == source.GetMessage()) badCopy = true;
                source.Replace("changed source");
                if (!CString::Equals(copy.GetMessage(), "original")) badCopy = true;
                watchedSource = (nuint)source.GetMessage();
                return copy;
            }

            int main()
            {
                bool caught = false;
                {
                    MessageError named = CopyAfterSourceDies();
                    if (badCopy || sourceScopesDestroyed != 1) return 1;
                    if (sourceReleases != 1) return 2;
                    if (!CString::Equals(named.GetMessage(), "original")) return 3;
                    try { throw named; }
                    catch (ref Exception error)
                    {
                        caught = true;
                        if (error.GetMessage() == named.GetMessage()) return 4;
                        watchedMessage = (nuint)error.GetMessage();
                        if (messageReleases != 0) return 5;
                        named.Replace("changed named");
                        if (!CString::Equals(error.GetMessage(), "original")) return 6;
                        if (!CString::Equals(named.GetMessage(), "changed named")) return 7;
                        if (!CString::Equals(error.message, "")) return 8;
                        if (messageReleases != 0) return 11;
                    }
                    if (messageReleases != 1) return 9;
                }
                if (!caught || allocations == 0 || allocations != releases) return 10;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("owned-exception-message.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0,
            $"Owned exception message exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
