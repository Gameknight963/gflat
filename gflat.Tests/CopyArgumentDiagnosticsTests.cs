using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class CopyArgumentDiagnosticsTests
{
    private const string Declarations = """
        struct Resource
        {
            public Resource() {}
            ~Resource() {}
        }
        interface IReceiver { public void Take(Resource value); }
        struct Receiver : IReceiver { public void Take(Resource value) {} }
        void Consume(Resource value) {}
        """;

    [Theory]
    [InlineData("sink.Take(value);")]
    [InlineData("Consume(value);")]
    [InlineData("void(Resource)* consume = Consume; consume(value);")]
    [InlineData("IReceiver* receiver = &sink; receiver.Take(value);")]
    public void NoncopyableArgumentsExplainTheCopyRequirement(string call)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Declarations +
            "\nvoid Read() { Receiver sink = new(); Resource value = new(); " + call + " }", options: new() { NoStdLib = true }));
        Assert.Contains("Resource", error.Description);
        Assert.Contains("does not support copying", error.Description);
    }

    [Fact]
    public void InaccessibleCopiesExplainTheAccessFailure()
    {
        string declarations = Declarations.Replace("public Resource() {}", "public Resource() {} private copy Resource(immutable(Resource)* source) {}");
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations +
            "\nvoid Read() { Receiver sink = new(); Resource value = new(); sink.Take(value); }", options: new() { NoStdLib = true }));
        Assert.Contains("copy operation", error.Description);
        Assert.Contains("not accessible", error.Description);
    }

    [Fact]
    public void DifferentArgumentTypesRetainTheirTypeMismatch()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Declarations +
            "\nvoid Read() { Receiver sink = new(); sink.Take(42); }", options: new() { NoStdLib = true }));
        Assert.Contains("cannot pass 'int' as 'Resource'", error.Description);
        Assert.DoesNotContain("copy", error.Description);
    }

    [Fact]
    public void FreshAndMovedArgumentsRemainAccepted()
        => Compiler.Check(Declarations +
            "\nvoid Read() { Receiver sink = new(); sink.Take(new Resource()); Resource value = new(); sink.Take(move value); }",
            options: new() { NoStdLib = true });
}
