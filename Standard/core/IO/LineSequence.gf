using Standard::Text;
using Standard::Collections;
namespace Standard::IO;

/// Deferred counted lines borrowed from a reader's current position.
/// Copies and enumerators share the source cursor; none rewind or close it.
/// Keep the reader alive, open and unmoved while this view or its iterators are used.
public struct LineSequence : IEnumerable<String, LineSequence::Enumerator>
{
    private IByteReader* input;

    public LineSequence(IByteReader* source) { input = source; }
    public readonly Enumerator GetEnumerator() => new Enumerator(input);

    public struct Enumerator
    {
        private IByteReader* input;
        private String current;
        private bool valid;
        private bool finished;

        public Enumerator(IByteReader* source)
        {
            input = source;
            current = new String();
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            if (!Standard::IO::TryReadLine(input, ref current))
            {
                finished = true;
                return false;
            }
            valid = true;
            return true;
        }

        /// Borrows the current line until MoveNext, iterator movement or destruction.
        /// Copy into String to retain an independent line.
        public ref immutable(String) Current
        {
            immutable get
            {
                if (!valid) throw new IOException("Enumerator has no current line");
                return current;
            }
        }
    }
}
