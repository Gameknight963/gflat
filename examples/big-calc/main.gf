using std::Numerics;
using std;

int main(int argc, char** argv)
{
    if (argc != 4)
    {
        Console::Error.WriteLine("Usage: big-calc <left> <operator> <right>");
        return 2;
    }
    readonly(char)* token = argv[2];
    int operation = -1;
    if (CString::Equals(token, "+")) operation = 0;
    else if (CString::Equals(token, "-")) operation = 1;
    else if (CString::Equals(token, "*")) operation = 2;
    else if (CString::Equals(token, "/")) operation = 3;
    else if (CString::Equals(token, "%")) operation = 4;
    else if (CString::Equals(token, "gcd")) operation = 5;
    else if (CString::Equals(token, "lcm")) operation = 6;
    if (operation < 0)
    {
        Console::Error.WriteLine("big-calc: expected operator +, -, *, /, %, gcd, or lcm");
        return 2;
    }

    try
    {
        BigInteger left = new();
        BigInteger right = new();
        if (!BigInteger::TryParse(argv[1], ref left)
            || !BigInteger::TryParse(argv[3], ref right))
        {
            Console::Error.WriteLine("big-calc: operands must be decimal integers");
            return 2;
        }
        BigInteger result = new();
        if (operation == 0) result = left.Add(&right);
        else if (operation == 1) result = left.Subtract(&right);
        else if (operation == 2) result = left.Multiply(&right);
        else if (operation == 3) result = left.Divide(&right);
        else if (operation == 4) result = left.Remainder(&right);
        else if (operation == 5) result = BigInteger::GreatestCommonDivisor(&left, &right);
        else result = BigInteger::LeastCommonMultiple(&left, &right);
        result.Format(Console::Out);
        Console::WriteLine();
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.Write("big-calc: ", 10);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
}
