using gflat.CompileExceptions;
using gflat.ast;

namespace gflat.Tests;

public sealed class ExtensionMethodTests
{
    [Theory]
    [InlineData("int Add(int self, int n = 2) => self + n;\nextension int { public int Add(int n = 2) => (*this) + n; } int main() { int n = 40; return n.Add(); }")]
    [InlineData("using Helpers; namespace Helpers { int Add(int self, int n) => self + n;\nextension int { public int Add(int n) => (*this) + n; } } int main() { int n = 40; return n.Add(2); }")]
    [InlineData("namespace Helpers { int Add(int self, int n) => self + n;\nextension int { public int Add(int n) => (*this) + n; } } int main() { return Helpers::Add(40, 2); }")]
    [InlineData("int Read(int* self) => *self;\nextension int { public int Read() => *this; } int main() { int n = 42; int* p = &n; return p.Read(); }")]
    [InlineData("int Read(ref readonly(int) self) => self;\nextension int { public readonly int Read() => (*this); } int main() { int n = 42; int* p = &n; return p.Read(); }")]
    [InlineData("void Set(ref int self, int n) { self = n; }\nextension int { public void Set(int n) { (*this) = n; } } int main() { int n = 0; n.Set(42); return n; }")]
    [InlineData("int Read(int*? self) { if (self == null) return 42; return *self; } int main() { int*? p = null; return Read(p); }")]
    [InlineData("T Identity<T>(T self) => self;\nextension<T> T { public T Identity() => (*this); } int main() { int n = 42; return n.Identity(); }")]
    [InlineData("struct Item { public int Read() => 42; } int Read(ref Item self) => 1;\nextension Item { public int Read() => 1; } int main() { Item n = new Item(); return n.Read(); }")]
    [InlineData("struct Item { public int Count { get => 40; } } int Add(ref Item self, int n) => self.Count + n;\nextension Item { public int Add(int n) => (*this).Count + n; } int main() { Item n = new Item(); return n.Add(2); }")]
    public void NamespaceExtensionsUseOrdinaryCalls(string source)
    {
        var result = CompilerTestHelper.Run(source);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("int Bad(int n, this int self) => n; int main() => 0;", "this parameter modifier was removed")]
    [InlineData("extension int* { public int Bad() => **this; } int main() => 0;", "pointer")]
    [InlineData("struct S { public int Bad(this int self) => self; } int main() => 0;", "this parameter modifier was removed")]
    [InlineData("extern int Bad(this int self); int main() => 0;", "this parameter modifier was removed")]
    [InlineData("extension int { public void Set() { *this = 1; } } int main() { immutable(int) n = 0; n.Set(); return 0; }", "Set")]
    [InlineData("namespace Helpers { extension int { public int Add() => *this; } } int main() { int n = 0; return n.Add(); }", "Add")]
    [InlineData("extension int { public void Set() { *this = 1; } } int main() { readonly(int) n = 0; n.Set(); return 0; }", "Set")]
    [InlineData("extension int { public readonly int Read() => *this; } int main() { int*? p = null; return p.Read(); }", "nullable")]
    public void InvalidExtensionsHaveSourceDiagnostics(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(error.Span.Length > 0);
    }

    [Fact]
    public void PointerAndValueReceiversCannotDeclareDifferentImplementationsForTheSameMember()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("extension int { public int Read() => 42; } extension int { public int Read() => 1; } int main() => 0;"));
        Assert.Contains("Read", error.Message);
        Assert.True(error.Span.Length > 0);
    }

    [Fact]
    public void VisibleNamespacesParticipateInOverloadResolution()
    {
        var result = CompilerTestHelper.Run("using A; using B; namespace A { int Add(int self, int n) => self + n;\nextension int { public int Add(int n) => (*this) + n; } } namespace B { int Add(int self, bool n) => 0;\nextension int { public int Add(bool n) => 0; } } int main() { int n = 40; return n.Add(2); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void EqualVisibleCandidatesAreAmbiguous()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("using A; using B; namespace A { int Add(int self) => self;\nextension int { public int Add() => (*this); } } namespace B { int Add(int self) => self;\nextension int { public int Add() => (*this); } } int main() { int n = 0; return n.Add(); }"));
        Assert.Contains("ambiguous", error.Message);
    }

    [Fact]
    public void InstanceMethodErrorsAreNotHiddenByExtensions()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("struct S { public int Read(int n) => n; } int Read(ref S self) => 42;\nextension S { public int Read() => 42; } int main() { S s = new S(); return s.Read(); }"));
        Assert.Contains("expects", error.Message);
    }

    [Fact]
    public void ReceiverEvaluatesOnceBeforeArguments()
    {
        var result = CompilerTestHelper.Run("""
            int* Receiver(ref int steps) { steps = steps * 10 + 1; return &steps; }
            int Argument(ref int steps) { steps = steps * 10 + 2; return 0; }
            int Read(int* self, int ignored) => *self;
            extension int { public int Read(int ignored) => *this; }
            int main() { int steps = 0; return Receiver(ref steps).Read(Argument(ref steps)); }
            """);
        Assert.Equal(12, result.ExitCode);
    }

    [Fact]
    public void AllExtensionReceiversBorrowWithoutCopyingOrDestroyingTheValue()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(immutable(char*) text, ...);
            struct R {
                public int n;
                public R(int value) { n = value; }
                public copy R(immutable(R)* source) { n = source.n; printf("C"); }
                ~R() { printf("D"); }
            }
            int Read(R self) => self.n;
            extension R { public int Read() => (*this).n; }
            int Borrow(ref immutable(R) self) => self.n;
            extension R { public immutable int Borrow() => (*this).n; }
            int main() { R r = new R(21); return r.Read() + r.Borrow(); }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("D", result.StandardOutput);
    }

    [Fact]
    public void FreshValueReceiverDoesNotCopyAndHasOneDestructor()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(immutable(char*) text, ...);
            struct R { public int n; public R() { n = 42; } ~R() { printf("D"); } }
            int Read(R self) => self.n;
            extension R { public int Read() => (*this).n; }
            int main() => new R().Read();
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("D", result.StandardOutput);
    }

    [Fact]
    public void GenericContainerReceiverInfersElementType()
    {
        var result = CompilerTestHelper.Run("""
            struct Box<T> { public T value; public Box(T n) { value = n; } }
            T Read<T>(ref immutable(Box<T>) self) => self.value;
            extension<T> Box<T> { public immutable T Read() => (*this).value; }
            int main() { Box<int> box = new Box<int>(42); return box.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CompletionPreviewDoesNotCheckUnusedGenericBodies()
    {
        var (ast, checker) = Compiler.Check("""
            struct Box<T> { public T value; public Box(T n) { value = n; } }
            T Broken<T>(ref immutable(Box<T>) self) { return MissingFunction(); }
            extension<T> Box<T> { public immutable T Broken() { return MissingFunction(); } }
            int main() { Box<int> box = new Box<int>(42); return 0; }
            """);
        int count = ast.Members.Count;
        var receiver = new NamedTypeExpression("Box$int", null, 1);
        var candidates = checker.GetVisibleExtensions("", [], receiver, true, false, "Broken");
        var candidate = Assert.Single(candidates);
        Assert.Equal("int", Assert.IsType<NamedTypeExpression>(candidate.GenericBindings["T"]).Name);
        Assert.Equal(count, ast.Members.Count);
        Assert.Null(checker.GetStruct("Box$Missing"));
    }

    [Fact]
    public void ExtensionCallPropagatesExceptionsAndUsesDefaults()
    {
        var result = CompilerTestHelper.Run("""
            int Fail(int self, int code = 42) { throw new Exception("extension", code); }
            extension int { public int Fail(int code = 42) { throw new Exception("extension", code); } }
            int main() { int n = 0; try { return n.Fail(); } catch (Exception* error) { return error.GetCode(); } }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void LegacyThisModifierCannotBeReintroducedByAReplacement()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("weak int Read(int self) => self; replace int Read(this int self) => self; int main() => 0;"));
        Assert.Contains("this parameter modifier was removed", error.Message);
    }

    [Fact]
    public void ExplicitRefFunctionsCanReplaceNullablePointerSlots()
    {
        var result = CompilerTestHelper.Run("""
            void Clear(ref int*? self) { self = null; }
            int main() { int n = 42; int*? p = &n; if (p == null) return 1; Clear(ref p); if (p == null) return 42; return 0; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ManagedReceiverRemainsRootedDuringCall()
    {
        var result = CompilerTestHelper.Run(new[] {
            new SourceFile("Standard/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", "libc", "TracingCollector.gf"))),
            new SourceFile("GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/libc/GC.gf"))),
            new SourceFile("main.gf", "int Read(int^ self) { GC::Collect(); return *self; }\nextension int { public int Read() { GC::Collect(); return *this; } } int main() => (new^ int(42)).Read();") });
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void NamespacedGenericReceiverDoesNotMatchAnotherNamespaceType()
    {
        var result = CompilerTestHelper.Run("""
            using A;
            namespace A {
                struct Box<T> { public T value; public Box(T n) { value = n; } }
                T Read<T>(ref immutable(Box<T>) self) => self.value;
                extension<T> Box<T> { public immutable T Read() => (*this).value; }
            }
            namespace B { struct Box<T> { public T value; public Box(T n) { value = n; } } }
            int main() { B::Box<int> other = new B::Box<int>(0); A::Box<int> box = new A::Box<int>(42); return box.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ExtensionBlocksCannotTargetPointerSlots()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("extension int* { public int Read() => **this; } int main() => 0;"));
        Assert.Contains("pointer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitRefPointerCallsInvalidateExposedNullableFacts()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("void Clear(ref int*? self) { self = null; } int main() { int n = 42; int*? p = &n; if (p == null) return 0; Clear(ref p); return *p; }"));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BorrowingAnExtensionDoesNotRunTheReceiversCopyConstructor()
    {
        var result = CompilerTestHelper.Run("""
            struct R {
                public int*?* slot;
                public R(int*?* s) { slot = s; }
                public copy R(readonly(R)* other) nothrow { slot = other.slot; *slot = null; }
                ~R() {}
            }
            int Read(R self, int value) => value;
            extension R { public int Read(int value) => value; }
            int main() { int n = 42; int*? p = &n; R r = new R(&p); if (p == null) return 0; return r.Read(*p); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void FunctionPointerFieldTakesPriority()
    {
        var result = CompilerTestHelper.Run("""
            alias Callback = int()*;
            int Actual() => 42;
            struct S { public Callback Read; public S() { Read = &Actual; } }
            int Read(ref S self) => 1;
            extension S { public int Read() => 1; }
            int main() { S s = new S(); return s.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ManagedPointeesBorrowTheSameInstanceReceiver()
    {
        var result = CompilerTestHelper.Run(new[] {
            new SourceFile("Standard/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", "libc", "TracingCollector.gf"))),
            new SourceFile("GC.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/libc/GC.gf"))),
            new SourceFile("main.gf", "struct S { public int value; } extension S { public int Read() { GC::Collect(); return this.value; } } int main() { S^ s = new^ S(); s.value = 42; return s.Read(); }") });
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void LocalFunctionPointerNameDoesNotHijackAnExtension()
    {
        var result = CompilerTestHelper.Run("int Wrong() => 1; int Read(int self) => self;\nextension int { public int Read() => (*this); } int main() { int()* Read = &Wrong; int n = 42; return n.Read(); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ConstantExtensionCallsUseBoundReceiver()
    {
        var result = CompilerTestHelper.Run("""
            using A; using B;
            namespace A { const int Plus(int self, int n = 2) => self + n;
            extension int { public const int Plus(int n = 2) => (*this) + n; } }
            namespace B { const int Plus(char self, int n = 2) => 0;
            extension char { public const int Plus(int n = 2) => 0; } }
            const int Answer = (40).Plus();
            int main() => Answer;
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericReceiverCanUseImportedTypeNames()
    {
        var result = CompilerTestHelper.Run("""
            using Models; using Helpers;
            namespace Models { struct Box<T> { public T value; public Box(T n) { value = n; } } }
            namespace Helpers { T Read<T>(ref immutable(Box<T>) self) => self.value;
            extension<T> Box<T> { public immutable T Read() => (*this).value; } }
            int main() { Box<int> box = new Box<int>(42); return box.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void PreviewRejectsKnownConstraintFailuresWithoutSpecialization()
    {
        var (ast, checker) = Compiler.Check("""
            interface I { int Read(); }
            struct S : I { public int Read() => 42; }
            T Identity<T : I>(T self) { return MissingFunction(); }
            extension<T : I> T { public T Identity() { return MissingFunction(); } }
            int main() => 0;
            """);
        int count = ast.Members.Count;
        Assert.Empty(checker.GetVisibleExtensions("", [], new NamedTypeExpression("int", null, 1), true, false, "Identity"));
        Assert.Single(checker.GetVisibleExtensions("", [], new NamedTypeExpression("S", null, 1), true, false, "Identity"));
        Assert.Equal(count, ast.Members.Count);
    }

    [Fact]
    public void PreviewResolvesGenericConstraintAliasesInTheirNamespace()
    {
        var (ast, checker) = Compiler.Check("""
            namespace Contracts {
                interface I { int Read(); }
                alias Contract = I;
                struct S : I { public int Read() => 42; }
                T Identity<T : Contract>(T self) { return MissingFunction(); }
                extension<T : Contract> T { public T Identity() { return MissingFunction(); } }
            }
            int main() => 0;
            """);
        int count = ast.Members.Count;
        var source = new NamedTypeExpression(checker.GetStruct("Contracts::S")!.Name, null, 1);
        Assert.Single(checker.GetVisibleExtensions("", ["Contracts"], source, true, false, "Identity"));
        Assert.Empty(checker.GetVisibleExtensions("", ["Contracts"], new NamedTypeExpression("int", null, 1), true, false, "Identity"));
        Assert.Equal(count, ast.Members.Count);
    }

    [Fact]
    public void RefParametersComposeWithRefReceiverExtensions()
    {
        var result = CompilerTestHelper.Run("""
            void Set(ref int self, int n) { self = n; }
            extension int { public void Set(int n) { (*this) = n; } }
            int Read(ref readonly(int) self) => self;
            extension int { public readonly int Read() => (*this); }
            int Update(ref int value) { value.Set(42); return value.Read(); }
            int main() { int value = 0; return Update(ref value); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericConstraintsUseDefinitionFileImports()
    {
        var result = CompilerTestHelper.Run(new[] {
            new SourceFile("contracts.gf", "namespace Contracts; interface I { int Read(); } struct S : I { public int Read() => 42; }"),
            new SourceFile("extensions.gf", "using Contracts; namespace Helpers; int ReadValue<T : I>(T self) => self.Read();\nextension<T : I> T { public int ReadValue() => (*this).Read(); }"),
            new SourceFile("main.gf", "using Helpers; int main() { Contracts::S s = new Contracts::S(); return s.ReadValue(); }") });
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void PreviewDoesNotInstantiateUnboundTypesInsideFunctionSignatures()
    {
        var (ast, checker) = Compiler.Check("""
            struct Box<T> { public T value; }
            struct Callback<F> {} extension<T> Callback<Box<T>(Box<T>)> { public int Unused() => 0; }
            int main() => 0;
            """);
        int count = ast.Members.Count;
        var source = new FunctionPointerTypeExpression(new NamedTypeExpression("int", null, 1),
            [new NamedTypeExpression("int", null, 1)], false, false, 1);
        Assert.Empty(checker.GetVisibleExtensions("", [], source, false, false, "Unused"));
        Assert.Equal(count, ast.Members.Count);
    }

    [Theory]
    [InlineData("T", false)]
    [InlineData("int", false)]
    [InlineData("int", true)]
    public void BroadRefReceiverPreviewUsesOnlyPreviouslyCheckedGenericTypes(string argument, bool checkedReceiver)
    {
        string declaration = checkedReceiver ? "Box<int> value = new();" : "";
        var (ast, checker) = Compiler.Check($$$"""
            struct Box<T> { public T value; }
            int Inspect<T>(ref immutable(T) value) => MissingFunction();
            extension<T> T { public immutable int Inspect() => MissingFunction(); }
            int main() { {{{declaration}}} return 0; }
            """);
        int count = ast.Members.Count;
        var receiver = new NamedTypeExpression("Box", null, 1, [new NamedTypeExpression(argument, null, 1)]);
        var candidates = checker.GetVisibleExtensions("", [], receiver, true, false, "Inspect");
        if (checkedReceiver) Assert.Single(candidates);
        else Assert.Empty(candidates);
        Assert.Equal(count, ast.Members.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PreviewBorrowsThePointeeAndSelectsTheConstrainedOverload(int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run("""
            interface IMark { void Mark(); }
            struct Item : IMark { public void Mark() {} }
            int Touch<S>(ref S value, long tag) => 1;
            extension<S> S { public int Touch(long tag) => 1; }
            int Touch<S : IMark>(ref S value, int tag) => 2;
            extension<S : IMark> S { public int Touch(int tag) => 2; }
            int Forward<P>(P* value) => value.Touch(0);
            int ForwardMarked<P : IMark>(P* value) => value.Touch(0);
            int main() {
                Item value = new();
                Item* pointer = &value;
                if (value.Touch(0) != 2 || ForwardMarked<Item>(pointer) != 2) return 1;
                return Forward<Item>(pointer) - 1;
            }
            """, optimization).ExitCode);
}
