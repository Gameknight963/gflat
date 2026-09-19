using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

public static class Compiler
{
    public static (CompilationUnit Ast, TypeChecker Checker) Check(string source, DiagnosticBag? diagnostics = null)
    {
        diagnostics ??= new();
        CompilationUnit ast = Parser.Parse(Lexer.Tokenize(source), diagnostics);
        void Validate()
        {
            if (!diagnostics.HasErrors) return;
            Diagnostic error = diagnostics.Items.First(d => d.Severity == DiagnosticSeverity.Error);
            throw new TypeCheckException(error.Message, error.Line);
        }
        Validate();
        var checker = new TypeChecker(diagnostics);
        ast.Accept(checker);
        Validate();
        return (ast, checker);
    }

    public static string Emit(string source, DiagnosticBag? diagnostics = null)
    {
        var (ast, checker) = Check(source, diagnostics);
        var emitter = new LlvmEmitter(checker);
        ast.Accept(emitter);
        return emitter.GetOutput();
    }
}
