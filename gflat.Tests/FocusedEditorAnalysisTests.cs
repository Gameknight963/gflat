using gflat.ast;
using gflat.comptime;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class FocusedEditorAnalysisTests
{
    private static readonly CompilationOptions Options = new() { NoStdLib = true };

    private static AnalysisSnapshot Analyze(string declarations, string focused, bool partial = true)
    {
        var library = new SourceFile("library.gf", declarations);
        var main = new SourceFile("focused.gf", focused);
        return new AnalysisSnapshot([library, main], Options,
            bodySources: partial ? new HashSet<SourceId> { main.Id } : null);
    }

    private static Diagnostic[] Errors(AnalysisSnapshot snapshot)
        => snapshot.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

    [Fact]
    public void ForeignRuntimeBodiesAreDeferredWhileTheirSignaturesRemainUsable()
    {
        const string library = """
            int Outside(int number) { missingFree(); return number; }
            class Controller
            {
                public Controller() { missingConstructor(); }
                ~Controller() { missingDestructor(); }
                public int Read() { missingInstance(); return 3; }
            }
            struct Pair
            {
                public int Number { get; set; } = 7;
                public static Pair operator +(Pair left, Pair right)
                { missingOperator(); return left; }
            }
            """;
        const string main = """
            int main()
            {
                Controller controller = new();
                Pair pair = new();
                Pair sum = pair + pair;
                return Outside(1) + controller.Read() + sum.Number;
            }
            """;
        var partial = Analyze(library, main);
        Assert.True(partial.Checker!.IsPartialBodyAnalysis);
        Assert.Empty(Errors(partial));
        var full = Analyze(library, main, partial: false);
        Assert.False(full.Checker!.IsPartialBodyAnalysis);
        foreach (string name in new[] { "missingFree", "missingConstructor", "missingDestructor", "missingInstance", "missingOperator" })
            Assert.Contains(Errors(full), diagnostic => diagnostic.Message.Contains(name));
    }

    [Theory]
    [InlineData("int main() => Outside(true);", "cannot pass")]
    [InlineData("int main() { if (false) return Outside(1); }", "Not all code paths")]
    [InlineData("int main() { int value; return value; }", "unassigned")]
    public void FocusedTypeControlFlowAndAssignmentDiagnosticsRemainAvailable(string main, string expected)
    {
        const string library = "int Outside(int value) => value;";
        var partial = Analyze(library, main);
        var full = Analyze(library, main, partial: false);
        Assert.Contains(Errors(partial), diagnostic => diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Errors(full).Select(diagnostic => diagnostic.Message), Errors(partial).Select(diagnostic => diagnostic.Message));
    }

    [Theory]
    [InlineData("struct Invalid { public MissingType value; }", "MissingType")]
    [InlineData("struct Invalid { public int value = true; }", "Cannot assign")]
    [InlineData("int Outside(int value = true) => value;", "default")]
    public void ForeignDeclarationAndInitializerErrorsAreStillChecked(string declarations, string expected)
    {
        var partial = Analyze(declarations, "int main() => 0;");
        Assert.Contains(Errors(partial), diagnostic => diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ForeignConstBodiesAndPerInvocationReflectionExpansionsKeepComputedTypes()
    {
        const string library = """
            const int Twice(int value) => value * 2;
            const int Width(const reflection type) => sizeof([:type:]);
            const reflection Choose(const bool wide)
            {
                static if (wide) { return reflect(long); }
                else { return reflect(int); }
            }
            """;
        const string main = """
            int main()
            {
                const int count = Twice(Width(Choose(false)));
                const int wide = Width(Choose(true));
                int[count] values;
                [:Choose(false):] number = 42;
                static if (count != 8 || wide != 8 || sizeof(int[count]) != 32)
                { incorrectConstantOrArrayType(); }
                return number;
            }
            """;
        Assert.Empty(Errors(Analyze(library, main)));
        Assert.Empty(Errors(Analyze(library, main, partial: false)));
    }

    [Fact]
    public void ConstrainedConstImplementationViewsAreCheckedBeforeInterpretation()
    {
        const string library = """
            static interface IReader { const int Read(); }
            static class Reader : IReader { public static const int Read() => 6; }
            constrained alias Selected : IReader = Reader;
            const int Read<C : IReader>() => C::Read();
            """;
        const string main = """
            int main()
            {
                const int concrete = Read<Reader>();
                const int constrained = Read<Selected>();
                static if (concrete != 6 || constrained != 6) { incorrectConstImplementation(); }
                return constrained;
            }
            """;
        Assert.Empty(Errors(Analyze(library, main)));
        Assert.Empty(Errors(Analyze(library, main, partial: false)));
    }

    [Fact]
    public void ForeignAttributeConstructorsAreDemandedAndProduceTheSameFields()
    {
        const string library = """
            class Attribute {}
            class Tag : Attribute
            {
                public int value;
                public Tag(int input) { value = input + 1; }
            }
            """;
        const string main = "[Tag(41)] int main() => 0;";
        foreach (bool partial in new[] { false, true })
        {
            var snapshot = Analyze(library, main, partial);
            Assert.Empty(Errors(snapshot));
            var method = snapshot.Nodes.Select(node => node.Node).OfType<MethodDeclaration>().Single(method => method.Name == "main");
            var attribute = Assert.Single(snapshot.Checker!.GetAttributes(method));
            Assert.Equal(new ConstValue.Integer(42), attribute.Value.Fields["value"]);
            Assert.True(snapshot.Checker.WasBodyChecked(attribute.Constructor!));
        }
    }

    [Fact]
    public void SpecializedInterfaceAndAutoGetterCapabilitiesComeFromDeclarations()
    {
        const string library = """
            interface IValue<T> { T Value { immutable get; } }
            struct Box<T> : IValue<T>
            {
                public T Value { get; set; }
                public Box(T value) { Value = value; }
            }
            """;
        const string main = """
            int main()
            {
                Box<int> box = new(42);
                immutable(Box<int>)* immutableView = &box;
                IValue<int>* interfaceView = &box;
                return immutableView.Value + interfaceView.Value;
            }
            """;
        Assert.Empty(Errors(Analyze(library, main)));
        Assert.Empty(Errors(Analyze(library, main, partial: false)));
    }

    [Fact]
    public void SpecializedRuntimeBodyErrorsAreDeferred()
    {
        const string library = """
            struct Box<T>
            {
                public T Value;
                public int Read() { missingSpecializedBody(); return 0; }
            }
            T Identity<T>(T value) { missingGenericBody(); return value; }
            """;
        const string main = "int main() { Box<int> box = new(); return Identity(box.Read()); }";
        Assert.Empty(Errors(Analyze(library, main)));
        var full = Analyze(library, main, partial: false);
        Assert.Contains(Errors(full), diagnostic => diagnostic.Message.Contains("missingSpecializedBody"));
        Assert.Contains(Errors(full), diagnostic => diagnostic.Message.Contains("missingGenericBody"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignCopyAndDestructorDeclarationsKeepOwnershipRules(bool copyable)
    {
        string copy = copyable ? "public copy Resource(immutable(Resource<T>)* source) { Value = source.Value; }" : "";
        string library = $$"""
            struct Resource<T>
            {
                public T Value;
                public Resource(T value) { Value = value; }
                {{copy}}
                ~Resource() {}
            }
            void Consume(Resource<int> value) {}
            """;
        const string main = "int main() { Resource<int> resource = new(42); Consume(resource); return 0; }";
        var partialErrors = Errors(Analyze(library, main));
        var fullErrors = Errors(Analyze(library, main, partial: false));
        if (copyable)
        {
            Assert.Empty(partialErrors);
            Assert.Empty(fullErrors);
        }
        else
        {
            Assert.Contains(partialErrors, diagnostic => diagnostic.Message.Contains("cannot pass", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(fullErrors.Select(diagnostic => diagnostic.Message), partialErrors.Select(diagnostic => diagnostic.Message));
        }
    }
}
