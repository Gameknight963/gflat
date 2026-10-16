using gflat.CompileExceptions;
using gflat.semantics;

namespace gflat.Tests;

public sealed class HierarchyResolutionRepeatTests
{
    [Fact]
    public void GenericClassFieldPublishedDuringSignaturesIsNotAddedAgain()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Demo {
                struct Owner { public Box<int> value; }
                class Box<T> { public T value; public static Owner Make()=>new Owner(); }
            }
            int main(){Demo::Owner value=Demo::Box<int>::Make();return value.value.value;}
            """).ExitCode);

    [Fact]
    public void RepeatedHierarchyPassesPreserveFieldsAndVirtualSlots()
    {
        var (_, checker) = Compiler.Check("""
            class Base { public int first; public virtual int Read()=>1; }
            class Derived : Base { public int second; public override int Read()=>42; }
            int main()=>0;
            """);
        var type = checker.GetClass("Derived")!;
        var fields = type.Fields.ToArray();
        var methods = type.VirtualMethods.ToArray();
        var slots = type.VTableSlots.ToArray();
        var declarations = type.FieldDeclarationsByName.ToArray();
        new HierarchyResolutionPass(checker.Symbols).ResolveHierarchy(type);
        new HierarchyResolutionPass(checker.Symbols).Execute();
        Assert.Equal(fields, type.Fields);
        Assert.Equal(methods, type.VirtualMethods);
        Assert.Equal(slots, type.VTableSlots);
        Assert.Equal(declarations, type.FieldDeclarationsByName);
    }

    [Theory]
    [InlineData("class Base { public int field; } class Broken : Base { public int own; public override int Missing()=>0; }", "does not override")]
    [InlineData("class Base { public int field; } class Broken : Base { public int field; }", "already declared in a base class")]
    [InlineData("interface I { void Read(); } abstract class Base : I { public int field; public abstract void Read(); } class Broken : Base { public int own; }", "must implement abstract")]
    [InlineData("class Broken : Other {} class Other : Broken {}", "Circular inheritance")]
    public void FailedResolutionCanBeRetriedWithoutCorruptingMetadata(string declaration, string expected)
    {
        var (_, checker) = CompilerTestHelper.CheckDiagnostics(declaration + " int main()=>0;");
        var type = checker.GetClass("Broken")!;
        var fields = type.Fields.ToArray();
        var slots = type.VTableSlots.ToArray();
        var methods = type.Methods.ToArray();
        var virtualMethods = type.VirtualMethods.ToArray();
        var interfaces = type.Interfaces.ToArray();
        int destructorSlot = type.DestructorSlot;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var error = Assert.Throws<TypeCheckException>(() =>
                new HierarchyResolutionPass(checker.Symbols).ResolveHierarchy(type));
            Assert.Contains(expected, error.Description);
            Assert.Equal(fields, type.Fields);
            Assert.Equal(slots, type.VTableSlots);
            Assert.Equal(methods, type.Methods);
            Assert.Equal(virtualMethods, type.VirtualMethods);
            Assert.Equal(interfaces, type.Interfaces);
            Assert.Equal(destructorSlot, type.DestructorSlot);
        }
    }
}
