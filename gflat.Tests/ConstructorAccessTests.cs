using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstructorAccessTests
{
    public static IEnumerable<object[]> InaccessibleCreationCases()
    {
        string[] creations = ["Secret value=new Secret(1);", "Secret* value=new* Secret(1);", "Secret^ value=new^ Secret(1);", "Secret value=new(1);", "Secret* value=new(1);", "Secret^ value=new(1);", "Secret* storage=((nuint)1).reinterpret<Secret*>();new* Secret(1) at storage;"];
        foreach(string kind in new[]{"class","struct"})
            foreach(string creation in creations)
                yield return [kind,creation];
    }

    [Theory]
    [MemberData(nameof(InaccessibleCreationCases))]
    public void PrivateConstructorIsCheckedForEveryCreationKind(string kind,string creation)
        => Reject($"{kind} Secret {{private Secret(int value){{}}}} int main(){{{creation}return 0;}}");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void UnmarkedConstructorsArePrivate(string kind)
        => Reject($"{kind} Secret {{Secret(int value){{}}}} int main(){{Secret value=new(1);return 0;}}");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void ProtectedConstructorRejectsUnrelatedCaller(string kind)
        => Reject($"{kind} Secret {{protected Secret(int value){{}}}} int main(){{Secret value=new(1);return 0;}}");

    [Fact]
    public void InaccessibleBestOverloadDoesNotFallBackToWideningPublicOverload()
        => Reject("struct Secret {private Secret(int value){} public Secret(long value){}} int main(){Secret value=new(1);return 0;}");

    [Theory]
    [InlineData("private Base(int value){}", "public Derived():base(1){}")]
    [InlineData("private Base(){}", "public Derived(){}")]
    [InlineData("private Base(){}", "")]
    [InlineData("private Base(int value=1){}", "")]
    public void BaseConstructionChecksSelectedConstructor(string baseConstructor,string derivedConstructor)
        => Reject($"class Base {{{baseConstructor}}} class Derived:Base {{{derivedConstructor}}} int main(){{Derived value=new();return 0;}}");

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SameTypeNestedAndDerivedFactoriesRetainAccess(int optimization)
    {
        var result=CompilerTestHelper.Run("""
            struct Outer<T>{
                public int Value;
                private Outer(int value){Value=value;}
                public struct Inner{
                    public int Value;
                    private Inner(int value){Value=value;}
                    public static Outer<T> MakeOuter()=>new Outer<T>(10);
                }
                public static Inner MakeInner()=>new Inner(11);
                public static Outer<T> Create()=>new Outer<T>(12);
            }
            class Base{public int Value;protected Base(int value=4){Value=value;}}
            class Derived:Base{public Derived():base(5){}}
            class ImplicitDerived:Base{}
            class Factory{public int Value;private Factory(int value){Value=value;}public static Factory Create()=>new Factory(6);}
            struct Plain{public int Value;}
            int main(){
                Outer<int> a=Outer<int>::Create();Outer<int> b=Outer<int>::Inner::MakeOuter();
                Outer<int>::Inner c=Outer<int>::MakeInner();
                Derived d=new();ImplicitDerived e=new();Factory f=Factory::Create();Plain plain=default(Plain);
                return a.Value+b.Value+c.Value+d.Value+e.Value+f.Value+plain.Value-48;
            }
            """,optimization);
        Assert.Equal(0,result.ExitCode);
    }

    [Theory]
    [InlineData("Secret", "class Secret{private Secret(){}}")]
    [InlineData("Derived", "class Base{private Base(){}} class Derived:Base{}")]
    public void DefaultStillRejectsUserConstructorsWithoutCallingThem(string name,string declaration)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check($"{declaration} int main(){{{name} value=default({name});return 0;}}"));
        Assert.DoesNotContain("inaccessible",error.Message);
        Assert.Contains("default",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static void Reject(string source)
    {
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(Sources(source)));
        Assert.Contains("constructor",error.Message);
        Assert.Contains("inaccessible",error.Message);
        Assert.Equal("constructor-access.gf",error.Span.Source?.Path);
    }

    private static IEnumerable<SourceFile> Sources(string source)
        => Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
            .Select(path=>new SourceFile(path,File.ReadAllText(path))).Append(new SourceFile("constructor-access.gf",source));
}
