using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NamespaceFunctionAccessTests
{
    [Theory]
    [InlineData("namespace Lib{private int Hidden()=>42;}", "int Read()=>Lib::Hidden();")]
    [InlineData("namespace Lib{private int Hidden()=>42;}", "using Lib;int Read()=>Hidden();")]
    [InlineData("private int Hidden()=>42;", "int Read()=>global::Hidden();")]
    [InlineData("namespace Lib{private int Hidden()=>42;}", "int Read(){int()* callback=&Lib::Hidden;return callback();}")]
    [InlineData("namespace Lib{private int Hidden()=>42;}", "int Read(){int()* callback=Lib::Hidden;return callback();}")]
    [InlineData("namespace Lib{private T Hidden<T>(T value)=>value;}", "int Read()=>Lib::Hidden<int>(42);")]
    [InlineData("namespace Lib{private T Hidden<T>(T value)=>value;}", "using Lib;int Read()=>Hidden(42);")]
    [InlineData("namespace Lib{private int Hidden(int value)=>value;\nextension int { private int Hidden() =>(*this); }}", "using Lib;int Read(){int value=42;return value.Hidden();}")]
    [InlineData("namespace Lib{public long Hidden(long value)=>value;private int Hidden(int value)=>value;}", "int Read()=>Lib::Hidden(42);")]
    [InlineData("namespace Lib{public long Hidden(long value)=>value;private int Hidden(int value)=>value;}", "int Read(){int(int)* callback=Lib::Hidden;return callback(42);}")]
    [InlineData("namespace Lib{public long Hidden(long value)=>value;private int Hidden(int value)=>value;}", "int Read(){int(int)* callback=&Lib::Hidden;return callback(42);}")]
    [InlineData("namespace Lib{private int Hidden()=>42;}", "void Read(){Lib::Hidden;}")]
    [InlineData("namespace Lib{private int Hidden()=>42;}", "void Read(){(&Lib::Hidden)();}")]
    public void SelectedPrivateNamespaceFunctionsAreRestrictedToTheirDeclaringFile(string library, string consumer)
    {
        var sources = Sources(library, consumer);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(sources, options: new() { NoStdLib = true }));
        Assert.StartsWith("Namespace function '", error.Description);
        Assert.Contains("Hidden", error.Description);
        Assert.EndsWith("' is private to its source file", error.Description);
        Assert.DoesNotContain("$", error.Description);
        Assert.Equal(sources[1].Id, error.Span.SourceId);
        Assert.True(error.Span.Length > 0);
        Assert.Single(Compiler.Analyze(sources, new() { NoStdLib = true }), d => d.Message == error.Description);
    }

    [Theory]
    [InlineData("namespace Lib{private int Hidden(int value)=>value;public long Hidden(long value)=>value;}", "long Read()=>Lib::Hidden((long)42);")]
    [InlineData("namespace Lib{private int Hidden(int value)=>value;public long Hidden(long value)=>value;}", "long Read(){long(long)* callback=Lib::Hidden;return callback(42);}")]
    [InlineData("namespace Lib{private int Hidden(int value)=>value;public long Hidden(long value)=>value;}", "long Read(){long(long)* callback=&Lib::Hidden;return callback(42);}")]
    [InlineData("namespace Lib{private int Hidden()=>42;public int()* Callback()=>Hidden;}", "int Read(){int()* callback=Lib::Callback();return callback();}")]
    [InlineData("namespace Lib{private int Hidden()=>42;}namespace Lib{public int Read()=>Hidden();}", "int Read()=>Lib::Read();")]
    [InlineData("class Holder{private static int Hidden()=>42;public static int Read()=>Hidden();}", "int Read()=>Holder::Read();")]
    [InlineData("struct Holder{private static int Hidden()=>42;public static int Read()=>Hidden();}", "int Read()=>Holder::Read();")]
    [InlineData("class Holder<T>{private static T Hidden(T value)=>value;public static T Read(T value)=>Hidden(value);}", "int Read()=>Holder<int>::Read(42);")]
    public void AccessUsesTheSelectedNamespaceDeclarationAndAllowsIntentionalCallbackExports(string library, string consumer)
        => Compiler.Check(Sources(library, consumer), options: new() { NoStdLib = true });

    [Fact]
    public void SingleSourceStringInputRetainsPrivateFunctionCallsAndAddresses()
        => Compiler.Check("private int Hidden()=>42;int Read(){int()* callback=&Hidden;return Hidden()+callback();}", options: new() { NoStdLib = true });

    [Theory]
    [InlineData("int Read()=>Lib::Hidden<int>(42);")]
    [InlineData("using Lib;int Read()=>Hidden(42);")]
    public void SpecializedPrivateFunctionDiagnosticsUseTheirRegisteredDisplayName(string consumer)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            Sources("namespace Lib{private T Hidden<T>(T value)=>value;}", consumer), options: new() { NoStdLib = true }));
        Assert.Equal("Namespace function 'Lib::Hidden<int>' is private to its source file", error.Description);
    }

    private static SourceFile[] Sources(string library, string consumer)
        => [new SourceFile("private-functions-library.gf", library), new SourceFile("private-functions-consumer.gf", consumer)];
}
