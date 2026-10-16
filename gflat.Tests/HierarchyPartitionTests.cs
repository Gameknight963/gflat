using gflat.CompileExceptions;
using gflat.semantics;
using gflat.symbols;

namespace gflat.Tests;

public sealed class HierarchyPartitionTests
{
    [Fact]
    public void SingleClassPartitionIsRepeatableAndLeavesOtherClassesUntouched()
    {
        var symbols = new SymbolTable();
        symbols.Classes["Base"] = new() { Name = "Base" };
        var child = new TypeChecker.ClassInfo { Name = "Child", Interfaces = ["Base", "I"] };
        var other = new TypeChecker.ClassInfo { Name = "Other", Interfaces = ["Base"] };
        symbols.Classes["Child"] = child;
        symbols.Classes["Other"] = other;
        var pass = new HierarchyResolutionPass(symbols);
        pass.ResolveBaseTypes(child);
        pass.ResolveBaseTypes(child);
        Assert.Equal("Base", child.BaseClass);
        Assert.Equal(["I"], child.Interfaces);
        Assert.Null(other.BaseClass);
        Assert.Equal(["Base"], other.Interfaces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPartitionPreservesDeclaredEdgesForRetries(bool staticBase)
    {
        var symbols = new SymbolTable();
        symbols.Classes["First"] = new() { Name = "First", IsStatic = staticBase };
        symbols.Classes["Second"] = new() { Name = "Second" };
        var child = new TypeChecker.ClassInfo { Name = "Child", Interfaces = staticBase ? ["First"] : ["First", "Second"] };
        var declared = child.Interfaces.ToArray();
        string? firstError = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var error = Assert.Throws<TypeCheckException>(() => new HierarchyResolutionPass(symbols).ResolveBaseTypes(child));
            firstError ??= error.Description;
            Assert.Equal(firstError, error.Description);
            Assert.Null(child.BaseClass);
            Assert.Equal(declared, child.Interfaces);
        }
    }
}
