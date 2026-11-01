using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionMethodTests
{
    [Fact]
    public void IndividualOverloadsHaveDistinctIdentityAndParameterMetadata()
    {
        Check("""
            int Pick(long number, bool enabled) => 1;
            int Pick(int value) => 2;
            const reflection group = reflect(Pick);
            const reflection first = group.Overloads[0];
            const reflection second = group.Overloads[1];
            int main()
            {
                static if (first == second) { wrongIdentity(); }
                static if (first.ParameterCount != 2) { wrongCount(); }
                static if (first.Parameters[0].Name != "number") { wrongName(); }
                static if (first.Parameters[0].Type != reflect(long)) { wrongType(); }
                static if (first.Parameters[1].Type != reflect(bool)) { wrongSecondType(); }
                static if (first.Parameters[1].Position != 1) { wrongPosition(); }
                static if (first.Parameters[0].IsConst) { wrongConst(); }
                static if (first.ReturnType != reflect(int)) { wrongReturn(); }
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GroupUsesNormalRankingButIndividualOverloadKeepsItsDeclaration(int optimization)
    {
        Run("""
            int Pick(long value, int suffix = 2) => 12;
            int Pick(int value) => 7;
            const reflection group = reflect(Pick);
            const reflection first = group.Overloads[0];
            int main()
            {
                if ([:group:](1) != 7) return 1;
                if ([:first:](1) != 12) return 2;
                return 42;
            }
            """, optimization);
    }

    [Fact]
    public void IndividualOverloadCannotFallBackToAnotherSignature()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            int Pick(int value) => 7;
            int Pick(long value) => 42;
            const reflection first = reflect(Pick).Overloads[0];
            int main() { long value = 1; return [:first:](value); }
            """));
        Assert.Contains("cannot pass", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CapturedNamespaceGroupIgnoresCallerShadows(int optimization)
    {
        Run("""
            namespace Library {
                public int Pick(int value) => 42;
                public int Pick(long value) => 11;
            }
            const reflection group = reflect(Library::Pick);
            namespace Caller {
                int Pick(int value) => 7;
                int Invoke(int Pick) => [:group:](1);
            }
            int main() => Caller::Invoke(0);
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImportedGroupCanSelectFromDifferentDeclarationNamespaces(int optimization)
    {
        Run("""
            using First;
            using Second;

            namespace First { public int Pick(int value) => 42; }
            namespace Second { public int Pick(long value) => 7; }
            const reflection group = reflect(Pick);
            int main() => [:group:](1);
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RootQualifiedReflectionIgnoresLexicalShadowing(int optimization)
    {
        Run("""
            int Pick(int value) => 42;
            namespace Caller {
                int Pick(int value) => 7;
                const reflection target = reflect(global::Pick);
                public int Invoke() => [:target:](1);
            }
            int main() => Caller::Invoke();
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StaticGroupAndIndividualHandlesSelectFunctionPointersBySignature(int optimization)
    {
        Run("""
            static class Tools {
                public static int Pick(long value) => 20;
                public static int Pick(int value) => 22;
            }
            const reflection group = reflect(Tools::Pick);
            const reflection first = group.Overloads[0];
            int main()
            {
                int(int)* fromGroup = [:group:];
                int(long)* fromIndividual = [:first:];
                return fromGroup(1) + fromIndividual(1);
            }
            """, optimization);
    }

    [Fact]
    public void IndividualFunctionPointerCannotSelectSiblingOverload()
    {
        Assert.Throws<TypeCheckException>(() => Check("""
            int Pick(long value) => 20;
            int Pick(int value) => 22;
            const reflection first = reflect(Pick).Overloads[0];
            int main() { int(int)* pointer = [:first:]; return pointer(1); }
            """));
    }

    [Theory]
    [InlineData("int main()=>[:method:]();")]
    [InlineData("int main(){int()* pointer=[:method:];return pointer();}")]
    public void ReflectedMethodCannotExposeCapabilitiesHiddenByAnAlias(string invocation)
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            static interface IReader { int Read(); }
            static class Reader : IReader {
                public static int Read() => 42;
                public static int Extra() => 7;
            }
            constrained alias Selected : IReader = Reader;
            const reflection method = reflect(Selected::Extra);
            """ + invocation));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericMethodHandleUsesOrdinaryInferenceAndFunctionValueTargets(int optimization)
    {
        Run("""
            T Identity<T>(T value) => value;
            const reflection method = reflect(Identity);
            int main()
            {
                int(int)* pointer = [:method:];
                return [:method:](21) + pointer(21);
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InstanceSpliceRetainsVirtualDispatch(int optimization)
    {
        Run("""
            class Base { public virtual int Read() => 7; }
            class Derived : Base { public override int Read() => 42; }
            const reflection method = reflect(Base::Read);
            int main()
            {
                Derived value = new();
                Base* pointer = &value;
                return pointer.[:method:]();
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DerivedReceiverDoesNotRedirectReflectedNonvirtualMethod(int optimization)
    {
        Run("""
            class Base { public int Read() => 42; }
            class Derived : Base { public int Read() => 7; }
            const reflection method = reflect(Base::Read);
            int main()
            {
                Derived value = new();
                Derived* pointer = &value;
                return pointer.[:method:]();
            }
            """, optimization);
    }

    [Theory]
    [InlineData("int main()=>[:method:]();")]
    [InlineData("int main(){int()* pointer=[:method:];return pointer();}")]
    public void ReflectionDoesNotGrantPrivateMethodAccess(string invocation)
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            static class Secret { private static int Hidden() => 42; }
            const reflection method = reflect(Secret::Hidden);
            """ + invocation));
        Assert.Contains("private", error.Message);
    }

    [Fact]
    public void InstanceHandleRejectsUnrelatedReceiver()
    {
        Assert.Throws<TypeCheckException>(() => Check("""
            struct First { public int Read() => 42; }
            struct Second { public int Read() => 7; }
            const reflection method = reflect(First::Read);
            int main(){Second value=new();return value.[:method:]();}
            """));
    }

    private static void Check(string source) => Compiler.Check(source, options: new() { NoStdLib = true });

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, InternalizeFunctions = true });
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.Equal(42, result.ExitCode);
    }
}
