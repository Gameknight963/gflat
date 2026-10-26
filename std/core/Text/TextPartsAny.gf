using std::Memory;
using std::Queries;
using std::Collections;
namespace std::Text;

// Borrowed source with a copied set of delimiter bytes.
public struct TextPartsAny : IEnumerable<Span<readonly(char)>, TextPartsAny::Enumerator>
{
    private Span<readonly(char)> source;
    private ulong mask0; private ulong mask1; private ulong mask2; private ulong mask3;
    private bool removeEmpty;

    public TextPartsAny(Span<readonly(char)> text, Span<readonly(char)> separators, bool removeEmpty = false)
    {
        source = text; mask0 = 0ul; mask1 = 0ul; mask2 = 0ul; mask3 = 0ul;
        this.removeEmpty = removeEmpty;
        for (nuint i = 0; i < separators.Length; i++) this.AddDelimiter(separators[i]);
    }

    public TextPartsAny(Span<readonly(char)> text, readonly(char)* separators, bool removeEmpty = false)
    {
        source = text; mask0 = 0ul; mask1 = 0ul; mask2 = 0ul; mask3 = 0ul;
        this.removeEmpty = removeEmpty;
        for (nuint i = 0; separators[i] != '\0'; i++) this.AddDelimiter(separators[i]);
    }

    private void AddDelimiter(char delimiter)
    {
        uint value = (uint)(byte)delimiter;
        ulong bit = 1ul << (int)(value % 64u);
        if (value < 64u) mask0 = mask0 | bit;
        else if (value < 128u) mask1 = mask1 | bit;
        else if (value < 192u) mask2 = mask2 | bit;
        else mask3 = mask3 | bit;
    }

    public immutable TextPartsAny::Enumerator GetEnumerator()
        => new TextPartsAny::Enumerator(source, mask0, mask1, mask2, mask3, removeEmpty);
    public immutable Enumerable<Span<readonly(char)>, TextPartsAny, TextPartsAny::Enumerator> AsEnumerable()
        => new Enumerable<Span<readonly(char)>, TextPartsAny, TextPartsAny::Enumerator>(*this);

    public struct Enumerator : IEnumerator<Span<readonly(char)>>
    {
        private Span<readonly(char)> source;
        private ulong mask0; private ulong mask1; private ulong mask2; private ulong mask3;
        private bool removeEmpty;
        private nuint next; private nuint start; private nuint length;
        private bool finished; private bool valid;

        private Enumerator(Span<readonly(char)> text, ulong bits0, ulong bits1, ulong bits2, ulong bits3, bool skipEmpty)
        {
            source = text; mask0 = bits0; mask1 = bits1; mask2 = bits2; mask3 = bits3;
            removeEmpty = skipEmpty; next = 0; start = 0; length = 0; finished = false; valid = false;
        }

        private immutable bool IsDelimiter(char delimiter)
        {
            uint value = (uint)(byte)delimiter;
            ulong bit = 1ul << (int)(value % 64u);
            if (value < 64u) return (mask0 & bit) != 0ul;
            if (value < 128u) return (mask1 & bit) != 0ul;
            if (value < 192u) return (mask2 & bit) != 0ul;
            return (mask3 & bit) != 0ul;
        }

        public bool MoveNext()
        {
            valid = false;
            while (!finished)
            {
                nuint begin = next; nuint end = begin;
                while (end < source.Length && !this.IsDelimiter(source[end])) end++;
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
                Span<readonly(char)> text = source;
                return text.Slice(start, length);
            }
        }
    }
}
