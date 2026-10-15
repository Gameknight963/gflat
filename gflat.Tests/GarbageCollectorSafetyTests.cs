namespace gflat.Tests;

public sealed class GarbageCollectorSafetyTests
{
    private static void Succeeds(string source, int optimization, bool vector = false)
    {
        string[] files = vector ? ["libc/GC.gf", "libc/TracingCollector.gf", "core/Vector.gf", "core/Span.gf"] : ["libc/GC.gf", "libc/TracingCollector.gf"];
        string ir = Compiler.Emit(files.Select(name => new SourceFile("std/" + name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", source)));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-tracing-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "main.ll");
            string output = Path.Combine(directory, OperatingSystem.IsWindows() ? "main.exe" : "main");
            File.WriteAllText(input, ir);
            ProcessResult compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, $"O{optimization}: {compiled.StandardError}");
            ProcessResult result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"O{optimization}, exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RepeatedCaughtExceptionPayloadsRetireTheirManagedFieldSlots(int optimization) => Succeeds("""
        class ManagedError : Exception {
            public int^ payload;
            public ManagedError(int^ value) : base("payload") { payload = value; }
        }
        void Fail(int value) { throw new ManagedError(new^ int(value)); }
        void CatchOne(int value) {
            try { Fail(value); }
            catch (ManagedError* error) {
                GC::Collect();
                if (*error.payload != value) throw new Exception("Lost exception field");
            }
        }
        int main() {
            for (int i = 0; i < 32; i++) {
                CatchOne(i);
                if (GC::Collect() != (nuint)1) return 1;
                if (std::TracingCollector::ManagedCount != (nuint)0) return 2;
            }
            return 0;
        }
        """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationAndCollectionInsideConstructorsKeepPartiallyBuiltGraphsAlive(int optimization) => Succeeds("""
        public static replace nuint std::TracingCollector::CollectionThreshold() => 1;
        class Pair {
            public int^ first;
            public int^ second;
            public Pair() {
                first = new^ int(20);
                GC::Collect();
                second = new^ int(22);
                GC::Collect();
            }
        }
        void Check() {
            Pair^ pair = new^ Pair();
            GC::Collect();
            if (*pair.first + *pair.second != 42) throw new Exception("Lost constructor field");
        }
        int main() {
            Check();
            if (GC::Collect() != (nuint)3) return 1;
            return 0;
        }
        """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedConstructorRetiresItsChildrenWithoutAllocatorInterception(int optimization) => Succeeds("""
        struct Broken {
            public int^ child;
            public Broken() {
                child = new^ int(42);
                GC::Collect();
                throw new Exception("constructor");
            }
        }
        void Fail() {
            try { Broken^ value = new^ Broken(); }
            catch (Exception* error) { }
        }
        int main() {
            Fail();
            if (GC::Collect() != (nuint)2) return 1;
            if (std::TracingCollector::ManagedCount != (nuint)0) return 2;
            return 0;
        }
        """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeVectorRetiresMovedAndClearedCycleRoots(int optimization) => Succeeds("""
        using std;
        class Node { public Node^? next; public int value; public Node(int n) { value = n; } }
        void Fill(Vector<Node^>* roots) {
            for (int i = 0; i < 24; i++) {
                Node^ node = new^ Node(i);
                node.next = node;
                roots.Add(node);
                GC::Collect();
            }
        }
        int main() {
            Vector<Node^> roots = new Vector<Node^>();
            Fill(&roots);
            for (nuint i = 0; i < roots.Count; i++) {
                if (roots[i].next!.value != (int)i) return 1;
            }
            roots.Clear();
            if (GC::Collect() != (nuint)24) return 2;
            return 0;
        }
        """, optimization, vector: true);
}
