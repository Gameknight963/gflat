using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class NamespaceValuePrecedenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValuesAndInheritedPropertiesShadowImportedNamespacesWithoutExecutingLookupGetters(int optimization)
    {
        const string source = """
            using Root;
            int propertyReads = 0;
            namespace Root {
                namespace Text { public int Read() => 99; public const int Result = 99; }
                public struct Value {
                    public int Number;
                    public immutable int Read() => Number + 7;
                    public immutable int Result { get => Number + 7; }
                }
            }
            namespace Fields {
                public struct Holder {
                    public Value Text;
                    public int Read() => Text.Read();
                    public int ReadResult() => Text.Result;
                }
                public class Base { public Value Text; }
                public class Derived : Base {
                    public int Read() => Text.Read();
                    public int ReadResult() => Text.Result;
                }
            }
            namespace Properties {
                public class Base {
                    private Value stored;
                    public Value Text { get { propertyReads++; return stored; } }
                }
                public class Derived : Base {
                    public int Read() => Text.Read();
                    public int ReadResult() => Text.Result;
                }
                public struct Holder {
                    private Value stored;
                    public Value Text { get { propertyReads++; return stored; } }
                    public int Read() => Text.Read();
                    public int ReadResult() => Text.Result;
                }
            }
            namespace Locals {
                int Read() { Value Text = new(); return Text.Read(); }
                int ReadResult() { Value Text = new(); return Text.Result; }
                int Parameter(Value Text) => Text.Read() + Text.Result;
            }
            namespace Globals {
                Value Text;
                int Read() => Text.Read();
                int ReadResult() => Text.Result;
            }
            namespace Statics {
                public static class Holder {
                    public static Value Text;
                    public static int Read() => Text.Read();
                    public static int ReadResult() => Text.Result;
                }
            }
            namespace Lexical {
                public static class Text { public static int Read() => 31; }
                int Read() => Text::Read();
                int Plain() => 37;
            }
            int main() {
                Fields::Holder field = new();
                Fields::Derived inheritedField = new();
                Properties::Holder property = new();
                Properties::Derived inheritedProperty = new();
                Value value = new();
                if (field.Read() != 7 || field.ReadResult() != 7) return 1;
                if (inheritedField.Read() != 7 || inheritedField.ReadResult() != 7) return 2;
                if (property.Read() != 7 || property.ReadResult() != 7) return 3;
                if (inheritedProperty.Read() != 7 || inheritedProperty.ReadResult() != 7) return 4;
                if (propertyReads != 4) return 5;
                if (Locals::Read() != 7 || Locals::ReadResult() != 7 || Locals::Parameter(value) != 14) return 6;
                if (Globals::Read() != 7 || Globals::ReadResult() != 7) return 7;
                if (Statics::Holder::Read() != 7 || Statics::Holder::ReadResult() != 7) return 8;
                if (Lexical::Read() != 31 || Lexical::Plain() != 37) return 9;
                if (global::Root::Text::Read() != 99 || global::Root::Text::Result != 99) return 10;
                return 0;
            }
            """;
        var result = CompilerTestHelper.Run(source, optimization);
        Assert.True(result.ExitCode == 0,
            $"Namespace/value precedence exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData("Text.Read()")]
    [InlineData("Text.Result")]
    public void ARealNamespaceStillRequiresDoubleColon(string expression)
    {
        string source = "using Root; namespace Root { namespace Text { public int Read()=>99; public const int Result=99; } } int main()=>"
            + expression + ";";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Namespace member", error.Message);
        Assert.Contains("'::'", error.Message);
    }

    [Fact]
    public void AValueStillCannotUseNamespaceMemberSyntax()
    {
        const string source = """
            using Root;
            namespace Root {
                namespace Text { public int Read() => 99; }
                public struct Value { public int Read() => 7; }
            }
            int main() { Value Text = new(); return Text::Read(); }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Instance member", error.Message);
        Assert.Contains("'.'", error.Message);
    }
}
