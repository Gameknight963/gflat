using std::Numerics;
using std;

int main(int argc, char** argv)
{
    if (argc != 4)
    {
        Console::Error.WriteLine("Usage: ratio-calc <left> <operator> <right>");
        return 2;
    }
    readonly(char)* token = argv[2];
    int operation = -1;
    if (CString::Equals(token, "+")) operation = 0;
    else if (CString::Equals(token, "-")) operation = 1;
    else if (CString::Equals(token, "*")) operation = 2;
    else if (CString::Equals(token, "/")) operation = 3;
    if (operation < 0)
    {
        Console::Error.WriteLine("ratio-calc: expected operator +, -, *, or /");
        return 2;
    }
    try
    {
        Rational left = new();
        Rational right = new();
        if (!Rational::TryParse(argv[1], ref left) || !Rational::TryParse(argv[3], ref right))
        {
            Console::Error.WriteLine("ratio-calc: operands must be integers or numerator/denominator with a nonzero denominator");
            return 2;
        }
        Rational result = new();
        if (operation == 0) result = left.Add(&right);
        else if (operation == 1) result = left.Subtract(&right);
        else if (operation == 2) result = left.Multiply(&right);
        else result = left.Divide(&right);
        result.Format(Console::Out);
        Console::WriteLine();
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.Write("ratio-calc: ", 12);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
}
