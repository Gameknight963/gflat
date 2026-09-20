using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

public static class Compiler
{
    /// <summary>Editor analysis: expected source errors are returned as diagnostics, without invoking LLVM.</summary>
    public static IReadOnlyList<Diagnostic> Analyze(IEnumerable<SourceFile> sources)
    {
        var diagnostics = new DiagnosticBag();
        try { Check(sources, diagnostics); }
        catch (TypeCheckException) { /* Check has recorded the source error. */ }
        return diagnostics.Items.ToArray();
    }

    public static (CompilationUnit Ast, TypeChecker Checker) Check(string source, DiagnosticBag? diagnostics = null)
        => Check(new SourceFile("<input>", source), diagnostics);

    public static (CompilationUnit Ast, TypeChecker Checker) Check(SourceFile source, DiagnosticBag? diagnostics = null)
        => Check(new[] { source }, diagnostics);

    public static (CompilationUnit Ast, TypeChecker Checker) Check(IEnumerable<SourceFile> sources, DiagnosticBag? diagnostics = null)
    {
        diagnostics ??= new();
        try
        {
            var compilation = new Compilation(sources, diagnostics);
            Validate();
            var checker = new TypeChecker(diagnostics);
            compilation.Unit.Accept(checker);
            Validate();
            return (compilation.Unit, checker);
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
        void Validate()
        {
            if (!diagnostics.HasErrors) return;
            Diagnostic error = diagnostics.Items.First(d => d.Severity == DiagnosticSeverity.Error);
            throw new TypeCheckException(error.Message, error.Span);
        }
    }

    public static string Emit(string source, DiagnosticBag? diagnostics = null)
        => Emit(new[] { new SourceFile("<input>", source) }, diagnostics);

    public static string Emit(IEnumerable<SourceFile> sources, DiagnosticBag? diagnostics = null)
    {
        var (ast, checker) = Check(sources, diagnostics);
        var emitter = new LlvmEmitter(checker);
        ast.Accept(emitter);
        return emitter.GetOutput();
    }
}
