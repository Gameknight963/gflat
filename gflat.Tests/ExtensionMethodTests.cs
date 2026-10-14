using gflat.CompileExceptions;
using gflat.ast;

namespace gflat.Tests;

public sealed class ExtensionMethodTests
{
    [Theory]
    [InlineData("int Add(this int self, int n = 2) => self + n; int main() { int n = 40; return n.Add(); }")]
    [InlineData("using Helpers; namespace Helpers { int Add(this int self, int n) => self + n; } int main() { int n = 40; return n.Add(2); }")]
    [InlineData("namespace Helpers { int Add(this int self, int n) => self + n; } int main() { return Helpers::Add(40, 2); }")]
    [InlineData("int Read(this int* self) => *self; int main() { int n = 42; int* p = &n; return p.Read(); }")]
    [InlineData("int Read(this ref readonly(int) self) => self; int main() { int n = 42; int* p = &n; return p.Read(); }")]
    [InlineData("void Set(this ref int self, int n) { self = n; } int main() { int n = 0; n.Set(42); return n; }")]
    [InlineData("int Read(this int*? self) { if (self == null) return 42; return *self; } int main() { int*? p = null; return p.Read(); }")]
    [InlineData("T Identity<T>(this T self) => self; int main() { int n = 42; return n.Identity(); }")]
    [InlineData("struct Item { public int Read() => 42; } int Read(this ref Item self) => 1; int main() { Item n = new Item(); return n.Read(); }")]
    [InlineData("struct Item { public int Count { get => 40; } } int Add(this ref Item self, int n) => self.Count + n; int main() { Item n = new Item(); return n.Add(2); }")]
    public void NamespaceExtensionsUseOrdinaryCalls(string source)
    {
        var result = CompilerTestHelper.Run(source);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("int Bad(int n, this int self) => n; int main() => 0;", "first parameter")]
    [InlineData("int Bad(this int self = 0) => self; int main() => 0;", "default")]
    [InlineData("struct S { public int Bad(this int self) => self; } int main() => 0;", "namespace functions")]
    [InlineData("extern int Bad(this int self); int main() => 0;", "namespace functions")]
    [InlineData("int main() { int(int)* f = (this int n) => n; return 0; }", "namespace functions")]
    [InlineData("namespace Helpers { int Add(this int self) => self; } int main() { int n = 0; return n.Add(); }", "struct or class")]
    [InlineData("void Set(this ref int self) { self = 1; } int main() { readonly(int) n = 0; n.Set(); return 0; }", "struct or class")]
    [InlineData("int Read(this ref int self) => self; int main() { int*? p = null; return p.Read(); }", "nullable")]
    public void InvalidExtensionsHaveSourceDiagnostics(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactPointerReceiverWinsBeforePointeeAdaptation()
    {
        var result = CompilerTestHelper.Run("int Read(this int* self) => 42; int Read(this ref int self) => 1; int main() { int n = 0; int* p = &n; return p.Read(); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void VisibleNamespacesParticipateInOverloadResolution()
    {
        var result = CompilerTestHelper.Run("using A; using B; namespace A { int Add(this int self, int n) => self + n; } namespace B { int Add(this int self, bool n) => 0; } int main() { int n = 40; return n.Add(2); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void EqualVisibleCandidatesAreAmbiguous()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("using A; using B; namespace A { int Add(this int self) => self; } namespace B { int Add(this int self) => self; } int main() { int n = 0; return n.Add(); }"));
        Assert.Contains("ambiguous", error.Message);
    }

    [Fact]
    public void InstanceMethodErrorsAreNotHiddenByExtensions()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("struct S { public int Read(int n) => n; } int Read(this ref S self) => 42; int main() { S s = new S(); return s.Read(); }"));
        Assert.Contains("expects", error.Message);
    }

    [Fact]
    public void ReceiverEvaluatesOnceBeforeArguments()
    {
        var result = CompilerTestHelper.Run("""
            int* Receiver(ref int steps) { steps = steps * 10 + 1; return &steps; }
            int Argument(ref int steps) { steps = steps * 10 + 2; return 0; }
            int Read(this int* self, int ignored) => *self;
            int main() { int steps = 0; return Receiver(ref steps).Read(Argument(ref steps)); }
            """);
        Assert.Equal(12, result.ExitCode);
    }

    [Fact]
    public void ValueReceiverCopiesAndDestroysWhileReferenceReceiverBorrows()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            struct R {
                public int n;
                public R(int value) { n = value; }
                public copy R(readonly(R)* source) { n = source.n; printf("C"); }
                ~R() { printf("D"); }
            }
            int Read(this R self) => self.n;
            int Borrow(this ref readonly(R) self) => self.n;
            int main() { R r = new R(21); return r.Read() + r.Borrow(); }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("CDD", result.StandardOutput);
    }

    [Fact]
    public void FreshValueReceiverDoesNotCopyAndHasOneDestructor()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            struct R { public int n; public R() { n = 42; } ~R() { printf("D"); } }
            int Read(this R self) => self.n;
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
            T Read<T>(this ref readonly(Box<T>) self) => self.value;
            int main() { Box<int> box = new Box<int>(42); return box.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CompletionPreviewDoesNotCheckUnusedGenericBodies()
    {
        var (ast, checker) = Compiler.Check("""
            struct Box<T> { public T value; public Box(T n) { value = n; } }
            T Broken<T>(this ref readonly(Box<T>) self) { return MissingFunction(); }
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
            int Fail(this int self, int code = 42) { throw new Exception("extension", code); }
            int main() { int n = 0; try { return n.Fail(); } catch (Exception* error) { return error.GetCode(); } }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ReplacementCannotSilentlyRemoveExtensionMarker()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("weak int Read(this int self) => self; replace int Read(int self) => self; int main() => 0;"));
        Assert.Contains("modifiers", error.Message);
    }

    [Fact]
    public void RefReceiverKeepsDeclaredNullableStorage()
    {
        var result = CompilerTestHelper.Run("""
            void Clear(this ref int*? self) { self = null; }
            int main() { int n = 42; int*? p = &n; if (p == null) return 1; p.Clear(); if (p == null) return 42; return 0; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ManagedReceiverRemainsRootedDuringCall()
    {
        var result = CompilerTestHelper.Run(new[] {
            new SourceFile("GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/libc/GC.gf"))),
            new SourceFile("main.gf", "int Read(this int^ self) { GC::Collect(); return *self; } int main() => (new^ int(42)).Read();") });
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void NamespacedGenericReceiverDoesNotMatchAnotherNamespaceType()
    {
        var result = CompilerTestHelper.Run("""
            using A;
            namespace A {
                struct Box<T> { public T value; public Box(T n) { value = n; } }
                T Read<T>(this ref readonly(Box<T>) self) => self.value;
            }
            namespace B { struct Box<T> { public T value; public Box(T n) { value = n; } } }
            int main() { B::Box<int> other = new B::Box<int>(0); A::Box<int> box = new A::Box<int>(42); return box.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void RefReceiverCannotChangeNarrowedSlotToNonnullable()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("int Read(this ref int* self) => *self; int main() { int n = 42; int*? p = &n; if (p == null) return 0; return p.Read(); }"));
        Assert.Contains("not a struct or class", error.Message);
    }

    [Fact]
    public void BorrowingReceiverInvalidatesExposedNullableFacts()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("void Clear(this ref int*? self) { self = null; } int main() { int n = 42; int*? p = &n; if (p == null) return 0; p.Clear(); return *p; }"));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReceiverCopyEffectsHappenBeforeLaterArgumentAnalysis()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            struct R {
                public int*?* slot;
                public R(int*?* s) { slot = s; }
                public copy R(readonly(R)* other) nothrow { slot = other.slot; *slot = null; }
                ~R() {}
            }
            int Read(this R self, int value) => value;
            int main() { int n = 42; int*? p = &n; R r = new R(&p); if (p == null) return 0; return r.Read(*p); }
            """));
        Assert.Contains("nullable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FunctionPointerFieldTakesPriority()
    {
        var result = CompilerTestHelper.Run("""
            alias Callback = int()*;
            int Actual() => 42;
            struct S { public Callback Read; public S() { Read = &Actual; } }
            int Read(this ref S self) => 1;
            int main() { S s = new S(); return s.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ManagedPointeeDoesNotBecomeBorrowableThroughExtensions()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(new[] {
            new SourceFile("GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/libc/GC.gf"))),
            new SourceFile("main.gf", "struct S {} int Read(this ref S self) => 42; int main() { S^ s = new^ S(); return s.Read(); }") }));
        Assert.Contains("no field or method", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalFunctionPointerNameDoesNotHijackAnExtension()
    {
        var result = CompilerTestHelper.Run("int Wrong() => 1; int Read(this int self) => self; int main() { int()* Read = &Wrong; int n = 42; return n.Read(); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ConstantExtensionCallsUseBoundReceiver()
    {
        var result = CompilerTestHelper.Run("""
            using A; using B;
            namespace A { const int Plus(this int self, int n = 2) => self + n; }
            namespace B { const int Plus(this char self, int n = 2) => 0; }
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
            namespace Helpers { T Read<T>(this ref readonly(Box<T>) self) => self.value; }
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
            T Identity<T : I>(this T self) { return MissingFunction(); }
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
                T Identity<T : Contract>(this T self) { return MissingFunction(); }
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
            void Set(this ref int self, int n) { self = n; }
            int Read(this ref readonly(int) self) => self;
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
            new SourceFile("extensions.gf", "using Contracts; namespace Helpers; int ReadValue<T : I>(this T self) => self.Read();"),
            new SourceFile("main.gf", "using Helpers; int main() { Contracts::S s = new Contracts::S(); return s.ReadValue(); }") });
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void PreviewDoesNotInstantiateUnboundTypesInsideFunctionSignatures()
    {
        var (ast, checker) = Compiler.Check("""
            struct Box<T> { public T value; }
            int Unused<T>(this Box<T>(Box<T>)* callback) => 0;
            int main() => 0;
            """);
        int count = ast.Members.Count;
        var source = new FunctionPointerTypeExpression(new NamedTypeExpression("int", null, 1),
            [new NamedTypeExpression("int", null, 1)], false, false, 1);
        Assert.Empty(checker.GetVisibleExtensions("", [], source, false, false, "Unused"));
        Assert.Equal(count, ast.Members.Count);
    }
}
