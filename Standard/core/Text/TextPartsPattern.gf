using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

namespace Standard::Text;

public struct TextPartsPattern : IEnumerable<Span<readonly(char)>, TextPartsPattern::Enumerator>
{
    private Span<readonly(char)> source;
    private Span<readonly(char)> separator;
    private bool removeEmpty;

    public TextPartsPattern(Span<readonly(char)> text, Span<readonly(char)> separator, bool removeEmpty = false)
    {
        source = text;
        this.separator = separator;
        this.removeEmpty = removeEmpty;
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(source, separator, removeEmpty);
    public immutable Enumerable<Span<readonly(char)>, TextPartsPattern, Enumerator> AsEnumerable()
        => new Enumerable<Span<readonly(char)>, TextPartsPattern, Enumerator>(*this);

    public struct Enumerator : IEnumerator<Span<readonly(char)>>
    {
        private Span<readonly(char)> source;
        private Span<readonly(char)> separator;
        private bool removeEmpty;
        private nuint next;
        private nuint start;
        private nuint length;
        private bool finished;
        private bool valid;

        public Enumerator(Span<readonly(char)> text, Span<readonly(char)> separator, bool removeEmpty)
        {
            source = text;
            this.separator = separator;
            this.removeEmpty = removeEmpty;
            next = 0; start = 0; length = 0; finished = false; valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            while (!finished)
            {
                nuint begin = next;
                nuint remaining = source.Length - begin;
                nuint found = remaining;
                if (!separator.IsEmpty && separator.Length <= remaining)
                    found = ByteSearch::IndexOf((source.Data! + begin).reinterpret<readonly(byte)*>(), remaining,
                        (separator.Data!).reinterpret<readonly(byte)*>(), separator.Length);
                nuint end = begin + found;
                if (found == remaining) finished = true;
                else next = end + separator.Length;
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
                Span<readonly(char)> text = source;
                return text.Slice(start, length);
            }
        }
    }
}
