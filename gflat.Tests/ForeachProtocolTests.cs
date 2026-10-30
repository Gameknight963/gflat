using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ForeachProtocolTests
{
    private const string Iterator = """
        struct Iterator {
            private int n;
            public Iterator() { n = -1; }
            public bool MoveNext() { n++; return n < 3; }
            public int Current { immutable get => n; }
        }
        """;

    [Theory]
    [InlineData("struct Source { public Iterator GetEnumerator() => new Iterator(); }", "Source source = new Source();", "source")]
    [InlineData("struct Source {} Iterator GetEnumerator(ref Source source) => new Iterator();\nextension Source { public Iterator GetEnumerator() => new Iterator(); }", "Source source = new Source();", "source")]
    [InlineData("struct Source {} Iterator GetEnumerator(ref Source source) => new Iterator();\nextension Source { public Iterator GetEnumerator() => new Iterator(); }", "Source source = new Source(); Source* p = &source;", "p")]
    [InlineData("struct Source {} Iterator GetEnumerator(ref immutable(Source) source) => new Iterator();\nextension Source { public immutable Iterator GetEnumerator() => new Iterator(); }", "immutable(Source) source = new Source();", "source")]
    [InlineData("struct Source {} Iterator GetEnumerator(ref Source source) => new Iterator();\nextension Source { public Iterator GetEnumerator() => new Iterator(); }", "", "new Source()")]
    public void CurrentPropertiesWorkWithOrdinaryAndExtensionFactories(string source, string locals, string collection)
    {
        var result = CompilerTestHelper.Run(Iterator + source + " int main() { " + locals + " int sum = 39; foreach (int item in " + collection + ") { sum += item; } return sum; }");
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("public ref int Current { get => n; }")]
    [InlineData("public ref readonly(int) Current { get => n; }")]
    [InlineData("public immutable int Current() => n;")]
    public void CurrentMethodsAndReferenceGettersKeepValueIteration(string current)
    {
        string iterator = "struct Iterator { private int n; public Iterator() { n = -1; } public bool MoveNext() { n++; return n < 3; } " + current + " }";
        var result = CompilerTestHelper.Run(iterator + " struct Source { public Iterator GetEnumerator() => new Iterator(); } int main() { Source source = new Source(); int sum = 39; foreach (int item in source) { sum += item; item = 100; } return sum; }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CurrentFieldRemainsSupported()
    {
        var result = CompilerTestHelper.Run("struct Iterator { public int Current; public Iterator() { Current = -1; } public bool MoveNext() { Current++; return Current < 3; } } struct Source { public Iterator GetEnumerator() => new Iterator(); } int main() { Source source = new Source(); int sum = 39; foreach (int item in source) { sum += item; } return sum; }");
        Assert.Equal(42, result.ExitCode);
    }

    private const string OwningSource = """
        extern int printf(immutable(char*) message, ...);
        struct Iterator {
            private int n;
            public Iterator() { n = -1; printf("E"); }
            public bool MoveNext() { n++; return n < 3; }
            public int Current { immutable get => n; }
            ~Iterator() { printf("I"); }
        }
        struct Source {
            public Source() { printf("S"); }
            public Iterator GetEnumerator() { printf("G"); return new Iterator(); }
            ~Source() { printf("C"); }
        }
        Source Make() => new Source();
        """;

    [Theory]
    [InlineData("foreach (int item in Make()) { break; }", "SGEIC")]
    [InlineData("foreach (int item in Make()) {}", "SGEIC")]
    [InlineData("Source owner = Make(); foreach (int item in move owner) { break; }", "SGEIC")]
    [InlineData("try { foreach (int item in Make()) { throw new Exception(); } } catch {}", "SGEIC")]
    public void CollectionEvaluatesOnceAndCleanupKeepsTheSourceAlive(string body, string output)
    {
        var result = CompilerTestHelper.Run(OwningSource + " int main() { " + body + " return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(output, result.StandardOutput);
    }

    [Fact]
    public void ThrowingCurrentGetterDestroysIteratorThenCollection()
    {
        var source = OwningSource.Replace("public int Current { immutable get => n; }", "public int Current { immutable get { throw new Exception(); } }");
        var result = CompilerTestHelper.Run(source + " int main() { try { foreach (int item in Make()) {} } catch {} return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("SGEIC", result.StandardOutput);
    }

    [Fact]
    public void ThrowingFactoryDestroysOnlyTheConstructedCollection()
    {
        var source = OwningSource.Replace("public Iterator GetEnumerator() { printf(\"G\"); return new Iterator(); }", "public Iterator GetEnumerator() { printf(\"G\"); throw new Exception(); }");
        var result = CompilerTestHelper.Run(source + " int main() { try { foreach (int item in Make()) {} } catch {} return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("SGC", result.StandardOutput);
    }

    [Theory]
    [InlineData("struct Iterator { public int Current; } struct Source { public Iterator GetEnumerator() => new Iterator(); }", "MoveNext")]
    [InlineData("struct Iterator { public int MoveNext() => 1; public int Current; } struct Source { public Iterator GetEnumerator() => new Iterator(); }", "bool")]
    [InlineData("struct Iterator { public bool MoveNext() => false; } struct Source { public Iterator GetEnumerator() => new Iterator(); }", "Current")]
    [InlineData("struct Iterator { public bool MoveNext() => false; private int Current { get => 0; } } struct Source { public Iterator GetEnumerator() => new Iterator(); }", "private")]
    public void InvalidProtocolsHaveNormalSourceDiagnostics(string declarations, string expected)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(declarations + " int main() { Source source = new Source(); foreach (int item in source) {} return 0; }"));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
