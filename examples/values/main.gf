using std;

extern int puts(readonly(char)* text);

int main()
{
    Vector<String> names = new Vector<String>();
    names.Add(s"Ada");
    String name = s"Grace";
    names.Add(move name);
    name = s"reused"; // Moving consumed the previous value; initialize a new one.

    names[(nuint)0].Append(c"!", 1);
    readonly(Vector<String>)* view = &names;
    String greeting = s$"Hello {(*view)[(nuint)0]} and {(*view)[(nuint)1]}. Count: {names.Count}";
    puts(greeting.ToString());

    String last = names.Pop();
    puts(last.ToString());
    puts(name.ToString());
    return 0;
}
