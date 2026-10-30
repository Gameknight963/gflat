using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class QualifiedExtensionReceiverTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QualifiedGenericReceiverInfersAcrossDeclarationAndCallerFiles(int optimization)
    {
        SourceFile[] sources=[
            new("types.gf","namespace Models {struct Box<T>{public T Value;public Box(T value){Value=value;}}}"),
            new("extension.gf","namespace Helpers {T Read<T>(Models::Box<T> self)=>self.Value;\nextension<T> Models::Box<T> { public T Read() =>(*this).Value; }}"),
            new("main.gf","using Helpers;int main(){Models::Box<int> box=new(42);return box.Read();}")];
        Assert.Equal(42,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NestedNamespaceReceiverPreservesRefAndImmutableProtection(int optimization)
    {
        SourceFile[] sources=[
            new("types.gf","namespace Models::Containers {struct Box<T>{public T Value;public Box(T value){Value=value;}}}"),
            new("extension.gf","namespace Helpers {T Read<T>(ref immutable(Models::Containers::Box<T>) self)=>self.Value;\nextension<T> Models::Containers::Box<T> { public immutable T Read() =>(*this).Value; }}"),
            new("main.gf","using Helpers;int main(){Models::Containers::Box<int> box=new(42);immutable(Models::Containers::Box<int>)* view=&box;return view.Read();}")];
        Assert.Equal(42,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RelativeNamespaceUsesExtensionDeclarationInsteadOfCallerNamespace(int optimization)
    {
        SourceFile[] sources=[
            new("types.gf","namespace Library {namespace Models {struct Box<T>{public T Value;public Box(T value){Value=value;}}}} namespace Models {struct Box<T>{public T Value;}}"),
            new("extension.gf","namespace Library::Helpers {T Read<T>(Models::Box<T> self)=>self.Value;\nextension<T> Models::Box<T> { public T Read() =>(*this).Value; }}"),
            new("main.gf","using Library::Helpers;int main(){Library::Models::Box<int> box=new(42);return box.Read();}")];
        Assert.Equal(42,CompilerTestHelper.Run(sources,optimization).ExitCode);
    }

    [Fact]
    public void RelativeNamespaceDoesNotAcceptCallersSameNamedGenericType()
    {
        SourceFile[] sources=[
            new("types.gf","namespace Library {namespace Models {struct Box<T>{public T Value;}}} namespace Models {struct Box<T>{public T Value;}}"),
            new("extension.gf","namespace Library::Helpers {T Read<T>(Models::Box<T> self)=>self.Value;\nextension<T> Models::Box<T> { public T Read() =>(*this).Value; }}"),
            new("main.gf","using Library::Helpers;int main(){Models::Box<int> box=new();return box.Read();}")];
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(sources));
        Assert.Contains("no field or method 'Read'",error.Message);
    }

    [Fact]
    public void QualifiedConcreteReceiverAndQualifiedAliasKeepExistingBehavior()
    {
        SourceFile[] sources=[
            new("types.gf","namespace Models {struct Box<T>{public T Value;public Box(T value){Value=value;}} alias Selected=Box<int>;}"),
            new("extension.gf","namespace Helpers {int Read(Models::Box<int> self)=>self.Value;\nextension Models::Box<int> { public int Read() =>(*this).Value; }int Plus<T>(Models::Selected self,T extra)=>self.Value+sizeof(T);\nextension Models::Selected { public int Plus<T>(T extra) =>(*this).Value+sizeof(T); }}"),
            new("main.gf","using Helpers;int main(){Models::Box<int> box=new(42);return box.Read()+box.Plus((byte)1);}")];
        Assert.Equal(85,CompilerTestHelper.Run(sources).ExitCode);
    }

    [Theory]
    [InlineData("struct Models {public struct Box<T>{public T Value;}}")]
    [InlineData("alias Models=Holder;")]
    public void DeclarationScopeTypeOrAliasDoesNotBecomeAnUnrelatedGlobalNamespace(string shadow)
    {
        SourceFile[] sources=[
            new("types.gf","namespace Models {struct Box<T>{public T Value;}} struct Holder {public struct Box<T>{public T Value;}}"),
            new("extension.gf",$"namespace Library {{{shadow} namespace Helpers {{T Read<T>(Models::Box<T> self)=>self.Value;\nextension<T> Models::Box<T> {{ public T Read() =>(*this).Value; }}}}}}"),
            new("main.gf","using Library::Helpers;int main(){Models::Box<int> box=new();return box.Read();}")];
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(sources));
        Assert.Contains("no field or method 'Read'",error.Message);
    }

    [Fact]
    public void GenericParentTypeIsNotMistakenForANamespace()
    {
        SourceFile[] sources=[
            new("types.gf","struct Holder<T>{public struct Box {public T Value;public Box(T value){Value=value;}}}"),
            new("extension.gf","namespace Helpers {T Read<T>(Holder<T>::Box self)=>self.Value;\nextension<T> Holder<T>::Box { public T Read() =>(*this).Value; }}"),
            new("main.gf","using Helpers;int main(){Holder<int>::Box box=new(42);return box.Read();}")];
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(sources));
        Assert.Contains("no field or method 'Read'",error.Message);
    }

    [Fact]
    public void ReadonlyReceiverCannotCallQualifiedMutableReferenceExtension()
    {
        SourceFile[] sources=[
            new("types.gf","namespace Models {struct Box<T>{public T Value;}}"),
            new("extension.gf","namespace Helpers {void Set<T>(ref Models::Box<T> self,T value){self.Value=value;}\nextension<T> Models::Box<T> { public void Set(T value) {(*this).Value=value;} }}"),
            new("main.gf","using Helpers;int main(){Models::Box<int> box=new();readonly(Models::Box<int>)* view=&box;view.Set(42);return 0;}")];
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit(sources));
        Assert.Contains("no field or method 'Set'",error.Message);
    }
}
