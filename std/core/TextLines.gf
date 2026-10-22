namespace std;

// Borrowed counted lines. LF and CRLF terminate lines; a bare CR is text.
public struct TextLines : IEnumerable<Span<readonly(char)>, TextLines::Enumerator>
{
    private Span<readonly(char)> source;

    public TextLines(Span<readonly(char)> text) { source = text; }

    public immutable TextLines::Enumerator GetEnumerator() => new TextLines::Enumerator(source);

    public immutable Enumerable<Span<readonly(char)>, TextLines, TextLines::Enumerator> AsEnumerable()
        => new Enumerable<Span<readonly(char)>, TextLines, TextLines::Enumerator>(*this);

    public struct Enumerator : IEnumerator<Span<readonly(char)>>
    {
        private Span<readonly(char)> source;
        private nuint next;
        private nuint start;
        private nuint length;
        private bool valid;

        public Enumerator(Span<readonly(char)> text)
        {
            source = text; next = 0; start = 0; length = 0; valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (next == source.Length) return false;
            start = next;
            nuint end = start;
            while (end < source.Length && source[end] != '\n') end++;
            next = end;
            if (end < source.Length)
            {
                next++;
                if (end > start && source[end - 1] == '\r') end--;
            }
            length = end - start;
            valid = true;
            return true;
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
