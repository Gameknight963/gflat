using gflat.ast;

namespace gflat
{
    public class Program
    {
        const string code = """
            using std;
    
            namespace Program
            {
                public class Program
                {
                    int Main(string[] args)
                    {
                        int x = 5;
                        string msg = $"x is {x} today";
                        return 0;
                    }
                }
            }
            """;
        static void Main(string[] args)
        {
            List<Token> tokens = Lexer.Tokenize(code);
            foreach (Token t in tokens)
                Console.WriteLine($"{t.Kind,-35} '{t.Text}'");
            CompilationUnit ast = Parser.Parse(tokens);
            AstPrinter printer = new AstPrinter();
            ast.Accept(printer);
        }
    }
}
