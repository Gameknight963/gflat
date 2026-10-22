using std;

extern int puts(readonly(char)* text);

int main()
{
    Vector<String> names = new Vector<String>();
    names.Add(s"Ada");
    String name = s"Grace";
    names.Add(move name);
    name = s"reused"; // Moving consumed the previous value; initialize a new one.

    names[0].Append("!", 1);
    immutable(Vector<String>)* view = &names;
    String greeting = s$"Hello {(*view)[0]} and {(*view)[1]}. Count: {names.Count}";
    puts(greeting.ToCString());
    String last = names.Pop();
    puts(last.ToCString());
    puts(name.ToCString());
    return 0;
}
