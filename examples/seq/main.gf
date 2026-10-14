using std;

private extern int strcmp(readonly(char*) left, readonly(char*) right);

int main(int argc, char** argv)
{
    if (argc < 3 || argc > 4 || argc == 4 && strcmp(argv[3], "--even") != 0)
    {
        Console::WriteLine("Usage: seq <start> <count> [--even]");
        return 2;
    }
    try
    {
        int start = argv[1].ParseInt();
        int count = argv[2].ParseInt();
        bool(int)* predicate = (int n) => true;
        if (argc == 4) predicate = (int n) => n % 2 == 0;
        FilteredIntRange values = Range(start, count).Where(predicate);
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
