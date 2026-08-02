using gflat.ast;

namespace gflat
{
    public class Program
    {
        const string code = """
            using std;

            namespace TestProgram
            {
                public class Animal
                {
                    public string Name;
                    public int Age;

                    public Animal(string name, int age)
                    {
                        Name = name;
                        Age = age;
                    }

                    public string Describe()
                    {
                        return $"I am {Name} and I am {Age} years old";
                    }
                }

                public class Dog : Animal
                {
                    public string Breed;

                    public Dog(string name, int age, string breed)
                    {
                        Name = name;
                        Age = age;
                        Breed = breed;
                    }

                    public string Bark()
                    {
                        return $"{Name} says: woof!";
                    }
                }

                public class Program
                {
                    public int Main(string[] args)
                    {
                        // basic arithmetic
                        int x = 5;
                        int y = 10;
                        int z = x + y * 2;

                        // conditionals
                        if (z > 20)
                        {
                            int big = z * 2;
                        }
                        else
                        {
                            int small = z + 1;
                        }

                        // while loop
                        int counter = 0;
                        while (counter < 10)
                        {
                            counter = counter + 1;
                        }

                        // for loop
                        int sum = 0;
                        for (int i = 0; i < 10; i++)
                        {
                            sum = sum + i;
                        }

                        // pointers
                        int* ptr = null;
                        int*? nullable = null;

                        // string interpolation
                        string msg = $"z is {z} and sum is {sum}";

                        // nested member access
                        Dog* dog = new Dog("Rex", 3, "Labrador");
                        string description = dog.Describe();
                        string bark = dog.Bark();

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
