using System;
using System.Diagnostics;
using System.IO;
using gflat.ast;

namespace gflat.Tests
{
    public record ExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    public static class CompilerTestHelper
    {
        public static (CompilationUnit Ast, TypeChecker Checker) Check(string code)
        {
            var tokens = Lexer.Tokenize(code);
            var ast = Parser.Parse(tokens);
            var checker = new TypeChecker();
            ast.Accept(checker);
            return (ast, checker);
        }

        public static string EmitIr(string code)
        {
            var (ast, checker) = Check(code);
            var emitter = new LlvmEmitter(checker);
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

                string libArgs = Program.GetClangLibArgs();
                string clangArgs = string.IsNullOrEmpty(libArgs)
                    ? $"\"{llPath}\" -o \"{exePath}\""
                    : $"\"{llPath}\" -o \"{exePath}\" {libArgs}";

                var clang = new Process();
                clang.StartInfo.FileName = "clang.exe";
                clang.StartInfo.Arguments = clangArgs;
                clang.StartInfo.RedirectStandardError = true;
                clang.StartInfo.RedirectStandardOutput = true;
                clang.StartInfo.UseShellExecute = false;
                clang.Start();
                clang.WaitForExit();

                if (clang.ExitCode != 0)
                {
                    string err = clang.StandardError.ReadToEnd();
                    throw new Exception($"clang compilation failed (exit {clang.ExitCode}): {err}\nIR:\n{ir}");
                }

                var proc = new Process();
                proc.StartInfo.FileName = exePath;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.UseShellExecute = false;
                proc.Start();

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                return new ExecutionResult(proc.ExitCode, stdout, stderr);
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
