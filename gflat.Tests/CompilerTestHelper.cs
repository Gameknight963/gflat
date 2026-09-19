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
            string ir = EmitIr(code);
            string testId = Guid.NewGuid().ToString("N");
            string tempDir = Path.Combine(Path.GetTempPath(), $"gflat_test_{testId}");
            Directory.CreateDirectory(tempDir);

            string llPath = Path.Combine(tempDir, "test.ll");
            string exePath = Path.Combine(tempDir, "test.exe");

            try
            {
                File.WriteAllText(llPath, ir);

                ProcessResult compilation = NativeToolchain.Compile(llPath, exePath);
                if (compilation.ExitCode != 0)
                    throw new Exception($"clang compilation failed: {compilation.StandardError}\nIR:\n{ir}");
                ProcessResult execution = NativeToolchain.Run(exePath, []);
                return new ExecutionResult(execution.ExitCode, execution.StandardOutput, execution.StandardError);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }
    }
}
