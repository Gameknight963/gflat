using gflat.CompileExceptions;

namespace gflat.Tests.Std.Memory;

public sealed class StandardSpanBorrowedReadonlyTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).Append(new SourceFile("readonly-span.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReadonlyDescriptorsCanReadTextCopyAndFillMutablePointees(int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources("""
            using Standard::Memory; using Standard::Text; using Standard::Queries;
            int main() {
                char[4] bytes = ['a', 'b', 'c', 'd'];
                readonly(Span<char>) view = new(bytes, 4);
                view.Fill('x');
                if (view.Length != 4 || bytes[0] != 'x' || bytes[3] != 'x') return 1;
                if (view.Slice(1, 2).Length != 2) return 2;
                readonly(Span<readonly(char)>) text = view.AsReadOnly();
                if (!text.StartsWith("xx") || !text.EndsWith("xx") || text.Trim().Length != 4) return 3;
                Span<readonly(char)> before = new(); Span<readonly(char)> after = new();
                if (!text.TrySplitOnce('x', ref before, ref after) || before.Length != 0 || after.Length != 3) return 4;
                char[4] copied;
                Span<char> destination = new(copied, 4);
                readonly(Span<immutable(char)>) source = view.AsImmutable();
                source.CopyTo(destination);
                if (!source.TryCopyTo(destination) || copied[0] != 'x' || copied[3] != 'x') return 5;
                Span<char>::Enumerator iterator = view.GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current != 'x' || !view.AsEnumerable().Any()) return 6;
                return 0;
            }
            """), optimization).ExitCode);

    [Theory]
    [InlineData("immutable(Span<char>) view = new(bytes, 1); view.Fill('x');")]
    [InlineData("Span<immutable(char)> view = new(bytes, 1); view.Fill('x');")]
    [InlineData("immutable(Span<char>) view = new(bytes, 1); view.Trim();")]
    public void DeeplyProtectedDescriptorsOrElementsCannotExposeMutableOperations(string operation)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using Standard::Memory;
            void Test() { char[1] bytes = ['a'];
            """ + operation + " }")));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImmutableTextDescriptorsCanUseParsingAndProtectedViewObservers(int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources("""
            using Standard::Memory; using Standard::Text;
            int main() {
                immutable(Span<readonly(char)>) truth = new("true", 4);
                bool parsedBool = false;
                if (!truth.TryParseBool(&parsedBool) || !parsedBool || !truth.ParseBool()) return 1;
                immutable(Span<readonly(char)>) number = new("42", 2);
                int parsedInt = 0;
                if (!number.TryParseInt(&parsedInt) || parsedInt != 42 || number.ParseInt() != 42) return 2;
                immutable(Span<readonly(char)>) text = new("  42  ", 6);
                if (!text.StartsWith("  ") || !text.EndsWith("  ") || !text.MatchesWildcard("*42*")) return 3;
                Span<readonly(char)> trimmed = text.Trim();
                if (trimmed.Length != 2 || trimmed.ParseInt() != 42) return 4;
                Span<readonly(char)> before = new(); Span<readonly(char)> after = new();
                if (!text.TrySplitOnce('4', ref before, ref after) || before.Length != 2 || after.Length != 3) return 5;
                TextLines lines = text.Lines();
                TextParts parts = text.Split(' ', true);
                if (!lines.GetEnumerator().MoveNext() || !parts.GetEnumerator().MoveNext()) return 6;
                return 0;
            }
            """), optimization).ExitCode);
}
