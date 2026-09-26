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
    public void CollectsDistinctDirectThrows()
        => Assert.Equal(new[] { "First", "Second" }, Summary(Types + "void Test(bool b) throws { if (b) throw new* First(); throw new* Second(); }"));

    [Fact]
    public void PropagatesThroughForwardCallsAndRecursion()
        => Assert.Equal(new[] { "First" }, Summary(Types + "void Test(bool b) throws { Other(b); } void Other(bool b) throws { if (b) Test(false); throw new* First(); }"));

    [Fact]
    public void RemovesCaughtExceptionsFromCalls()
        => Assert.Equal(new[] { "Second" }, Summary(Types + "void Fail(bool b) throws { if (b) throw new* First(); throw new* Second(); } void Test() throws { try { Fail(true); } catch (First* e) {} }"));

    [Fact]
    public void RemovesCaughtDirectThrowsAndIncludesHandlerThrows()
        => Assert.Equal(new[] { "Second" }, Summary(Types + "void Test() throws { try { throw new* First(); } catch (Exception* e) { throw new* Second(); } }"));

    [Fact]
    public void CatchAllStopsPropagation()
        => Assert.Empty(Summary(Types + "void Fail() throws { throw new* First(); } void Test() { try { Fail(); } catch {} }"));

    [Fact]
    public void UsesSelectedOverload()
        => Assert.Equal(new[] { "Second" }, Summary(Types + "void Fail(int x) throws { throw new* First(); } void Fail(bool x) throws { throw new* Second(); } void Test() throws { Fail(true); }"));

    [Fact]
    public void InterfaceDispatchRemainsConservative()
        => Assert.Equal(new[] { "Exception" }, Summary("interface I { void Run() throws; } void Test(I* value) throws { value.Run(); }"));

    [Fact]
    public void VirtualDispatchRemainsConservative()
        => Assert.Equal(new[] { "Exception" }, Summary(Types + "class C { public virtual void Run() throws { throw new* First(); } } void Test(C* value) throws { value.Run(); }"));

    [Fact]
    public void PlainInstanceCallsPropagateTheirImplementation()
        => Assert.Equal(new[] { "First" }, Summary(Types + "class C { public void Run() throws { throw new* First(); } } void Test(C* value) throws { value.Run(); }"));

    [Fact]
    public void EmptyThrowingMethodHasEmptySummary()
        => Assert.Empty(Summary("void Test() throws {}"));

    [Fact]
    public void UncheckedGenericBodyIsNotReportedAsNonThrowing()
        => Assert.Equal(new[] { "Exception" }, Summary("void Test<T>() throws { throw new* Exception(); }"));

    [Fact]
    public void InstantiatedGenericCallPropagatesExceptions()
        => Assert.Equal(new[] { "First" }, Summary(Types + "void Fail<T>() throws { throw new* First(); } void Test() throws { Fail<int>(); }"));

    [Fact]
    public void BaseCatchFiltersNamespacedExceptions()
        => Assert.Empty(Summary("namespace Errors { class Base : Exception {} class Child : Base {} void Fail() throws { throw new* Child(); } } void Test() throws { try { Errors::Fail(); } catch (Errors::Base* e) {} }"));

    [Fact]
    public void CatchAllFiltersUnknownDispatch()
        => Assert.Empty(Summary("interface I { void Run() throws; } void Test(I* value) { try { value.Run(); } catch {} }"));
}
