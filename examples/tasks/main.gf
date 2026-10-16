using std;

// Build: dotnet run --project gflat -- build examples/tasks/tasks.gfproj
// Run the resulting examples/tasks/bin/Debug/tasks.exe directly for console input.
// Example session:
//   add Finish String tests
//   add Write std
//   done 2
//   list
//   quit
// Console I/O only; tasks live until this process exits.
extern int getchar();
extern int puts(readonly(char)* text);

struct Task
{
    // Field cleanup and copying are supplied automatically, including Title.
    public String Title;
    public bool Done;

    public Task(String title)
    {
        Title = move title;
        Done = false;
    }
}

// False means EOF with no remaining input. A final line need not end in '\n'.
bool ReadLine(String* line)
{
    line.Clear();
    while (true)
    {
        int input = getchar();
        if (input == -1) return line.Length != (nuint)0;
        if (input == 10) return true;
        if (input != 13)
        {
            char character = (char)input;
            line.Append(&character, 1);
        }
    }
}

bool StartsWith(readonly(String)* text, readonly(char)* prefix)
{
    nuint i = 0;
    while (prefix[i] != '\0')
    {
        if (i == text.Length || text.Data[i] != prefix[i]) return false;
        i++;
    }
    return true;
}

bool IsCommand(readonly(String)* text, readonly(char)* command)
{
    if (!StartsWith(text, command)) return false;
    nuint length = 0;
    while (command[length] != '\0') length++;
    return text.Length == length;
}

bool Complete(Vector<Task>* tasks, readonly(String)* line)
{
    int number = 0;
    if (!TryParseInt(line.Data + 5, line.Length - (nuint)5, &number)) return false;
    if (number <= 0 || (nuint)number > tasks.Count) return false;
    (*tasks)[(nuint)(number - 1)].Done = true;
    return true;
}

void List(readonly(Vector<Task>)* tasks)
{
    if (tasks.Count == (nuint)0) puts("No tasks yet.");
    for (nuint i = 0; i < tasks.Count; i++)
    {
        readonly(Task)* task = &(*tasks)[i];
        char mark = ' ';
        if (task.Done) mark = 'x';
        String row = s$"{i + (nuint)1}. [{mark}] {task.Title}";
        puts(row.ToCString());
    }
}

int main()
{
    Vector<Task> tasks = new Vector<Task>();
    String line = new String();
    puts("Tasks (in memory). Commands: add <title>, list, done <number>, quit");
    while (ReadLine(&line))
    {
        if (IsCommand(&line, "quit")) break;
        if (IsCommand(&line, "list")) List(&tasks);
        else if (StartsWith(&line, "add "))
        {
            if (line.Length == (nuint)4) puts("A task needs a title.");
            else
            {
                String title = String::From(line.Data + 4, line.Length - (nuint)4);
                tasks.Add(new Task(move title));
                puts("Added.");
            }
        }
        else if (StartsWith(&line, "done "))
        {
            if (Complete(&tasks, &line)) puts("Done.");
            else puts("Use done <number> with an existing task number.");
        }
        else if (line.Length != (nuint)0)
            puts("Unknown command. Use add <title>, list, done <number>, or quit.");
    }
    return 0;
}
