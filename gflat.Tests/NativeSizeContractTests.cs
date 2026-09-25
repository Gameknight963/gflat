using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class NativeSizeContractTests
{
    [Fact]
    public void AllocatorAndMallocHaveNativeSizedParameters()
        => Compiler.Check("void*?(nuint)* AllocatorAddress() => &Allocator::Allocate; void*?(nuint)* MallocAddress() => &malloc;");

    [Theory]
    [InlineData("Allocator::Allocate")]
    [InlineData("malloc")]
    public void AllocationSignaturesAreDistinctFromUnsignedLong(string name)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($"void*?(ulong)* Address() => &{name};"));

    [Theory]
    [InlineData("namespace Allocator { public replace void*? Allocate(ulong n) => null; public replace void Free(void* p) {} }")]
    [InlineData("void*? __gflat_gc_alloc(ulong n) => null;")]
    [InlineData("struct S { public static S operator s\"\"(readonly(char)* p, ulong n) => new S(); }")]
    public void PreviousUnsignedLongContractsAreRejected(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));

    [Fact]
    public void SynthesizedLiteralLengthHasNativeUnsignedType()
    {
        var (ast, checker) = Compiler.Check("""
            alias Size = nuint;
            struct S { public static S operator s""(readonly(char)* p, Size n) => new S(); }
            S Make() => s"abc";
            """);
        var method = ast.Members.OfType<MethodDeclaration>().Single(m => m.Name == "Make");
        var returned = Assert.IsType<ReturnStatement>(method.Body!.Statements.Single());
        var literal = Assert.IsAssignableFrom<CallExpression>(returned.Value);
        Assert.Equal("nuint", Assert.IsType<NamedTypeExpression>(checker.GetType(literal.Arguments[1])).Name);
    }
}
