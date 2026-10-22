using std;

// Build: gflat build examples/tasks/tasks.gfproj
// Run the resulting examples/tasks/bin/Debug/tasks.exe directly for console input.
// Example session:
//   add Finish String tests
//   add Write std
//   done 2
//   list
//   quit
// Console I/O only; tasks live until this process exits.

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

bool IsCommand(readonly(String)* text, readonly(char)* command)
{
    return text.Length == CString::Length(command) && text.StartsWith(command);
}

bool Complete(Vector<Task>* tasks, readonly(String)* line)
{
    int number = 0;
    if (!TryParseInt(line.Data + 5, line.Length - 5, &number)) return false;
    if (number <= 0 || (nuint)number > tasks.Count) return false;
    (*tasks)[(nuint)(number - 1)].Done = true;
    return true;
}

void List(readonly(Vector<Task>)* tasks)
{
    if (tasks.Count == 0) Console::WriteLine("No tasks yet.");
    for (nuint i = 0; i < tasks.Count; i++)
    {
        readonly(Task)* task = &(*tasks)[i];
        char mark = ' ';
        if (task.Done) mark = 'x';
        String row = s$"{i + 1}. [{mark}] {task.Title}";
        Console::WriteLine(ref row);
    }
}

int main()
{
    Vector<Task> tasks = new Vector<Task>();
    String line = new String();
    Console::WriteLine("Tasks (in memory). Commands: add <title>, list, done <number>, quit");
    while (Console::TryReadLine(ref line))
    {
        if (IsCommand(&line, "quit")) break;
        if (IsCommand(&line, "list")) List(&tasks);
        else if (line.StartsWith("add "))
        {
            if (line.Length == 4) Console::WriteLine("A task needs a title.");
            else
            {
                String title = String::From(line.Data + 4, line.Length - 4);
                tasks.Add(new Task(move title));
                Console::WriteLine("Added.");
            }
        }
        else if (line.StartsWith("done "))
        {
            if (Complete(&tasks, &line)) Console::WriteLine("Done.");
            else Console::WriteLine("Use done <number> with an existing task number.");
        }
        else if (line.Length != 0)
            Console::WriteLine("Unknown command. Use add <title>, list, done <number>, or quit.");
    }
    return 0;
}
