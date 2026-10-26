using std::Memory;
using std::Queries;
using std::Collections;
namespace std::Text;

// Single-byte delimiter splitting over borrowed counted storage.
public struct TextParts : IEnumerable<Span<readonly(char)>, TextParts::Enumerator>
{
    private Span<readonly(char)> source;
    private char delimiter;
    private bool removeEmpty;

    public TextParts(Span<readonly(char)> text, char separator, bool removeEmpty = false)
    {
        source = text;
        delimiter = separator;
        this.removeEmpty = removeEmpty;
    }

    public immutable TextParts::Enumerator GetEnumerator() => new TextParts::Enumerator(source, delimiter, removeEmpty);

    public immutable Enumerable<Span<readonly(char)>, TextParts, TextParts::Enumerator> AsEnumerable()
        => new Enumerable<Span<readonly(char)>, TextParts, TextParts::Enumerator>(*this);

    public struct Enumerator : IEnumerator<Span<readonly(char)>>
    {
        private Span<readonly(char)> source;
        private char delimiter;
        private bool removeEmpty;
        private nuint next;
        private nuint start;
        private nuint length;
        private bool finished;
        private bool valid;

        public Enumerator(Span<readonly(char)> text, char separator, bool skipEmpty)
        {
            source = text; delimiter = separator; removeEmpty = skipEmpty;
            next = 0; start = 0; length = 0; finished = false; valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            while (!finished)
            {
                nuint begin = next;
                nuint end = begin;
                while (end < source.Length && source[end] != delimiter) end++;
                if (end == source.Length) finished = true;
                else next = end + 1;
                if (removeEmpty && end == begin) continue;
                start = begin; length = end - begin; valid = true;
                return true;
            }
            return false;
        }

        public Span<readonly(char)> Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                // Slice the copied view metadata; source bytes remain protected.
                Span<readonly(char)> text = source;
                return text.Slice(start, length);
            }
        }
    }
}
