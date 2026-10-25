using std::Collections;
namespace std;

// A reusable sequence. Source supplies GetEnumerator(); Iterator supplies
// MoveNext() and Current. These patterns remain usable without interfaces.
// Built-in factories store borrowed views instead of copying containers.
struct Enumerable<T, Source, Iterator> : IEnumerable<T, Iterator>
{
    private Source source;
    public Enumerable(Source input) { source = move input; }
    public Source SourceView() => source;
    public Iterator GetEnumerator() => source.GetEnumerator();
}
