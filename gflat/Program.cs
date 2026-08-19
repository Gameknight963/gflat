using gflat.ast;
using System.Diagnostics;

namespace gflat
{
    public class Program
    {
        const string code = """
            namespace Program
            {
                public class Program
                {
                    public int FizzBuzz(int n)
                    {
                        if (n > 100)
                        {
                            return 0;
                        }
                        if (n % 15 == 0)
                        {
                            printf("FizzBuzz\n");
                        }
                        if (n % 3 == 0)
                        {
                            printf("Fizz\n");
                        }
                        if (n % 5 == 0)
                        {
                            printf("Buzz\n");
                        }
                        printf("%d\n", n);
                        FizzBuzz(n + 1);
                        return 0;
                    }

                    public int Main()
                    {
                        FizzBuzz(1);
                        return 0;
                    }
                }
            }
            """;
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
            process.StartInfo.Arguments = "output.ll -o output.exe";
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
            Console.WriteLine($"ran with exit code: {process.ExitCode}");
            return 0;
        }
    }
}
