namespace gflat.Tests;

public sealed class ForeachGarbageCollectionTests
{
    private static void Succeeds(string source)
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("GC.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", source)
        ]);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void IteratorConstructionFailureRetiresRootsAfterCollectionCleanup() => Succeeds("""
        struct Iterator {
            private int^ value;
            public Iterator(int^ item) { value = item; GC::Collect(); throw new Exception(); }
            public bool MoveNext() => false;
            public int Current { get => *value; }
        }
        struct Source {
            private int^ value;
            public Source() { value = new^ int(42); }
            public Iterator GetEnumerator() { GC::Collect(); return new Iterator(value); }
            ~Source() { GC::Collect(); if (*value != 42) throw new Exception("Lost source root"); }
        }
        void Iterate() {
            try { foreach (int item in new Source()) {} }
            catch { if (GC::Collect() != (nuint)1) throw new Exception("Retained failed iterator"); }
        }
        int main() { Iterate(); if (GC::Collect() != (nuint)0) return 1; return 0; }
        """);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedCollectionAndIteratorSurviveCollectionAtEveryProtocolBoundary(bool extension) => Succeeds("""
        class Iterator {
            public int^ value;
            private bool done;
            public Iterator(int^ item) { value = item; done = false; GC::Collect(); }
            public bool MoveNext() { GC::Collect(); if (done) return false; done = true; return true; }
            public int Current { get { GC::Collect(); return *value; } }
        }
        class Source {
            public int^ value;
            public Source() { value = new^ int(42); }
            FACTORY
        }
        EXTENSION
        int Iterate() {
            int total = 0;
            foreach (int item in new^ Source()) { GC::Collect(); total += item; }
            return total;
        }
        int main() {
            if (Iterate() != 42) return 1;
            if (GC::Collect() != (nuint)3) return 2;
            return 0;
        }
        """.Replace("FACTORY", extension ? "" : "public Iterator^ GetEnumerator() { GC::Collect(); return new^ Iterator(value); }")
            .Replace("EXTENSION", extension ? "Iterator^ GetEnumerator(this Source^ source) { GC::Collect(); return new^ Iterator(source.value); }" : ""));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeOwningTemporaryKeepsManagedFieldsAliveAndUnwinds(bool getterThrows) => Succeeds("""
        struct Iterator {
            private int^ value;
            private bool done;
            public Iterator(int^ item) { value = item; done = false; GC::Collect(); }
            public bool MoveNext() { GC::Collect(); if (done) return false; done = true; return true; }
            public int Current { get { GC::Collect(); GETTER } }
            ~Iterator() { GC::Collect(); if (*value != 42) throw new Exception("Lost iterator root"); }
        }
        struct Source {
            private int^ value;
            public Source() { value = new^ int(42); }
            public Iterator GetEnumerator() { GC::Collect(); return new Iterator(value); }
            ~Source() { GC::Collect(); if (*value != 42) throw new Exception("Lost collection root"); }
        }
        int Iterate() {
            try {
                foreach (int item in new Source()) { GC::Collect(); if (item != 42) return 1; break; }
            } catch { CAUGHT }
            return 0;
        }
        int main() {
            if (Iterate() != 0) return 1;
            if (GC::Collect() != (nuint)EXPECTED) return 2;
            return 0;
        }
        """.Replace("GETTER", getterThrows ? "throw new Exception();" : "return *value;")
            .Replace("CAUGHT", getterThrows ? "if (GC::Collect() != (nuint)1) return 4;" : "return 3;")
            .Replace("EXPECTED", getterThrows ? "0" : "1"));
}
