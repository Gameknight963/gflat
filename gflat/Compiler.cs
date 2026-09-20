using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

public static class Compiler
{
    public static (CompilationUnit Ast, TypeChecker Checker) Check(string source, DiagnosticBag? diagnostics = null)
    {
        return Check(new SourceFile("<input>", source), diagnostics);
    }

    public static (CompilationUnit Ast, TypeChecker Checker) Check(SourceFile source, DiagnosticBag? diagnostics = null)
    {
        diagnostics ??= new();
        using var context = SourceContext.Enter(source.Span(0));
        try
        {
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
        catch (TypeCheckException ex)
        {
            if (!diagnostics.HasErrors)
            {
                using var location = SourceContext.Enter(ex.Span);
                diagnostics.Report(DiagnosticRules.GF1000_GeneralTypeError, ex.Line, ex.Span.Column, ex.FilePath, ex.Description);
            }
            throw;
        }
    }

    public static string Emit(string source, DiagnosticBag? diagnostics = null)
    {
        var (ast, checker) = Check(source, diagnostics);
        var emitter = new LlvmEmitter(checker);
        ast.Accept(emitter);
        return emitter.GetOutput();
    }
}
