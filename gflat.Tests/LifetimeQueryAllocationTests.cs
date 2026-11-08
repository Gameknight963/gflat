using gflat.ast;

namespace gflat.Tests;

public sealed class LifetimeQueryAllocationTests
{
    [Fact]
    public void ScalarAndPointerLifetimeQueriesDoNotAllocateTraversalState()
    {
        var checker = Compiler.Check("int main() => 0;").Checker;
        var scalar = new NamedTypeExpression("int", null, 0);
        TypeExpression[] types = [scalar, new PointerTypeExpression(scalar, false, 0)];
        bool unexpected = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            foreach (var type in types)
                unexpected |= checker.HasDestructor(type) || checker.RequiresRelocation(type);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(unexpected);
        Assert.True(allocated < 256 * 1024, $"Lifetime queries allocated {allocated:N0} bytes");
    }
}
