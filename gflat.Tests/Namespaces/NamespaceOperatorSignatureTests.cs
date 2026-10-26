namespace gflat.Tests.Namespaces;

public sealed class NamespaceOperatorSignatureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string alias in new[] { "return", "parameter" })
        foreach (string operation in new[] { "binary", "unary", "compound" })
        {
            yield return [alias, operation, false, false, 0];
            foreach (bool reverse in new[] { false, true })
            foreach (int optimization in new[] { 0, 2 })
                yield return [alias, operation, true, reverse, optimization];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OperatorsBindCompleteSignaturesInTheirDeclaringOwner(
        string alias, string operation, bool early, bool reverse, int optimization)
    {
        bool returnsOwner = operation == "compound";
        bool aliasReturn = alias.EndsWith("return", StringComparison.Ordinal);
        string declarationAlias = aliasReturn
            ? "alias Word=" + (returnsOwner ? "Library::Number" : "int") + ";"
            : "alias Operand=Library::Number;";
        string resultType = aliasReturn ? "Word" : returnsOwner ? "Library::Number" : "int";
        string operandType = aliasReturn ? "Library::Number" : "Operand";
        string parameters = operandType + " left" + (operation == "unary" ? "" : ", " + operandType + " right");
        string implementation = returnsOwner
            ? "{ Library::Number result=new(); result.Value=42; return result; }"
            : "=>42;";
        string symbol = operation == "unary" ? "-" : "+";
        string library = $$"""
            namespace Library {
                {{declarationAlias}}
                struct Number {
                    public int Value;
                    public static {{resultType}} operator {{symbol}}({{parameters}}) {{implementation}}
                }
            }
            """;
        string body = operation switch
        {
            "compound" => "value+=value; return value.Value;",
            "unary" => "return -value;",
            _ => "return value+value;"
        };
        string helper = $$"""
            namespace Helpers {
                alias Word=long;
                alias Operand=long;
                struct Probe<T> {
                    public static int Check() {
                        Library::Number value=new();
                        {{body}}
                    }
                }
            }
            """;
        string client = (early ? "int Trigger(Helpers::Probe<int>* probe)=>0;\n" : "") +
            "int main()=>Helpers::Probe<int>::Check();";
        SourceFile[] sources = [
            new(reverse ? "b-library.gf" : "a-library.gf", library),
            new(reverse ? "a-helper.gf" : "b-helper.gf", helper),
            new("z-client.gf", client)
        ];
        Assert.Equal(42, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OperatorOwnerArgumentsRemainIndependentOfCallerTypeParameters(int optimization)
    {
        const string source = """
            int Trigger(Helpers::Probe<long>* probe)=>0;
            namespace Library {
                struct Number<T> {
                    public T Value;
                    public static Number<T> operator +(Number<T> left, Number<T> right)=>left;
                }
            }
            namespace Helpers {
                struct Probe<T> {
                    public static int Check() {
                        Library::Number<int> value=new();
                        value.Value=42;
                        Library::Number<int> result=value+value;
                        return result.Value;
                    }
                }
            }
            int main()=>Helpers::Probe<long>::Check();
            """;
        Assert.Equal(42, CompilerTestHelper.Run(source, optimization).ExitCode);
    }
}
