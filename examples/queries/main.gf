using Standard::Text;
using Standard::Queries;
using Standard::Collections;
using Standard;

int main()
{
    Vector<int> numbers = new Vector<int>();
    for (int i = 1; i <= 10; i++) numbers.Add(i);

    // The pointer and the queries borrow numbers; its destructor owns cleanup.
    Vector<int>* borrowed = &numbers;
    var query = borrowed.AsEnumerable()
        .Where((int n) => n % 2 == 0)
        .Select((int n) => n * n)
        .Take(3);

    foreach (int number in query)
    {
        String line = s$"{number}";
        Console::WriteLine(ref line);
    }

    Vector<int> saved = query.ToVector();
    nuint count = saved.Count;
    String summary = s$"Saved {count} values";
    Console::WriteLine(ref summary);
    return 0;
}
