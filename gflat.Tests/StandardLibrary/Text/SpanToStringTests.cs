namespace gflat.Tests.Std.Text;

public sealed class SpanToStringTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(
        StandardTestSources.Values, StandardTestSources.Load("core/Text/span_to_string.gf"))
        .Append(new SourceFile("span-to-string.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesOwnCountedBytesAndOutliveTheirSource(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Memory;
            using Standard::Text;

            String Snapshot()
            {
                char[4] bytes = ['a', '\0', 'b', (char)255];
                Span<readonly(char)> view = new(bytes, 4);
                return view.ToString();
            }
            int main()
            {
                String snapshot = Snapshot();
                if (snapshot.Length != 4 || snapshot[0] != 'a' || snapshot[1] != '\0' ||
                    snapshot[2] != 'b' || snapshot[3] != (char)255 || snapshot.Data[4] != '\0') return 1;
                char[3] bytes = ['x', '\0', 'y'];
                Span<char> view = new(bytes, 3);
                String copy = view.ToString();
                bytes[0] = 'z';
                copy[2] = 'q';
                if (copy.Length != 3 || copy[0] != 'x' || copy[1] != '\0' || bytes[2] != 'y') return 2;
                immutable(Span<char>) protectedView = view;
                String protectedCopy = protectedView.ToString();
                if (protectedCopy[0] != 'z' || protectedCopy[2] != 'y') return 3;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyViewsDoNotReadStorageOrAllocate(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Memory;
            using Standard::Text;

            namespace Memory
            {
                public replace void*? Allocate(nuint size) => null;
            }
            int main()
            {
                Span<char> empty = new();
                Span<readonly(char)> nullBuffer = new(null, 0);
                Span<readonly(char)> unreadable = new(((nuint)1).reinterpret<readonly(char)*>(), 0);
                String first = empty.ToString();
                String second = nullBuffer.ToString();
                String third = unreadable.ToString();
                if (!first.IsEmpty || !second.IsEmpty || !third.IsEmpty) return 1;
                if (first.Data[0] != '\0' || second.Data[0] != '\0' || third.Data[0] != '\0') return 2;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
