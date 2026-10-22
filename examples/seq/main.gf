using std;

private extern int strcmp(immutable(char*) left, immutable(char*) right);

int main(int argc, char** argv)
{
    if (argc < 3 || argc > 4 || argc == 4 && strcmp(argv[3], "--even") != 0)
    {
        Console::WriteLine("Usage: seq <start> <count> [--even]");
        return 2;
    }
    try
    {
        int start = std::ParseInt(argv[1]);
        int count = std::ParseInt(argv[2]);
        bool(int)* predicate = (int n) => true;
        if (argc == 4) predicate = (int n) => n % 2 == 0;
        FilteredIntRange values = new IntRange(start, count).Where(predicate);
        Console::Writer output = new Console::Writer();
        foreach (int value in values)
        {
            value.Format(&output);
            Console::WriteLine();
        }
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::WriteLine(error.GetMessage());
        return 1;
    }
}
