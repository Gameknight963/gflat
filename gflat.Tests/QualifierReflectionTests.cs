namespace gflat.Tests;

public sealed class QualifierReflectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RecursiveMetadataDistinguishesShallowStorageAndDeepViews(int optimization)
    {
        const string source = """
            using Standard;
            struct Box { public int* data; }
            const bool deep = typeof(immutable(Box)).IsImmutable;
            const bool shallow = typeof(readonly(Box)).IsImmutable;
            int main() {
                if (!deep || shallow) return 1;
                TypeInfo first = typeof(readonly(Box)*);
                TypeInfo second = typeof(immutable(Box)*);
                if (first.IsReadOnly || first.IsImmutable || second.IsReadOnly || second.IsImmutable) return 2;
                if (!first.ElementType!.IsReadOnly || first.ElementType!.IsImmutable) return 3;
                if (!second.ElementType!.IsReadOnly || !second.ElementType!.IsImmutable) return 4;
                TypeInfo fixed = typeof(readonly(int*));
                if (!fixed.IsReadOnly || fixed.IsImmutable || fixed.ElementType!.IsReadOnly) return 5;
                TypeInfo graph = typeof(immutable(int*));
                if (!graph.IsReadOnly || !graph.IsImmutable || !graph.ElementType!.IsReadOnly) return 6;
                if (graph.ElementType!.IsImmutable) return 7;
                return 0;
            }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(source, optimization).ExitCode);
    }
}
