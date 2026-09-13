using gflat.ast;
using System.ComponentModel;
using System.Diagnostics;

namespace gflat
{
    public class Program
    {
        const string code = """
            extern int printf(char* fmt, ...);
            extern void* malloc(long size);

            struct Point
            {
                int x;
                int y;

                void Move(int dx, int dy)
                {
                    this.x += dx;
                    this.y += dy;
                }

                void Print()
                {
                    printf("Point(%d, %d)\n", this.x, this.y);
                }
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
                public alias BinaryOp = int(int, int)*;

                public int Add(int a, int b)
                {
                    return a + b;
                }

                public int Double(int x)
                {
                    return x * 2;
                }
            }

            enum LogLevel : char
            {
                Info = 1,
                Warning = 2,
                Error = 3
            }

            enum Status
            {
                Pending,
                Running = 10,
                Done
            }

            int main()
            {
                LogMessage("Top-level functions work!");

                Point p;
                p.x = 10;
                p.y = 20;
                p.Print();

                char[] a = "string";
                printf("buffer before: %s\n", a);
                a[0] = 'S';
                printf("buffer after: %s\n", a);

                p.Move(5, 5);
                p.Print();

                Point* ptr = &p;
                ptr.Move(100, 200);
                ptr.Print();

                printf("ptr address: 0x%llx, is_null: %d\n", ptr->address, ptr->is_null);

                defer printf("deferred cleanup: main finished!\n");

                Point* heapPoint = malloc(8L);
                defer heapPoint->free();
                heapPoint.x = 42;
                heapPoint.y = 84;
                heapPoint.Print();
                printf("heapPoint address: 0x%llx\n", heapPoint->address);

                long sum = Add64(1000000000L, 2000000000L);
                printf("sum: %lld\n", sum);

                int d = Math::Double(21);
                printf("double: %d\n", d);

                alias LocalOp = Math::BinaryOp;
                LocalOp op = &Math::Add;
                printf("function pointer call: %d\n", op(20, 22));

                int(int, int)*? maybeOp = null;
                printf("maybeOp is_null: %d\n", maybeOp->is_null);
                maybeOp = &Math::Add;
                printf("maybeOp is_null after assignment: %d, address: 0x%llx\n", maybeOp->is_null, maybeOp->address);

                Status status = Status::Running;
                printf("enum status: %d (expected 10), is Done: %d\n", status, Status.Done);
                LogLevel level = LogLevel::Info;
                printf("enum level: %d\n", level);

                uint u = 3000000000u;
                ulong ul = 10000000000000000000ul;
                printf("unsigned int: %u, unsigned long: %llu\n", u, ul);

                uint shifted = 1u << 4;
                uint rshifted = 0x80000000u >> 1;
                printf("shifted: %u, rshifted: 0x%x\n", shifted, rshifted);

                int convertedFromUInt = u;
                printf("implicit uint to int: %d\n", convertedFromUInt);

                long largeNum = 0x123456789abcdef0l;
                int truncated = (int)largeNum;
                printf("explicit cast long to int: 0x%x\n", truncated);

                nuint ptrAddress = (nuint)ptr;
                Point* restoredPtr = (Point*)ptrAddress;
                printf("nuint roundtrip address: 0x%llx\n", restoredPtr->address);

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
            process.StartInfo.FileName = Path.GetFullPath("output.exe");
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit();
            Console.WriteLine($"0x{process.ExitCode:X8} {GetExitCodeMessage(process.ExitCode)}");
            return 0;
        }
    }
}
