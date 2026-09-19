using gflat.ast;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace gflat
{
    public class Program
    {
        readonly static string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "program.gf"));

        public static string GetClangLibArgs()
        {
            string? lib = Environment.GetEnvironmentVariable("LIB");
            if (!string.IsNullOrEmpty(lib))
                return "";

            string vsBase = @"C:\Program Files\Microsoft Visual Studio";
            if (Directory.Exists(vsBase))
            {
                try
                {
                    List<string> dirs = Directory.GetDirectories(vsBase, "*", SearchOption.AllDirectories)
                        .Where(d => d.EndsWith(@"VC\Tools\MSVC"))
                        .ToList();
                    foreach (string vcTools in dirs)
                    {
                        string[] versions = Directory.GetDirectories(vcTools);
                        foreach (string v in versions.OrderByDescending(x => x))
                        {
                            string x64 = Path.Combine(v, "lib", "x64");
                            if (File.Exists(Path.Combine(x64, "libcmt.lib")))
                            {
                                return $"-Xlinker /libpath:\"{x64}\"";
                            }
                        }
                    }
                }
                catch { }
            }
            return "";
        }

        static string GetExitCodeMessage(int exitCode)
        {
            uint code = unchecked((uint)exitCode);

            return code switch
            {
                0x00000000 => "sucessful",
                0xC0000005 => "access violation",
                0xC0000006 => "in-page error",
                0xC0000017 => "not enough memory",
                0xC000001D => "illegal instruction",
                0xC0000094 => "integer division by zero",
                0xC0000095 => "integer overflow",
                0xC0000096 => "Privileged instruction",
                0xC000009A => "insufficient system resources",
                0xC0000135 => "DLL not found",
                0xC0000139 => "entry point not found",
                0xC0000142 => "DLL initialization failed",
                _ => $"unknown error (0x{code:X8})"
            };
        }

        static int Main(string[] args)
        {
            //List<Token> tokens = Lexer.Tokenize(code);
            //foreach (Token t in tokens)
            //    Console.WriteLine($"{t.Kind,-35} '{t.Text}'");
            //CompilationUnit ast = Parser.Parse(tokens);
            //AstPrinter printer = new AstPrinter();
            //ast.Accept(printer);

            diagnostics.DiagnosticBag diagnostics = new diagnostics.DiagnosticBag();
            List<Token> tokens = Lexer.Tokenize(code);
            CompilationUnit ast = Parser.Parse(tokens, diagnostics);

            if (diagnostics.HasErrors)
            {
                bool useColor = !Console.IsOutputRedirected;
                Console.Error.WriteLine(diagnostics.FormatAll(useColor));
                return 1;
            }

            TypeChecker checker = new TypeChecker(diagnostics);
            try
            {
                ast.Accept(checker);
            }
            catch (CompileExceptions.TypeCheckException)
            {
                // Handled via diagnostics reporting below
            }

            if (checker.Diagnostics.Count > 0)
            {
                bool useColor = !Console.IsOutputRedirected;
                Console.Error.WriteLine(checker.Diagnostics.FormatAll(useColor));
            }

            if (checker.Diagnostics.HasErrors)
            {
                return 1;
            }

            LlvmEmitter emitter = new LlvmEmitter(checker);
            ast.Accept(emitter);
            string ir = emitter.GetOutput();
            //Console.WriteLine(ir);
            File.WriteAllText("output.ll", ir);

            string fileName = "clang.exe";
            if (OperatingSystem.IsWindows())
            {
                string arch = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => "x64",
                    Architecture.X86 => "x86",
                    Architecture.Arm64 => "ARM64",
                    _ => throw new PlatformNotSupportedException()
                };

                string vswhere = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Microsoft Visual Studio\Installer\vswhere.exe");

                ProcessStartInfo psi = new(vswhere,
                    "-latest -requires Microsoft.VisualStudio.Component.VC.Llvm.Clang " +
                    $@"-find VC\Tools\Llvm\{arch}\bin\clang.exe")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using Process findClangProc = Process.Start(psi)!;
                string output = findClangProc.StandardOutput.ReadToEnd().Trim();
                string? clangPath = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .FirstOrDefault()
                          ?.Trim();
                if (clangPath != null) fileName = clangPath;
            }
            Process process = new Process();
            process.StartInfo.FileName = fileName;
            string libArgs = GetClangLibArgs();
            process.StartInfo.Arguments = string.IsNullOrEmpty(libArgs)
                ? "output.ll -o output.exe"
                : $"output.ll -o output.exe {libArgs}";
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine("clang failed:");
                Console.Error.WriteLine(process.StandardError.ReadToEnd());
                return 1;
            }

            Console.WriteLine("compiled successfully -> output.exe, running...");

            process = new Process();
            process.StartInfo.FileName = Path.GetFullPath("output.exe");
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit();
            Console.WriteLine($"0x{process.ExitCode:X8} {GetExitCodeMessage(process.ExitCode)}");
            return 0;
        }
    }
}
