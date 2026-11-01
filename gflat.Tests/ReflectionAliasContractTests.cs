using gflat.CompileExceptions;
using gflat.ast;
using System.Collections;
using System.Reflection;

namespace gflat.Tests;

public sealed class ReflectionAliasContractTests
{
    private const string Providers = """
        static interface IReader { int Read(); }
        static class Reader : IReader {
            public static int Read() => 42;
            public static int Extra() => 7;
        }
        constrained alias Selected : IReader = Reader;
        """;

    public static IEnumerable<object[]> HiddenCalls()
    {
        string[] bodies = [
            "const reflection method=reflect(T::Extra); return [:method:]();",
            "static if (true) { const reflection method=reflect(T::Extra); return [:method:](); } return 0;",
            "static foreach (reflection method in reflect(T).Methods) { static if (method.Name == \"Extra\") { return [:method:](); } } return 0;",
            "const reflection method=Hidden<T>(); return [:method:]();",
            "const reflection method=reflect(immutable(T)::Extra); return [:method:]();"
        ];
        foreach (bool concreteFirst in new[] { false, true })
            foreach (string body in bodies) yield return [concreteFirst, body];
    }

    [Theory]
    [MemberData(nameof(HiddenCalls))]
    public void ReflectionDoesNotBypassContractViewsThroughCachedBodies(bool concreteFirst, string body)
    {
        string source = Providers + $$"""
            const reflection Hidden<T>() => reflect(T::Extra);
            int Use<T>() { {{body}} }
            int main()
            {
                {{(concreteFirst ? "Use<Reader>();" : "")}}
                return Use<Selected>();
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedExpansionCallSpansCannotHideAnEarlierUnsafeDependency(bool concreteFirst)
    {
        string source = Providers + $$"""
            struct Unsafe { public int Invoke<P>() => P::Extra(); }
            struct Safe { public int Invoke<P>() => P::Read(); }
            struct Pair { public Unsafe first; public Safe second; }
            int Use<T>()
            {
                Pair pair = new();
                static foreach (reflection field in reflect(Pair).Fields)
                {
                    pair.[:field:].Invoke<T>();
                }
                return 0;
            }
            int main()
            {
                {{(concreteFirst ? "Use<Reader>();" : "")}}
                return Use<Selected>();
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectingHiddenMethodMetadataDoesNotGrantOrRequireCallAccess(bool concreteFirst)
    {
        Compiler.Check(Providers + $$"""
            const reflection Hidden<T>() => reflect(T::Extra);
            int Inspect<T>()
            {
                const reflection method = Hidden<T>();
                static if (method.Name != "Extra") { wrongMetadata(); }
                return 0;
            }
            int main()
            {
                {{(concreteFirst ? "Inspect<Reader>();" : "")}}
                return Inspect<Selected>();
            }
            """, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("class", false, false)]
    [InlineData("class", true, false)]
    [InlineData("class", false, true)]
    [InlineData("class", true, true)]
    [InlineData("struct", false, false)]
    [InlineData("struct", true, false)]
    [InlineData("struct", false, true)]
    [InlineData("struct", true, true)]
    public void GenericTypeMethodsRetainTheOwnerContractView(string kind, bool concreteFirst, bool genericMethod)
    {
        string method = genericMethod ? "Read<U>" : "Read";
        string invocation = genericMethod ? "Read<int>()" : "Read()";
        string source = Providers + $$"""
            {{kind}} Box<T>
            {
                public int {{method}}()
                {
                    const reflection method = reflect(T::Extra);
                    return [:method:]();
                }
            }
            int main()
            {
                {{(concreteFirst ? "Box<Reader> concrete = new(); concrete." + invocation + ";" : "")}}
                Box<Selected> restricted = new();
                return restricted.{{invocation}};
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericOwnerConstHelperCannotReturnAnUnrestrictedCachedHandle(bool concreteFirst)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + $$"""
            static class Box<T>
            {
                public static const reflection Hidden<U>() => reflect(T::Extra);
                public static int Read<U>()
                {
                    const reflection method = Hidden<U>();
                    return [:method:]();
                }
            }
            int main()
            {
                {{(concreteFirst ? "Box<Reader>::Read<int>();" : "")}}
                return Box<Selected>::Read<int>();
            }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticConditionChecksTheCanonicalSelectedOverload(bool concreteFirst)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check($$"""
            static interface IProbe { bool Probe(long value); }
            static class Probe : IProbe
            {
                public static const bool Probe(long value) => false;
                public static const bool Probe(int value) => true;
            }
            constrained alias Selected : IProbe = Probe;
            int Read<T>()
            {
                static if (T::Probe(1)) { return 42; }
                else { return 7; }
            }
            int main()
            {
                {{(concreteFirst ? "Read<Probe>();" : "")}}
                return Read<Selected>();
            }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedGenericMethodsRetainBaseArgumentViews(bool concreteFirst)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + $$"""
            class Base<T>
            {
                public int Read<U>() { const reflection method=reflect(T::Extra);return [:method:](); }
            }
            class Derived<T> : Base<T> {}
            int main()
            {
                {{(concreteFirst ? "Derived<Reader> plain=new();plain.Read<int>();" : "")}}
                Derived<Selected> restricted=new();
                return restricted.Read<int>();
            }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void NongenericDerivedTypeRetainsItsConstrainedBaseArguments()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + """
            class Base<T> { public int Read<U>() { const reflection method=reflect(T::Extra);return [:method:](); } }
            class Derived : Base<Selected> {}
            int main() { Derived value=new();return value.Read<int>(); }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void AWrapperWithoutReflectionSyntaxStillChecksQualifiedArgumentViews()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + """
            struct Box<T> { public int Read() { const reflection method=reflect(T::Extra);return [:method:](); } }
            int Wrapper<T>() { Box<immutable(T)> value=new();return value.Read(); }
            int main() { Wrapper<Reader>();return Wrapper<Selected>(); }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData("class Box<T>{public Box(){const reflection method=reflect(T::Extra);[:method:]();}}")]
    [InlineData("struct Box<T>{~Box(){const reflection method=reflect(T::Extra);[:method:]();}}")]
    [InlineData("class Box<T>{public static int Value=[:reflect(T::Extra):]();}")]
    [InlineData("struct Box<T>{public static int operator +(Box<T> left,Box<T> right){const reflection method=reflect(T::Extra);return [:method:]();}}")]
    public void OtherGenericTypeBodiesDoNotBypassAliasContracts(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + declaration +
            "int main(){Box<Reader> plain=new();Box<Selected> restricted=new();return 0;}", options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void GenericOperatorPrivateProofDoesNotConflictWithItsCanonicalDeclaration()
    {
        Compiler.Check(Providers + """
            struct Box<T>
            {
                public static int operator +(Box<T> left, Box<T> right)
                {
                    const reflection method = reflect(T::Read);
                    return [:method:]();
                }
            }
            int main()
            {
                Box<Reader> plain = new();
                Box<Selected> restricted = new();
                return restricted + restricted;
            }
            """, options: new() { NoStdLib = true });
    }

    [Fact]
    public void GenericOperatorPrivateProofPreservesActualDuplicateErrors()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + """
            struct Box<T>
            {
                public static int operator +(Box<T> left, Box<T> right) => 1;
                public static int operator +(Box<T> left, Box<T> right) => 2;
            }
            int main() { Box<Selected> value = new(); return value + value; }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("Operator overloads cannot differ", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerProofUsesTheSelectedReplacementBody(bool unsafeReplacement)
    {
        string unsafeBody = "{const reflection method=reflect(T::Extra);return [:method:]();}";
        string safeBody = "=>42;";
        string source = Providers + $$"""
            static class Box<T> { public weak static int Read() {{(unsafeReplacement ? safeBody : unsafeBody)}} }
            public replace static int Box::Read() {{(unsafeReplacement ? unsafeBody : safeBody)}}
            int main()=>Box<Selected>::Read();
            """;
        if (unsafeReplacement)
        {
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
            Assert.Contains("not exposed", error.Message);
        }
        else Compiler.Check(source, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("class", false)]
    [InlineData("class", true)]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    public void NestedNongenericTypesRetainTheirGenericOwnerView(string kind, bool concreteFirst)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Providers + $$"""
            class Outer<T>
            {
                public {{kind}} Nested
                {
                    public int Read()
                    {
                        const reflection method = reflect(T::Extra);
                        return [:method:]();
                    }
                }
            }
            int main()
            {
                {{(concreteFirst ? "Outer<Reader>::Nested plain=new();plain.Read();" : "")}}
                Outer<Selected>::Nested restricted = new();
                return restricted.Read();
            }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void PrivateNestedConstructorSignaturesResolveTheirEnclosingEntryType()
    {
        Compiler.Check(Providers + """
            struct Span<T>
            {
                private T* data;
                public Span(T* value) { data = value; }
            }
            struct Outer<T>
            {
                public struct Entry { public int Value; }
                public struct Enumerator
                {
                    private Span<Entry> entries;
                    public Enumerator(Span<Entry> value) { entries = value; }
                }
            }
            int main()
            {
                Outer<Reader> plain = new();
                Outer<Selected> restricted = new();
                return 0;
            }
            """, options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void RepeatedCompletedTypeViewsReuseTheirPrivateProof(string kind)
    {
        var (_, checker) = Compiler.Check(Providers + $$"""
            {{kind}} Box<T>
            {
                public int Read()
                {
                    const reflection method = reflect(T::Read);
                    return [:method:]();
                }
            }
            int main()
            {
                Box<Reader> plain = new();
                Box<Selected> restricted = new();
                return restricted.Read();
            }
            """, options: new() { NoStdLib = true });
        var ownerViews = Assert.IsAssignableFrom<IDictionary>(typeof(TypeChecker)
            .GetField("reflectionOwnerViews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(checker));
        int checkedMembers = ownerViews.Count;
        Assert.True(checkedMembers > 0);
        var original = checker.ResolveAlias(View());
        for (int i = 0; i < 8; i++)
        {
            var repeated = checker.ResolveAlias(View());
            Assert.Equal(((NamedTypeExpression)original).Name, ((NamedTypeExpression)repeated).Name);
            Assert.Equal(checkedMembers, ownerViews.Count);
        }

        static NamedTypeExpression View() => new("Box", null, 1, [new NamedTypeExpression("Selected", null, 1)]);
    }
}
