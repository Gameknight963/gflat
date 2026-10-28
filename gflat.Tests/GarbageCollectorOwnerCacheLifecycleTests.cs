namespace gflat.Tests;

public sealed class GarbageCollectorOwnerCacheLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TypedManagedFieldsReclassifyAcrossAdoptionAndRetiredOwnerSlotReuse(int optimization)
    {
        const string program = """
            using std;
            public static replace nuint std::TracingCollector::CollectionThreshold() => 0;
            struct Holder { public int^? item; }

            int AdoptNativeField(int value) {
                Holder* raw = new* Holder();
                raw.item = new^ int(value);
                // The registered field is outside every managed allocation here.
                if (GC::Collect() != 0 || TracingCollector::ManagedCount != 1) return 1;
                if (GC::Collect() != 0 || *raw.item! != value) return 2;
                {
                    Holder^ owner = TracingCollector::Adopt(raw);
                    if (GC::Collect() != 0 || TracingCollector::ManagedCount != 2) return 3;
                    if (GC::Collect() != 0 || *owner.item! != value) return 4;
                }
                // The former native root is now only an edge inside dead owner storage.
                if (GC::Collect() != 2 || TracingCollector::ManagedCount != 0) return 5;
                if (GC::Collect() != 0 || TracingCollector::ManagedCount != 0) return 6;
                return 0;
            }

            void MakeDeadOwner(int value) {
                Holder^ owner = new^ Holder();
                owner.item = new^ int(value);
                if (GC::Collect() != 0 || *owner.item! != value)
                    throw new Exception("Lost live managed owner field");
            }
            int ReuseRetiredOwnerSlots(int value) {
                MakeDeadOwner(value);
                if (GC::Collect() != 2 || TracingCollector::ManagedCount != 0) return 7;
                // Sweeping cached the old managed field's Slot node. Compiler-created
                // roots for fresh native storage can reuse that node with a new address.
                Holder* raw = new* Holder();
                raw.item = new^ int(value + 1);
                if (GC::Collect() != 0 || TracingCollector::ManagedCount != 1) return 8;
                if (GC::Collect() != 0 || *raw.item! != value + 1) return 9;
                delete raw;
                if (GC::Collect() != 1 || TracingCollector::ManagedCount != 0) return 10;
                if (GC::Collect() != 0 || TracingCollector::ManagedCount != 0) return 11;
                return 0;
            }
            int main() {
                for (int round = 0; round < 16; round++) {
                    int result = AdoptNativeField(round);
                    if (result != 0) return result;
                    result = ReuseRetiredOwnerSlots(round);
                    if (result != 0) return result;
                }
                return 0;
            }
            """;
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("collector-owner-cache-lifecycle.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"O{optimization}, exit {result.ExitCode}: {result.StandardError}");
    }
}
