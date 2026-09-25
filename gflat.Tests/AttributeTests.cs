using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class AttributeTests
{
    private static TypeChecker.EvaluatedAttribute Read(string source)
    {
        var (ast, checker) = Compiler.Check(source);
        return Assert.Single(checker.GetAttributes(ast.Members.OfType<MethodDeclaration>().Single(m => m.Name == "main")));
    }

    [Fact]
    public void RunsSelectedConstructorWithConstantExpressions()
    {
        var attr = Read("""
            class Tag : Attribute {
                public int value;
                public Tag(bool b) { value = 99; }
                public Tag(int n) { value = n; }
            }
            [Tag(6 * 7)] int main() { return 0; }
            """);
        Assert.Equal(new ConstValue.Integer(42), attr.Value.Fields["value"]);
        Assert.Equal("int", Assert.IsType<NamedTypeExpression>(attr.Constructor!.Parameters[0].Type).Name);
    }

    [Fact]
    public void RunsBaseConstructorsAndFieldInitializers()
    {
        var attr = Read("""
            class BaseTag : Attribute {
                public int first = 2;
                public BaseTag(int n) { first += n; }
            }
            class Tag : BaseTag {
                public int second = first + 3;
                public bool enabled;
                public Tag(int n) : base(n) { second += 1; }
            }
            [Tag(4)] int main() { return 0; }
            """);
        Assert.Equal(new ConstValue.Integer(6), attr.Value.Fields["first"]);
        Assert.Equal(new ConstValue.Integer(10), attr.Value.Fields["second"]);
        Assert.Equal(new ConstValue.Boolean(false), attr.Value.Fields["enabled"]);
    }

    [Fact]
    public void SupportsImplicitConstructorsAndForwardDeclarations()
    {
        var attr = Read("[Tag] int main() { return 0; } class Tag : Attribute { public int value = 7; }");
        Assert.Equal(new ConstValue.Integer(7), attr.Value.Fields["value"]);
    }

    [Fact]
    public void DefaultArgumentHasTheParameterType()
    {
        var attr = Read("class Tag : Attribute { public bool value; public Tag(bool b) { value = b; } } [Tag(default)] int main() { return 0; }");
        Assert.Equal(new ConstValue.Boolean(false), attr.Value.Fields["value"]);
    }

    [Fact]
    public void ConstructorLocalsShadowFields()
    {
        var attr = Read("class Tag : Attribute { public int value = 9; public Tag(int value) { value = 3; } } [Tag(1)] int main() { return 0; }");
        Assert.Equal(new ConstValue.Integer(9), attr.Value.Fields["value"]);
    }

    [Fact]
    public void BaseConstructorCannotSeeDerivedParameters()
    {
        var attr = Read("class Base : Attribute { public int value = 9; public int result; public Base() { result = value; } } class Tag : Base { public Tag(int value) {} } [Tag(1)] int main() { return 0; }");
        Assert.Equal(new ConstValue.Integer(9), attr.Value.Fields["result"]);
    }

    [Fact]
    public void FieldInitializersCannotSeeConstructorParameters()
    {
        var attr = Read("class Tag : Attribute { public int value = 9; public int result = value; public Tag(int value) {} } [Tag(1)] int main() { return 0; }");
        Assert.Equal(new ConstValue.Integer(9), attr.Value.Fields["result"]);
    }

    [Fact]
    public void EnumArgumentsAndConstFunctionCallsWork()
    {
        var attr = Read("enum Mode { A = 7 } const int twice(int n) { return n * 2; } class Tag : Attribute { public int value; public Tag(Mode m, int n) { value = (int)m + n; } } [Tag(Mode::A, twice(3))] int main() { return 0; }");
        Assert.Equal(new ConstValue.Integer(13), attr.Value.Fields["value"]);
    }

    [Fact]
    public void StringArgumentsAreExpressions()
    {
        var attr = Read("class Tag : Attribute { public readonly(char*) text; public Tag(readonly(char)* s) { text = s; } } [Tag(\"hello\")] int main() { return 0; }");
        Assert.Equal(new ConstValue.String("hello"), attr.Value.Fields["text"]);
    }

    [Fact]
    public void MultipleUsesHaveIndependentObjects()
    {
        var (ast, checker) = Compiler.Check("class Tag : Attribute { public int value; public Tag(int n) { value = n; } } [Tag(1)] [Tag(2)] int main() { return 0; }");
        var attrs = checker.GetAttributes(ast.Members.OfType<MethodDeclaration>().Single(m => m.Name == "main"));
        Assert.Equal(2, attrs.Count);
        Assert.Equal(new ConstValue.Integer(1), attrs[0].Value.Fields["value"]);
        Assert.Equal(new ConstValue.Integer(2), attrs[1].Value.Fields["value"]);
    }

    [Fact]
    public void GenericSpecializationsKeepIndependentAttributeNodes()
    {
        var (ast, checker) = Compiler.Check("class Tag : Attribute {} [Tag] T identity<T>(T n) { return n; } int main() { return identity<int>(1); }");
        var methods = ast.Members.OfType<MethodDeclaration>().Where(m => m.Name.StartsWith("identity")).ToList();
        Assert.Equal(2, methods.Count);
        Assert.NotSame(methods[0].Attributes[0], methods[1].Attributes[0]);
        foreach (var method in methods) Assert.Single(checker.GetAttributes(method));
    }

    [Fact]
    public void CustomExceptionDoesNotSuppressAttributePrelude()
        => Read("class Exception {} class Tag : Attribute {} [Tag] int main() { return 0; }");

    [Fact]
    public void NamespaceQualifiedAttributesWork()
    {
        var attr = Read("namespace Meta { class Tag : Attribute { public int value = 8; } } [Meta::Tag] int main() { return 0; }");
        Assert.Equal(new ConstValue.Integer(8), attr.Value.Fields["value"]);
    }

    [Fact]
    public void RetainsAttributesOnFieldsAndDestructors()
    {
        var (ast, checker) = Compiler.Check("class Tag : Attribute {} [Tag] class Item { [Tag] public int value; [Tag] public Item() {} [Tag] ~Item() {} }");
        var item = ast.Members.OfType<ClassDeclaration>().Single(c => c.Name == "Item");
        Assert.Single(checker.GetAttributes(item));
        foreach (var member in item.Members) Assert.Single(checker.GetAttributes(member));
    }

    [Theory]
    [InlineData("", "[Missing]", "derived from Attribute")]
    [InlineData("class Tag {}", "[Tag]", "derived from Attribute")]
    [InlineData("struct Tag {}", "[Tag]", "derived from Attribute")]
    [InlineData("", "[Attribute]", "derived from Attribute")]
    [InlineData("class Attribute {}", "[Attribute]", "reserved prelude")]
    [InlineData("abstract class Tag : Attribute {}", "[Tag]", "abstract")]
    [InlineData("class Tag : Attribute { private Tag() {} }", "[Tag]", "inaccessible")]
    [InlineData("class Tag : Attribute { public Tag(int n) {} }", "[Tag(true)]", "No matching constructor")]
    [InlineData("class Tag : Attribute { ~Tag() {} }", "[Tag]", "destructors")]
    [InlineData("int runtime() { return 1; } class Tag : Attribute { public Tag(int n) {} }", "[Tag(runtime())]", "compile time")]
    [InlineData("extern int external(); class Tag : Attribute { public Tag() { int n = external(); } }", "[Tag]", "compile time")]
    [InlineData("class Tag : Attribute { public Tag() { while (true) {} } }", "[Tag]", "compile time")]
    [InlineData("int counter = 0; class Tag : Attribute { public Tag() { counter = 1; } }", "[Tag]", "non-local")]
    [InlineData("class Tag : Attribute { public Tag() { defer { int n = 1; } } }", "[Tag]", "not supported at compile time")]
    [InlineData("class Tag : Attribute { public Tag() { Tag other = new Tag(); } }", "[Tag]", "maximum call depth")]
    [InlineData("int counter = 0; const int read() { return counter; } class Tag : Attribute { public Tag(int counter) { int n = read(); } }", "[Tag(1)]", "compile-time constant")]
    public void RejectsInvalidAttributes(string declaration, string annotation, string error)
    {
        var ex = Assert.Throws<TypeCheckException>(() => Compiler.Check(declaration + annotation + " int main() { return 0; }"));
        Assert.Contains(error, ex.Message);
    }

    [Fact]
    public void AnnotationDoesNotCallConstructorAtRuntime()
    {
        string source = "class Tag : Attribute { public int value; public Tag(int n) { value = n; } } [Tag(42)] int main() { return 7; }";
        string ir = Compiler.Emit(source);
        Assert.Equal(Compiler.Emit(source.Replace("[Tag(42)]", "")), ir);
        Assert.Equal(7, CompilerTestHelper.Run(source).ExitCode);
    }
}
