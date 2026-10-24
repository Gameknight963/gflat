using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericContractDefaultTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void StaticProviderDefaultsComeFromTheExposedContract(bool requiredImplementation, int optimization)
    {
        string implementationDefault = requiredImplementation ? "" : "= Number";
        string concreteCall = requiredImplementation ? "Implementation::Provider::Read(2)" : "Implementation::Provider::Read()";
        var result = CompilerTestHelper.Run($$"""
            namespace Contracts {
                const int Number = 1;
                interface IProvider { static int Read(int value = Number); }
            }
            namespace Implementation {
                const int Number = 2;
                static class Provider : Contracts::IProvider {
                    public static int Read(int value {{implementationDefault}}) => value;
                }
            }
            const int Number = 3;
            weak alias Selected : Contracts::IProvider = Implementation::Provider;
            int Forward<T : Contracts::IProvider>() => T::Read();
            int main() {
                if ({{concreteCall}} != 2) return 1;
                if (Selected::Read() != 1) return 2;
                if (Forward<Implementation::Provider>() != 1) return 3;
                if (Forward<Selected>() != 1) return 4;
                if ({{concreteCall}} != 2) return 5;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void InstanceDefaultsComeFromTheGenericReceiversContract(bool requiredImplementation, int optimization)
    {
        string implementationDefault = requiredImplementation ? "" : "= Number";
        string concreteCall = requiredImplementation ? "reader.Read(2)" : "reader.Read()";
        var result = CompilerTestHelper.Run($$"""
            namespace Contracts {
                const int Number = 1;
                interface IReader { int Read(int value = Number); }
            }
            namespace Implementation {
                const int Number = 2;
                struct Reader : Contracts::IReader {
                    public int Read(int value {{implementationDefault}}) => value;
                }
            }
            const int Number = 3;
            int Forward<T : Contracts::IReader>(T reader) => reader.Read();
            int main() {
                Implementation::Reader reader = new();
                if ({{concreteCall}} != 2) return 1;
                if (Forward(reader) != 1) return 2;
                if ({{concreteCall}} != 2) return 3;
                return 0;
            }
            """, optimization);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredImplementationParametersStayRequiredForConcreteCalls(bool isStatic)
    {
        string source = isStatic ? """
            interface I { static int Read(int value = 1); }
            static class Reader : I { public static int Read(int value) => value; }
            int main() => Reader::Read();
            """ : """
            interface I { int Read(int value = 1); }
            struct Reader : I { public int Read(int value) => value; }
            int main() { Reader reader = new(); return reader.Read(); }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("expects 1", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplementationDefaultsDoNotMakeRequiredContractParametersOptional(bool isStatic)
    {
        string source = isStatic ? """
            interface I { static int Read(int value); }
            static class Reader : I { public static int Read(int value = 2) => value; }
            weak alias Selected : I = Reader;
            int main() => Selected::Read();
            """ : """
            interface I { int Read(int value); }
            struct Reader : I { public int Read(int value = 2) => value; }
            int Forward<T : I>(T reader) => reader.Read();
            int main() { Reader reader = new(); return Forward(reader); }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("No matching overload", error.Message);
    }
}
