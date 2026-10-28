namespace gflat.Tests;

public sealed class GenericMethodTypeNameCollisionTests
{
    [Theory]
    [InlineData("struct", false, false)]
    [InlineData("struct", false, true)]
    [InlineData("struct", true, false)]
    [InlineData("struct", true, true)]
    [InlineData("class", false, false)]
    [InlineData("class", false, true)]
    [InlineData("class", true, false)]
    [InlineData("class", true, true)]
    public void MethodNamesDoNotRenameUnrelatedTypes(string kind, bool genericType, bool staticMethod)
    {
        foreach (bool genericOwner in new[] { false, true })
            Compiler.Check(Collision("Library", kind, genericType, staticMethod, genericOwner) + "int main()=>Library::Run();",
                options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("class", false)]
    [InlineData("class", true)]
    public void TypeNamesDoNotRenameOrCloseSameNamedGenericMethods(string kind, bool shadowParameter)
    {
        foreach (string ns in new[] { "", "Library" })
            Compiler.Check(Symmetric(ns, kind, shadowParameter) + "int main()=>" + (ns.Length == 0 ? "" : ns + "::") + "Run();",
                options: new() { NoStdLib = true });
    }

    [Fact]
    public void RecursiveMethodAndNestedTypeReferencesKeepTheirIdentity()
        => Compiler.Check(Additional + "int main(){if(Recursive::Run()!=42)return 1;return Nested::Run();}",
            options: new() { NoStdLib = true });

    [Fact]
    public void OriginalGlobalTypeAndRenamedMethodControlBothCompile()
    {
        const string source = "struct Box<T>{public T value;} static class Helpers{public static Box<T> METHOD<T>(){return new Box<T>();}} int main(){var item=Helpers::METHOD<int>();return 0;}";
        foreach (string name in new[] { "Box", "Make" })
            Compiler.Check(source.Replace("METHOD", name, StringComparison.Ordinal), options: new() { NoStdLib = true });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CollisionAndShadowingPacketRuns(int optimization)
    {
        var declarations = new List<string>();
        var calls = new List<string>();
        void Add(string text, string ns)
        {
            declarations.Add(text);
            calls.Add($"if({ns}::Run()!=42)return {calls.Count + 1};");
        }
        foreach (string kind in new[] { "struct", "class" })
            foreach (bool genericType in new[] { false, true })
                foreach (bool staticMethod in new[] { false, true })
                    foreach (bool genericOwner in new[] { false, true })
                    {
                        string ns = "Case" + calls.Count;
                        Add(Collision(ns, kind, genericType, staticMethod, genericOwner), ns);
                    }
        foreach (string kind in new[] { "struct", "class" })
            foreach (bool shadowParameter in new[] { false, true })
            {
                string ns = "Case" + calls.Count;
                Add(Symmetric(ns, kind, shadowParameter), ns);
            }
        declarations.Add(Additional);
        calls.Add("if(Recursive::Run()!=42)return 21;if(Nested::Run()!=42)return 22;");
        declarations.Add(Symmetric("", "struct", false));
        calls.Add("if(Run()!=42)return 23;");
        string program = string.Join("\n", declarations) + "int main(){" + string.Join("", calls) + "return 0;}";
        var result = CompilerTestHelper.Run(program, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        Assert.Empty(result.StandardOutput);
    }

    private static string Collision(string ns, string kind, bool genericType, bool staticMethod, bool genericOwner)
    {
        string box = genericType ? "Box<T>" : "Box";
        string concrete = genericType ? "Box<int>" : "Box";
        string value = genericType ? "T" : "int";
        string helpers = genericOwner ? "Helpers<T>" : "Helpers";
        string owner = genericOwner ? "Helpers<long>" : "Helpers";
        string modifier = staticMethod ? "static " : "";
        string invocation = staticMethod ? owner + "::Box<int>(input)" : "helper.Box<int>(input)";
        // Instance generic-return binding is separate; T still appears in arguments and locals.
        string resultType = !staticMethod && genericType ? concrete : box;
        return $$"""
            namespace {{ns}}{
                {{kind}} {{box}}{
                    public {{value}} value;
                    public Box({{value}} n){value=n;}
                }
                class {{helpers}}{
                    public {{modifier}}{{resultType}} Box<T>({{box}} input){
                        {{box}} local=new {{box}}(input.value);
                        return local;
                    }
                }
                public int Run(){
                    {{concrete}} input=new {{concrete}}(42);
                    {{owner}} helper=new {{owner}}();
                    var result={{invocation}};
                    return result.value;
                }
            }
            """;
    }

    private static string Symmetric(string ns, string kind, bool shadowParameter)
    {
        string parameter = shadowParameter ? "T" : "U";
        string members = $$"""
            {{kind}} Box<T>{
                public {{parameter}} Box<{{parameter}}>({{parameter}} value){return value;}
            }
            public int Run(){Box<int> owner=new Box<int>();return (int)owner.Box<long>(42L);}
            """;
        return ns.Length == 0 ? members : "namespace " + ns + "{" + members + "}";
    }

    private const string Additional = """
        namespace Recursive{
            struct Box<T>{public T value;public Box(T n){value=n;}}
            static class Helpers{
                public static Box<T> Box<T>(T value,int depth){
                    if(depth>0)return Helpers::Box<T>(value,depth-1);
                    return new Box<T>(value);
                }
            }
            public int Run(){var result=Helpers::Box<int>(42,3);return result.value;}
        }
        namespace Nested{
            class Box{public struct Item{public int value;public Item(int n){value=n;}}}
            static class Helpers{public static Box::Item Box<T>(){return new Box::Item(42);}}
            public int Run(){var result=Helpers::Box<int>();return result.value;}
        }
        """;
}
