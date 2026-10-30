namespace gflat.Tests.Std.Text;

public sealed class SpanEmptyObserverMetadataTests
{
    private const string Preamble = """
        using Standard::Memory; using Standard::Text;
        int allocations = 0;
        namespace Memory {
            public replace void*? Allocate(nuint count) { allocations++; return null; }
            public replace void Free(void* storage) nothrow {}
        }
        """;

    [Theory]
    [InlineData("readonly", 0)]
    [InlineData("readonly", 2)]
    [InlineData("immutable", 0)]
    [InlineData("immutable", 2)]
    public void EmptyPatternObserversPreserveProtectedDescriptorMetadata(string protection, int optimization)
        => Run(Preamble + $$"""
            int main() {
                char anchor = '*';
                {{protection}}(Span<readonly(char)>) text = new(&anchor, 0);
                // Copying this descriptor is safe: its pointer already protects its char leaf.
                Span<readonly(char)> copied = text;
                if (copied.Length != 0 || copied.Data != &anchor) return 1;
                Span<readonly(char)> before = new(); Span<readonly(char)> after = new();
                readonly(char)* sentinel = ((nuint)1).reinterpret<readonly(char)*>();
                Span<readonly(char)> separator = new(sentinel, 0);
                if (!text.TrySplitLastOnce(sentinel, 0, ref before, ref after) || before.Data != &anchor || after.Data != null) return 2;
                if (!text.TrySplitLastOnce(separator, ref before, ref after) || before.Data != &anchor || after.Data != null) return 3;
                if (!text.TrySplitLastOnce("", ref before, ref after) || before.Data != &anchor || after.Data != null) return 4;
                if (!text.TrySplitOnce(sentinel, 0, ref before, ref after) || before.Data != null || after.Data != &anchor) return 5;
                if (!text.TrySplitOnce(separator, ref before, ref after) || before.Data != null || after.Data != &anchor) return 6;
                {{protection}}(Span<readonly(char)>) fake = new(sentinel, 0);
                if (!fake.TrySplitLastOnce(separator, ref before, ref after) || before.Data != sentinel || after.Data != null) return 7;
                if (!fake.TrySplitOnce(separator, ref before, ref after) || before.Data != null || after.Data != sentinel) return 8;
                bool flag = true;
                int number = 81;
                if (fake.TryParseBool(&flag) || flag || fake.TryParseInt(&number) || number != 0) return 9;
                return allocations;
            }
            """, optimization);

    [Theory]
    [InlineData("readonly", 0)]
    [InlineData("readonly", 2)]
    [InlineData("immutable", 0)]
    [InlineData("immutable", 2)]
    public void RefOutputAliasesCannotOverwriteTheSnapshotBeforeItIsCommitted(string protection, int optimization)
        => Run(Preamble + $$"""
            int main() {
                char anchor = '*';
                Span<readonly(char)> source = new(&anchor, 0);
                {{protection}}(Span<readonly(char)>)* view = &source;
                Span<readonly(char)> before = new(); Span<readonly(char)> after = new();
                Span<readonly(char)> separator = new(((nuint)1).reinterpret<readonly(char)*>(), 0);
                if (!view.TrySplitLastOnce(separator, ref source, ref after) || source.Data != &anchor || after.Data != null) return 10;
                source = new Span<readonly(char)>(&anchor, 0);
                if (!view.TrySplitLastOnce(separator, ref before, ref source) || before.Data != &anchor || source.Data != null) return 11;
                source = new Span<readonly(char)>(&anchor, 0);
                if (!view.TrySplitOnce(separator, ref source, ref after) || source.Data != null || after.Data != &anchor) return 12;
                source = new Span<readonly(char)>(&anchor, 0);
                if (!view.TrySplitOnce(separator, ref before, ref source) || before.Data != null || source.Data != &anchor) return 13;
                return allocations;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        string[] files = ["core/Memory/Span.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf",
            "core/Text/CString.gf", "core/Text/ByteSearch.gf", "core/Text/text_split_once_pattern_span.gf",
            "core/Text/text_split_last_once_pattern_span.gf", "core/Text/boolean_parsing.gf", "core/Text/integer_parsing.gf", "core/Text/parsing.gf",
            "core/Text/span_boolean_parsing.gf", "core/Text/span_integer_parsing.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", file))))
            .Append(new SourceFile("empty-observer-metadata.gf", code));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
