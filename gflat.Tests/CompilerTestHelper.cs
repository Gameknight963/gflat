using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests
{
    public record ExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    public static class CompilerTestHelper
    {
        public static (CompilationUnit Ast, TypeChecker Checker) Check(string code)
        {
            return Compiler.Check(code);
        }

        public static (CompilationUnit Ast, TypeChecker Checker) CheckDiagnostics(string code)
        {
            DiagnosticBag bag = new DiagnosticBag();
            List<Token> tokens = Lexer.Tokenize(code);
            CompilationUnit ast = Parser.Parse(tokens, bag);
            Compilation.AddStandardLibrary(ast, bag);
            TypeChecker checker = new TypeChecker(bag);
            try
            {
                ast.Accept(checker);
            }
            catch (TypeCheckException ex)
            {
                if (!bag.HasErrors)
                {
                    bag.Report(DiagnosticRules.GF1000_GeneralTypeError, ex.Line, 0, null, ex.Message);
                }
            }
            return (ast, checker);
        }

        public static string EmitIr(string code)
        {
            (CompilationUnit ast, TypeChecker checker) = Check(code);
            LlvmEmitter emitter = new LlvmEmitter(checker);
            ast.Accept(emitter);
            return emitter.GetOutput();
        }

        public static ExecutionResult Run(string code)
        {
            return RunIr(Compiler.Emit(code, options: new() { InternalizeFunctions = true }));
        }

        public static ExecutionResult Run(IEnumerable<SourceFile> sources)
            => RunIr(Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));

        public static ExecutionResult Run(IEnumerable<SourceFile> sources, int optimizationLevel)
            => RunIr(Compiler.Emit(sources, options: new() { InternalizeFunctions = true }), optimizationLevel);

        public static ExecutionResult Run(string code, int optimizationLevel)
            => RunIr(Compiler.Emit(code, options: new() { InternalizeFunctions = true }), optimizationLevel);

        internal static ExecutionResult RunIr(string ir, int? optimizationLevel = null, int? expectedExitCode = null)
        {
            string testId = Guid.NewGuid().ToString("N");
            string tempDir = Path.Combine(Path.GetTempPath(), $"gflat_test_{testId}");
            Directory.CreateDirectory(tempDir);

            string llPath = Path.Combine(tempDir, "test.ll");
            string exePath = Path.Combine(tempDir, "test.exe");

            ExecutionResult result;
            try
            {
                File.WriteAllText(llPath, ir);

                ProcessResult compilation = NativeToolchain.Compile(llPath, exePath, optimizationLevel: optimizationLevel);
                if (compilation.ExitCode != 0)
                    throw new Exception($"clang compilation failed: {compilation.StandardError}\nIR:\n{ir}");
                ProcessResult execution = NativeToolchain.Run(exePath, []);
                result = new ExecutionResult(execution.ExitCode, execution.StandardOutput, execution.StandardError);
                if (expectedExitCode.HasValue && result.ExitCode != expectedExitCode.Value)
                    throw new InvalidOperationException(
                        $"Unexpected native exit code: actual={result.ExitCode}, expected={expectedExitCode.Value}.\n" +
                        $"stdout:\n{result.StandardOutput}\nstderr:\n{result.StandardError}");
            }
            catch (Exception error)
            {
                error.Data["CompilerTestHelperDirectory"] = tempDir;
                throw;
            }
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
            return result;
        }
    }
}
