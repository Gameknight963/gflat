using gflat.diagnostics;

namespace gflat.Tests;

public sealed class PropertyDiagnosticProvenanceTests
{
    [Theory]
    [InlineData("struct Entry{public readonly(int)* Value{get;set;}} int main(){Entry value=new Entry();return 0;}", "Property 'Entry.Value' requires an initializer or constructor assignment")]
    [InlineData("class Entry{public readonly(int)* Value{get;set;}} int main(){Entry value=new Entry();return 0;}", "Property 'Entry.Value' requires an initializer or constructor assignment")]
    [InlineData("namespace Lib{struct Entry{public readonly(int)* Value{get;set;}}} int main(){Lib::Entry value=new Lib::Entry();return 0;}", "Property 'Lib::Entry.Value' requires an initializer or constructor assignment")]
    [InlineData("namespace Lib{class Entry<T>{public immutable(T)* Value{get;set;}}} int main(){Lib::Entry<int> value=new Lib::Entry<int>();return 0;}", "Property 'Lib::Entry<int>.Value' requires an initializer or constructor assignment")]
    [InlineData("namespace Lib{class Family<T>{public struct Entry{public immutable(T)* Value{get;set;}}}} int main(){Lib::Family<int>::Entry value=new Lib::Family<int>::Entry();return 0;}", "Property 'Lib::Family<int>::Entry.Value' requires an initializer or constructor assignment")]
    [InlineData("namespace Lib{struct Entry{public int* field;}} int main(){Lib::Entry value=new Lib::Entry();return 0;}", "Field 'Lib::Entry.field' requires an initializer or constructor assignment")]
    [InlineData("struct Entry{public readonly(int)* Value{get;set;}public Entry(){}}", "Property 'Value' must be initialized before use or constructor completion")]
    [InlineData("class Entry{public readonly(int)* Value{get;set;}public Entry(){}}", "Property 'Value' must be initialized before use or constructor completion")]
    [InlineData("namespace Lib{struct Entry<T>{public immutable(T)* Value{get;set;}public Entry(){}}} int main(){Lib::Entry<int> value=new Lib::Entry<int>();return 0;}", "Property 'Value' must be initialized before use or constructor completion")]
    [InlineData("struct Entry{public readonly(int)* Value{get;set;}public Entry(int* pointer){int n=*Value;Value=pointer;}}", "Property 'Value' must be initialized before use or constructor completion")]
    [InlineData("struct Inner{public readonly(int)* Value{get;set;}}struct Outer{public Inner child;public Outer(){}}", "Property 'child.Value' must be initialized before use or constructor completion")]
    [InlineData("struct Inner{public readonly(int)* pointer;}struct Outer{public Inner Value{get;set;}public Outer(){}}", "Field 'Value.pointer' must be initialized before use or constructor completion")]
    [InlineData("struct Entry{public int* field;public Entry(){}}", "Field 'field' must be initialized before use or constructor completion")]
    [InlineData("namespace App; public int* Value{get;set;} int main()=>0;", "Static or global property 'Value' requires an initializer")]
    [InlineData("class App{public static int* Value{get;set;}} int main()=>0;", "Static or global property 'Value' requires an initializer")]
    [InlineData("class App<T>{public static T* Value{get;set;}} App<int>*? unused; int main()=>0;", "Static or global property 'Value' requires an initializer")]
    [InlineData("class App{public static int* field;} int main()=>0;", "Static or global field 'field' requires an initializer")]
    public void RequiredMembersUseSourcePropertyNamesAndLeafCategories(string text, string expected)
    {
        var source = new SourceFile("property-provenance.gf", text);
        var error = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain('$', error.Message);
        Assert.Equal(source.Path, error.FilePath);
        Assert.True(error.Span.Length > 0);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void GenericConstructorPropertyAssignmentsStillSatisfyInitialization(string kind)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            namespace Lib {
                {{kind}} Entry<T> {
                    public immutable(T)* Value { get; }
                    public Entry(T* pointer) { Value=pointer; }
                }
            }
            int main(){int number=7;Lib::Entry<int> entry=new Lib::Entry<int>(&number);return *entry.Value-7;}
            """).ExitCode);

    [Fact]
    public void NestedPropertyAssignmentsStillUseOriginalStoragePaths()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Inner{public readonly(int)* Value{get;}public Inner(int* pointer){Value=pointer;}}
            struct Outer{public Inner child;public Outer(int* pointer){child=new Inner(pointer);}}
            int main(){int number=7;Outer value=new Outer(&number);return *value.child.Value-7;}
            """).ExitCode);
}
