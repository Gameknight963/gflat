using gflat.diagnostics;

namespace gflat.Tests;

public sealed class StaticSignatureBindingTests
{
    [Theory]
    [InlineData("class", "", false)]
    [InlineData("class", "alias Word=long;", false)]
    [InlineData("class", "alias Word=long;", true)]
    [InlineData("struct", "", false)]
    [InlineData("struct", "alias Word=long;", false)]
    [InlineData("struct", "alias Word=long;", true)]
    public void StaticOverloadSignaturesBindBeforeCallsAndAddresses(string kind, string shadow, bool local)
    {
        string source = $$"""
            namespace Library {
                alias Word=int;
                {{kind}} Box<T> {
                    public static Word Read(Word value)=>23;
                    public static Word Read(long value)=>24;
                }
            }
            {{(local ? "" : shadow)}}
            int main() {
                {{(local ? shadow : "")}}
                int(int)* narrow=&Library::Box<int>::Read;
                int(long)* wide=&Library::Box<int>::Read;
                if (Library::Box<int>::Read(0) != 23) return 1;
                if (Library::Box<int>::Read(0L) != 24) return 2;
                return narrow(0)+wide(0L);
            }
            """;
        AssertValid(source);
        Assert.Equal(47, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentrantBodiesCanReadLaterStaticMethodSignatures(bool reverse)
    {
        const string first="public static int First()=>Other::Use<int>();";
        const string later="public static Word Later(Word value)=>value;";
        string source = $$"""
            namespace Lib {
                alias Word=int;
                class Box<T> { {{(reverse ? later+first : first+later)}} }
            }
            namespace Other {
                alias Word=long;
                int Use<U>() { int(int)* callback=&Lib::Box<int>::Later; return callback(42); }
            }
            int main()=>Lib::Box<int>::First();
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void SignatureSpecializationCanReadAnotherLaterMethodSignature()
    {
        const string source = """
            namespace Lib {
                alias Word=int;
                class Box<T> {
                    public static Other::Probe<T> Before()=>new Other::Probe<T>();
                    public static Word Later(Word value)=>value;
                }
            }
            namespace Other {
                alias Word=long;
                struct Probe<U> {
                    public static int Check() { int(int)* callback=&Lib::Box<int>::Later; return callback(42); }
                }
            }
            int main() {
                Lib::Box<int>::Before();
                return Other::Probe<int>::Check();
            }
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void RecursiveStaticMethodKeepsItsBoundSignature()
    {
        const string source = """
            namespace Lib {
                alias Word=int;
                class Box<T> {
                    public static Word Count(Word value) {
                        if(value == 0) return 0;
                        return Lib::Box<int>::Count(value-1)+1;
                    }
                }
            }
            alias Word=long;
            int main() { int(int)* callback=&Lib::Box<int>::Count; return callback(42); }
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void GenericFreeFunctionCloneBindsNamespaceAliases()
    {
        const string source = """
            namespace Lib { alias Word=int; Word Identity<T>(Word value)=>value; }
            alias Word=long;
            int main()=>Lib::Identity<int>(42);
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ReferenceParametersKeepTheirSignatureShape()
    {
        const string source = """
            namespace Lib {
                alias Word=int;
                class Box<T> { public static Word Increment(ref Word value) {value++;return value;} }
            }
            alias Word=long;
            int main() { int value=41; int(ref int)* callback=&Lib::Box<int>::Increment; return callback(ref value); }
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void OptionalParameterConstantsUseTheBoundParameterType()
    {
        const string source = """
            namespace Lib { alias Word=int; class Box<T> { public static Word Read(Word value=42)=>value; } }
            alias Word=long;
            int main()=>Lib::Box<int>::Read();
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void NullableReadonlyPointerAliasesKeepTheirShape()
    {
        const string source = """
            namespace Lib {
                alias Pointer=readonly(int)*?;
                class Box<T> { public static Pointer Identity(Pointer value)=>value; }
            }
            alias Pointer=int*;
            int main() {
                int value=42;
                readonly(int)*?(readonly(int)*?)* callback=&Lib::Box<int>::Identity;
                if(callback(null)!=null) return 1;
                return callback(&value)![0];
            }
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    private static void AssertValid(string source) => Assert.DoesNotContain(
        Compiler.Analyze([new SourceFile("static-signature.gf",source)],new(){NoStdLib=true}),
        d=>d.Severity==DiagnosticSeverity.Error);
}
