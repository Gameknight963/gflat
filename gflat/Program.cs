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
            foreach (Token token in tokens) Console.WriteLine(token);
        }
    }
}
