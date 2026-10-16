using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ValueDiagnosticTypeNameTests
{
    [Theory]
    [InlineData("Library::Box<int> value = new Library::Other<int>();", "Cannot assign 'Library::Other<int>' to 'Library::Box<int>'")]
    [InlineData("Library::Box<int> value = new Library::Box<int>(); value = new Library::Other<int>();", "Cannot assign 'Library::Other<int>' to 'Library::Box<int>'")]
    [InlineData("Library::Box<int> value = new Library::Box<int>(); delete value;", "Cannot delete non-pointer type 'Library::Box<int>'")]
    [InlineData("Library::Box<int> value = new Library::Box<int>(); int[2] data; data[value] = 0;", "Array index must be an integer, got 'Library::Box<int>'")]
    [InlineData("Library::Box<int> left = new Library::Box<int>(); Library::Other<int> right = new Library::Other<int>(); (&left) - (&right);", "Cannot subtract pointers of different types 'Library::Box<int>*' and 'Library::Other<int>*'")]
    [InlineData("(bool flag)=>{if(flag)return new Library::Box<int>();return new Library::Other<int>();};", "Inconsistent return types in lambda: 'Library::Box<int>' and 'Library::Other<int>'")]
    public void ValueErrorsUseSourceTypeNames(string statement, string expected)
    {
        string text = "namespace Library { struct Box<T>{} struct Other<T>{} } int main(){" + statement + "return 0;}";
        var source = new SourceFile("value-diagnostic-type-names.gf", text);
        var diagnostic = Assert.Single(Compiler.Analyze([source], new() { NoStdLib=true }));
        Assert.Equal(expected, diagnostic.Message);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib=true }));
        Assert.Equal(expected, error.Description);
        Assert.Equal(diagnostic.Span, error.Span);
    }
}
