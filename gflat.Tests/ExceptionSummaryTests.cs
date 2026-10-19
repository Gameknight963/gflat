using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public class ExceptionSummaryTests
{
    private static IReadOnlyList<string> Summary(string code, string name = "Test")
    {
        var snapshot = new AnalysisSnapshot([new SourceFile(Path.GetFullPath("exceptions.gf"), code)]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var method = snapshot.Nodes.Select(n => n.Node).OfType<MethodDeclaration>().Single(m => m.Name == name);
        return snapshot.Checker!.GetPossibleExceptions(method);
    }

    private const string Types = "class First : Exception {} class Second : Exception {} ";

    [Fact]
    public void GenericBodyDoesNotInheritCallersCatchScope()
        => Assert.Equal(new[] { "First" }, Summary(Types + "void Fail<T>() { throw new First(); } void Caught() { try { Fail<int>(); } catch {} } void Test() { Fail<int>(); }"));

    [Fact]
    public void TerminatingMethodHasNoOutgoingExceptions()
        => Assert.Empty(Summary("void Test() nothrow { throw new Exception(); }"));

    [Fact]
    public void ForeignNothrowContractSuppressesUnknownOutgoingExceptions()
        => Assert.Empty(Summary("extern int abs(int value) nothrow; void Test(){abs(-42);}"));

    [Fact]
    public void CollectsDistinctDirectThrows()
        => Assert.Equal(new[] { "First", "Second" }, Summary(Types + "void Test(bool b) { if (b) throw new First(); throw new Second(); }"));

    [Fact]
    public void PropagatesThroughForwardCallsAndRecursion()
        => Assert.Equal(new[] { "First" }, Summary(Types + "void Test(bool b) { Other(b); } void Other(bool b) { if (b) Test(false); throw new First(); }"));

    [Fact]
    public void RemovesCaughtExceptionsFromCalls()
        => Assert.Equal(new[] { "Second" }, Summary(Types + "void Fail(bool b) { if (b) throw new First(); throw new Second(); } void Test() { try { Fail(true); } catch (First* e) {} }"));

    [Fact]
    public void RemovesCaughtDirectThrowsAndIncludesHandlerThrows()
        => Assert.Equal(new[] { "Second" }, Summary(Types + "void Test() { try { throw new First(); } catch (Exception* e) { throw new Second(); } }"));

    [Fact]
    public void CatchAllStopsPropagation()
        => Assert.Empty(Summary(Types + "void Fail() { throw new First(); } void Test() { try { Fail(); } catch {} }"));

    [Fact]
    public void UsesSelectedOverload()
        => Assert.Equal(new[] { "Second" }, Summary(Types + "void Fail(int x) { throw new First(); } void Fail(bool x) { throw new Second(); } void Test() { Fail(true); }"));

    [Fact]
    public void InterfaceDispatchRemainsConservative()
        => Assert.Equal(new[] { TypeChecker.UnknownExceptionType }, Summary("interface I { void Run(); } void Test(I* value) { value.Run(); }"));

    [Fact]
    public void VirtualDispatchRemainsConservative()
        => Assert.Equal(new[] { TypeChecker.UnknownExceptionType }, Summary(Types + "class C { public virtual void Run() { throw new First(); } } void Test(C* value) { value.Run(); }"));

    [Fact]
    public void PlainInstanceCallsPropagateTheirImplementation()
        => Assert.Equal(new[] { "First" }, Summary(Types + "class C { public void Run() { throw new First(); } } void Test(C* value) { value.Run(); }"));

    [Fact]
    public void EmptyThrowingMethodHasEmptySummary()
        => Assert.Empty(Summary("void Test() {}"));

    [Fact]
    public void UncheckedGenericBodyIsNotReportedAsNonThrowing()
        => Assert.Equal(new[] { TypeChecker.UnknownExceptionType }, Summary("void Test<T>() { throw new Exception(); }"));

    [Fact]
    public void InstantiatedGenericCallPropagatesExceptions()
        => Assert.Equal(new[] { "First" }, Summary(Types + "void Fail<T>() { throw new First(); } void Test() { Fail<int>(); }"));

    [Fact]
    public void BaseCatchFiltersNamespacedExceptions()
        => Assert.Empty(Summary("namespace Errors { class Base : Exception {} class Child : Base {} void Fail() { throw new Child(); } } void Test() { try { Errors::Fail(); } catch (Errors::Base* e) {} }"));

    [Fact]
    public void CatchAllFiltersUnknownDispatch()
        => Assert.Empty(Summary("interface I { void Run(); } void Test(I* value) { try { value.Run(); } catch {} }"));
}
