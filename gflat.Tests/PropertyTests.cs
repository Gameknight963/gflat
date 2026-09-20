using gflat.CompileExceptions;
using Xunit;

namespace gflat.Tests;

public class PropertyTests
{
    [Fact]
    public void ExplicitAccessorsAndAssignmentValues()
    {
        var result = CompilerTestHelper.Run("""
            struct Counter {
                int count;
                public int Count { get { return count; } set { count = value; value = 99; } }
                public int Double => count * 2;
            }
            int main() {
                Counter c = new Counter();
                int assigned = (c.Count = 4);
                int old = c.Count++;
                int next = ++c.Count;
                c.Count += 3;
                return assigned + old + next + c.Double;
            }
            """);
        Assert.Equal(32, result.ExitCode);
    }

    [Fact]
    public void AutoPropertiesInitializersAndConstructorAssignment()
    {
        var result = CompilerTestHelper.Run("""
            class Player {
                public int Id { get; }
                public int Score { get; private set; } = 3;
                public Player(int id) { Id = id; }
                public void Add(int n) { Score += n; }
            }
            int main() {
                Player p = new Player(10);
                p.Add(4);
                return p.Id + p.Score;
            }
            """);
        Assert.Equal(17, result.ExitCode);
    }

    [Fact]
    public void ReceiverEvaluatedOnceAndBeforeValue()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly char* text, ...);
            struct Counter { public int Count { get; set; } }
            Counter* receiver(Counter* c) { printf("R"); return c; }
            int value() { printf("V"); return 7; }
            int main() {
                Counter c = new Counter();
                receiver(&c).Count = value();
                receiver(&c).Count += value();
                receiver(&c).Count++;
                return c.Count;
            }
            """);
        Assert.Equal(15, result.ExitCode);
        Assert.Equal("RVRVR", result.StandardOutput);
    }

    [Fact]
    public void StaticAndWriteOnlyAccessors()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly char* text, ...);
            class Constants {
                public static int Answer => 42;
                public static int Output { set => printf("%d", value); }
            }
            int main() { Constants::Output = Constants::Answer; return Constants::Answer; }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("42", result.StandardOutput);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void GenericPropertiesAndLocalShadowing(string kind)
    {
        Assert.Equal(19, CompilerTestHelper.Run($$"""
            {{kind}} Box<T> {
                public T Item { get; set; }
                public int Shadow(int Item) => Item;
            }
            int main() { Box<int> b = new Box<int>(); b.Item = 12; return b.Item + b.Shadow(7); }
            """).ExitCode);
    }

    [Fact]
    public void VirtualAndInterfacePropertyDispatch()
    {
        Assert.Equal(34, CompilerTestHelper.Run("""
            interface ICounter { int Count { get; set; } }
            class Base { public virtual int Count { get { return 1; } set {} } }
            class Counter : Base, ICounter {
                int count;
                public override int Count { get { return count; } set { count = value; } }
            }
            int main() {
                Counter c = new Counter();
                Base* b = (Base*)&c;
                b.Count = 10;
                ICounter* i = (ICounter*)&c;
                i.Count += 7;
                return b.Count + i.Count;
            }
            """).ExitCode);
    }

    [Fact]
    public void AbstractPropertyAndReadOnlyAutoGetter()
    {
        Assert.Equal(12, CompilerTestHelper.Run("""
            abstract class Base { public abstract int Count { get; set; } }
            class Counter : Base {
                int count;
                public override int Count { get => count; set => count = value; }
            }
            struct S { public int Value { get; set; } = 5; }
            int main() {
                Counter c = new Counter(); Base* b = (Base*)&c; b.Count = 7;
                S s = new S(); readonly S* p = &s;
                return b.Count + p.Value;
            }
            """).ExitCode);
    }

    [Fact]
    public void AccessorsUseTheirOwnFilesImports()
    {
        Assert.Equal(42, CompilerTestHelper.Run([
            new SourceFile("main.gf", "int main() { Library::C c = new Library::C(); return c.Answer; }"),
            new SourceFile("library.gf", "using Helpers; namespace Library { class C { public int Answer => answer(); } }"),
            new SourceFile("helper.gf", "namespace Helpers { int answer() => 42; }")
        ]).ExitCode);
    }

    [Fact]
    public void InitializersKeepDeclarationOrder()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly char* text, ...);
            int init(int n) { printf("%d", n); return n; }
            struct S {
                public int A = init(1);
                public int P { get; set; } = init(2);
                public int B = init(3);
                public int Q { get; } = init(4);
            }
            int main() { S s = new S(); return s.A + s.P + s.B + s.Q; }
            """);
        Assert.Equal(10, result.ExitCode);
        Assert.Equal("1234", result.StandardOutput);
    }

    [Fact]
    public void VirtualAutoPropertiesHaveDistinctBackingFields()
    {
        Assert.Equal(15, CompilerTestHelper.Run("""
            class Base { public virtual int P { get; set; } = 1; }
            class Derived : Base { public override int P { get; set; } = 2; }
            int main() { Derived d = new Derived(); Base* b = (Base*)&d; b.P = 15; return d.P; }
            """).ExitCode);
    }

    [Fact]
    public void ExplicitReadOnlyGetterCanHaveMutableSetter()
    {
        Assert.Equal(9, CompilerTestHelper.Run("""
            struct S { int n; public int P { readonly get => n; set => n = value; } }
            int main() { S s = new S(); s.P = 9; readonly S* p = &s; return p.P; }
            """).ExitCode);
    }

    [Fact]
    public void CompoundPropertyUpdateUsesUserDefinedOperator()
    {
        Assert.Equal(17, CompilerTestHelper.Run("""
            struct Number {
                public int N;
                public Number(int n) { N = n; }
                public static Number operator +(Number a, Number b) => new Number(a.N + b.N);
            }
            struct S { public Number P { get; set; } }
            int main() {
                S s = new S(); s.P = new Number(3);
                Number old = s.P;
                s.P += new Number(5);
                s.P += new Number(6);
                return old.N + s.P.N;
            }
            """).ExitCode);
    }

    [Fact]
    public void ReadOnlyPointerPropertyPreservesPointeeQualifier()
    {
        Assert.Equal(65, CompilerTestHelper.Run("""
            struct Text { public readonly char* Data => "A"; }
            int main() { Text text = new Text(); return (int)text.Data[0]; }
            """).ExitCode);
    }

    [Fact]
    public void TemporaryReceiverLivesThroughSetterAndThrowingValue()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly char* text, ...);
            struct S {
                int n;
                public int P { get { printf("G"); return n; } set { printf("S"); n = value; } }
                public ~S() { printf("D"); }
            }
            S make() { printf("N"); return new S(); }
            int fail() throws { printf("F"); throw new* Exception(); }
            int main() {
                make().P += 1;
                try { make().P = fail(); } catch { printf("C"); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("NGSDNFDC", result.StandardOutput);
    }

    [Fact]
    public void ThrowingAccessorsUseNormalExceptionCleanup()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly char* text, ...);
            struct S {
                public int P {
                    get throws { printf("G"); throw new* Exception(); }
                    set throws { printf("S"); throw new* Exception(); }
                }
            }
            int main() {
                S s = new S();
                try { int n = s.P; } catch { printf("C"); }
                try { s.P = 1; } catch { printf("C"); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GCSC", result.StandardOutput);
    }

    [Theory]
    [InlineData("struct S { public int P { get; private set; } } int main() { S s = new S(); s.P = 1; return 0; }", "private setter")]
    [InlineData("class S { public int P { get; } } int main() { S s = new S(); s.P = 1; return 0; }", "declaring constructor")]
    [InlineData("struct S { public int P => 1; } int main() { S s = new S(); s.P = 1; return 0; }", "no setter")]
    [InlineData("struct S { public int P { set {} } } int main() { S s = new S(); return s.P; }", "no getter")]
    [InlineData("struct S { public int P { get; set; } } int main() { S s = new S(); int* p = &s.P; return 0; }", "address of a property")]
    [InlineData("struct S { public int P { get; set {} } }", "either have bodies")]
    [InlineData("struct S { public int P { get; get; } }", "Duplicate get")]
    [InlineData("struct S { public static int P { get; set; } }", "static field storage")]
    [InlineData("struct S { public int P { get; public set; } }", "more restrictive")]
    [InlineData("struct S { public int P; public int P => 1; }", "Duplicate declaration")]
    [InlineData("struct S { public int P { get; set; } } int main() { S s; s.P = 1; return 0; }", "unassigned")]
    [InlineData("struct S { public int P { get; set; } } int main() { S s = new S(); readonly S* p = &s; p.P = 1; return 0; }", "readonly")]
    [InlineData("struct V { public int X; } struct S { public V P => new V(); } int main() { S s = new S(); s.P.X = 1; return 0; }", "value returned by a property")]
    [InlineData("struct S { public void P { get; set; } }", "Properties cannot")]
    [InlineData("struct V { ~V() {} } struct S { public V P { get; set; } }", "values with destructors")]
    [InlineData("struct V { public int X; } struct S { public V P => new V(); } int main() { S s = new S(); int* p = &s.P.X; return 0; }", "address of a value returned by a property")]
    [InlineData("interface I { int P { get; set; } } struct S : I { public int P { get; private set; } }", "must be public")]
    [InlineData("interface I { int P { get; set; } } class S : I { public int P { get; } }", "must be public")]
    [InlineData("class B { public int P { get; set; } } class D : B { public int P => 2; } int main() { D d = new D(); d.P = 3; return 0; }", "no setter")]
    [InlineData("interface I { public int P { get; private set; } }", "restricted accessibility")]
    [InlineData("interface I { int P { get throws; } }", "Throwing interface property")]
    [InlineData("class S { public int P { get; } public const S() { P = 7; } } const S s = new S();", "Property access is not yet supported in constant evaluation")]
    public void InvalidPropertiesAreDiagnosed(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));
        Assert.Contains(message, error.Message);
    }
}
