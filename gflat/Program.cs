using gflat.ast;
using System.ComponentModel;
using System.Diagnostics;

namespace gflat
{
    public class Program
    {
        const string code = """
            extern int printf(char* fmt, ...);

            struct Point
            {
                int x;
                int y;
            }

            void LogMessage(char* msg)
            {
                printf("%s\n", msg);
            }

            long Add64(long a, long b)
            {
                return a + b;
            }

            namespace Math
            {
                int Double(int x)
                {
                    return x * 2;
                }
            }

            int main()
            {
                LogMessage("Top-level functions work!");

                Point p;
                p.x = 3;
                p.y = 4;
                Point* ptr = &p;
                ptr.x = 99;
                printf("Point: %d %d\n", p.x, p.y);

                long sum = Add64(1000000000L, 2000000000L);
                printf("sum: %lld\n", sum);

                int d = Math::Double(21);
                printf("double: %d\n", d);

                return 0;
            }
            """;

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
                    var dirs = Directory.GetDirectories(vsBase, "*", SearchOption.AllDirectories)
                        .Where(d => d.EndsWith(@"VC\Tools\MSVC"))
                        .ToList();
                    foreach (var vcTools in dirs)
                    {
                        var versions = Directory.GetDirectories(vcTools);
                        foreach (var v in versions.OrderByDescending(x => x))
                        {
                            string x64 = Path.Combine(v, "lib", "x64");
                            if (File.Exists(Path.Combine(x64, "libcmt.lib")))
                                return $"-Xlinker /libpath:\"{x64}\"";
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

            List<Token> tokens = Lexer.Tokenize(code);
            CompilationUnit ast = Parser.Parse(tokens);
            TypeChecker checker = new TypeChecker();
            ast.Accept(checker);
            LlvmEmitter emitter = new LlvmEmitter(checker);
            ast.Accept(emitter);
            string ir = emitter.GetOutput();
            Console.WriteLine(ir);
            File.WriteAllText("output.ll", ir);

            Process process = new Process();
            process.StartInfo.FileName = "clang.exe";
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
            process.StartInfo.FileName = "output.exe";
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit();
            Console.WriteLine($"0x{process.ExitCode:X8} {GetExitCodeMessage(process.ExitCode)}");
            return 0;
        }
    }
}
