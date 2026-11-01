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
            const bool deep = reflect(immutable(Box)).IsImmutable;
            const bool shallow = reflect(readonly(Box)).IsImmutable;
            int main() {
                if (!deep || shallow) return 1;
                immutable(TypeInfo)* first = Describe<readonly(Box)*>();
                immutable(TypeInfo)* second = Describe<immutable(Box)*>();
                if (first.IsReadOnly || first.IsImmutable || second.IsReadOnly || second.IsImmutable) return 2;
                if (!first.ElementType!.IsReadOnly || first.ElementType!.IsImmutable) return 3;
                if (!second.ElementType!.IsReadOnly || !second.ElementType!.IsImmutable) return 4;
                immutable(TypeInfo)* fixed = Describe<readonly(int*)>();
                if (!fixed.IsReadOnly || fixed.IsImmutable || fixed.ElementType!.IsReadOnly) return 5;
                immutable(TypeInfo)* graph = Describe<immutable(int*)>();
                if (!graph.IsReadOnly || !graph.IsImmutable || !graph.ElementType!.IsReadOnly) return 6;
                if (graph.ElementType!.IsImmutable) return 7;
                return 0;
            }
            """;
        var sources = new[] { "core/TypeInfo.gf", "core/FieldInfo.gf", "core/type_descriptions.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("qualifier-reflection.gf", source));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
