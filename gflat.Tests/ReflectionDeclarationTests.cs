using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionDeclarationTests
{
    [Fact]
    public void RepeatedVariableQueriesUseTheDeclarationAcrossUseLines()
    {
        Check("""
            int main()
            {
                int value = 42;
                const reflection first = reflect(value);
                const reflection second = reflect(value);
                static if (first != second) { wrongIdentity(); }
                static if (first.Type != reflect(int)) { wrongType(); }
                static if (first.Kind != "Variable") { wrongKind(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void ShadowedDeclarationsHaveDifferentIdentity()
    {
        Check("""
            int main()
            {
                int value = 42;
                const reflection outer = reflect(value);
                { int value = 7; static if (reflect(value) == outer) { wrongShadow(); } }
                static if (reflect(value) != outer) { wrongRestoration(); }
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ParameterQueriesMatchMethodMetadataAndSpliceTheActiveParameter(int optimization)
    {
        Run("""
            int Read(int value)
            {
                const reflection first = reflect(value);
                const reflection second = reflect(value);
                const reflection parameter = reflect(Read).Parameters[0];
                static if (first != second || first != parameter) { wrongIdentity(); }
                static if (first.Kind != "Parameter" || first.Position != 0) { wrongParameter(); }
                static if (first.Type != reflect(int)) { wrongType(); }
                return [:first:];
            }
            int main() => Read(42);
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LocalSplicesUseTheirStillActiveDeclaration(int optimization)
    {
        Run("""
            int main()
            {
                int value = 42;
                const reflection local = reflect(value);
                return [:local:];
            }
            """, optimization);
    }

    [Fact]
    public void ShadowedLocalHandleCannotSilentlyBindToTheShadow()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            int main()
            {
                int value = 42;
                const reflection outer = reflect(value);
                { int value = 7; return [:outer:]; }
            }
            """));
        Assert.Contains("hidden in this scope", error.Message);
    }

    [Fact]
    public void EscapedLocalMetadataRemainsInspectableButCannotBeSpliced()
    {
        const string declaration = """
            const reflection DescribeLocal()
            {
                int value = 42;
                return reflect(value);
            }
            """;
        Check(declaration + """
            int main()
            {
                const reflection saved = DescribeLocal();
                static if (saved.Name != "value" || saved.Type != reflect(int)) { wrongMetadata(); }
                return 0;
            }
            """);
        var error = Assert.Throws<TypeCheckException>(() => Check(declaration + """
            int main()
            {
                int value = 7;
                const reflection saved = DescribeLocal();
                return [:saved:];
            }
            """));
        Assert.Contains("outside its declaration's scope", error.Message);
    }

    [Fact]
    public void LocalAliasLookupUsesTheNearestDeclaration()
    {
        Check("""
            alias Number = long;
            int main()
            {
                const reflection outer = reflect(Number);
                alias Number = int;
                const reflection local = reflect(Number);
                static if (local.Kind != "Alias" || local == outer) { wrongAlias(); }
                static if (local.Target != reflect(int)) { wrongLocalTarget(); }
                { alias Number = byte; static if (reflect(Number).Target != reflect(byte)) { wrongInner(); } }
                static if (reflect(Number) != local) { wrongRestoration(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void ImportedAndQualifiedAliasQueriesShareTheSelectedDeclaration()
    {
        Check("""
            using Library;

            namespace Library { public alias Count = int; }
            int main()
            {
                const reflection imported = reflect(Count);
                const reflection qualified = reflect(Library::Count);
                static if (imported != qualified || qualified.Kind != "Alias") { wrongDeclaration(); }
                static if (qualified.QualifiedName != "Library::Count") { wrongQualification(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void ImportedAliasAmbiguityIsDiagnosed()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            using First;
            using Second;

            namespace First { public alias Count = int; }
            namespace Second { public alias Count = long; }
            int main() { const reflection info = reflect(Count); return 0; }
            """));
        Assert.Contains("ambiguous", error.Message);
    }

    [Fact]
    public void AliasMetadataDescribesValidationAndExposureSeparately()
    {
        Check("""
            static interface IReader { int Read(); }
            static class Reader : IReader { public static int Read() => 42; }
            alias Checked : IReader = Reader;
            constrained alias Restricted : IReader = Reader;
            int main()
            {
                const reflection checked = reflect(Checked);
                const reflection restricted = reflect(Restricted);
                static if (checked.IsConstrained || !restricted.IsConstrained) { wrongView(); }
                static if (checked.Constraints[0] != reflect(IReader)) { wrongValidation(); }
                static if (restricted.Constraints[0] != reflect(IReader)) { wrongRestriction(); }
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("const reflection info = reflect(Selected); return Extra<[:info:]>();")]
    [InlineData("alias Local = Selected; const reflection info = reflect(Local); return Extra<[:info:]>();")]
    public void AliasTypeSplicesRetainTheirCapabilityView(string operation)
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            static interface IReader { int Read(); }
            static class Reader : IReader {
                public static int Read() => 42;
                public static int Extra() => 7;
            }
            constrained alias Selected : IReader = Reader;
            int Extra<T>() => T::Extra();
            """ + "int main(){" + operation + "}"));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamespaceAndGlobalFieldHandlesIgnoreCallerShadows(int optimization)
    {
        Run("""
            int number = 30;
            namespace Library { public int number = 12; }
            const reflection root = reflect(global::number);
            const reflection qualified = reflect(Library::number);
            int Read()
            {
                int number = 7;
                static if (!root.IsStatic || !qualified.IsStatic) { wrongStorage(); }
                static if (qualified.QualifiedName != "Library::number") { wrongName(); }
                return [:root:] + [:qualified:];
            }
            int main() => Read();
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NamespaceConstFieldsKeepTheirDeclarationTypeContext(int optimization)
    {
        Run("""
            using Library;

            namespace Library { alias Count = long; public const Count Answer = 42; }
            int main()
            {
                const reflection imported = reflect(Answer);
                const reflection qualified = reflect(Library::Answer);
                static if (imported != qualified || !qualified.IsConst) { wrongDeclaration(); }
                static if (qualified.Type != reflect(long)) { wrongType(); }
                return (int)[:qualified:];
            }
            """, optimization);
    }

    [Fact]
    public void NamespaceFieldReflectionDoesNotGrantPrivateSourceAccess()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check([
            new SourceFile("private-library.gf", "namespace Library; private int number=42;"),
            new SourceFile("private-caller.gf", "const reflection field=reflect(Library::number);int main()=>[:field:];")
        ], options: new() { NoStdLib = true }));
        Assert.Contains("private", error.Message);
    }

    private static void Check(string source) => Compiler.Check(source, options: new() { NoStdLib = true });

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, InternalizeFunctions = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 42);
    }
}
