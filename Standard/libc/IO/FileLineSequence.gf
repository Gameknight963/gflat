using Standard::Text;
using Standard::Queries;
using Standard::Collections;

namespace Standard::IO;

// Borrows an open FileStream at its current position; each enumeration consumes it.
public struct FileLineSequence : IEnumerable<String, FileLineSequence::Enumerator>
{
    private FileStream* input;
    public FileLineSequence(FileStream* source) { input = source; }
    public readonly Enumerator GetEnumerator() => new Enumerator(input);
    public Enumerable<String, FileLineSequence, FileLineSequence::Enumerator> AsEnumerable()
        => new Enumerable<String, FileLineSequence, FileLineSequence::Enumerator>(*this);

    public struct Enumerator
    {
        private FileStream* input;
        private String current;
        private bool valid;
        private bool finished;
        public Enumerator(FileStream* source)
        {
            input = source; current = new String(); valid = false; finished = false;
        }
        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            current.Clear();
            if (!FileLines::TryReadLine(input, &current)) { finished = true; return false; }
            valid = true;
            return true;
        }
        // The borrowed line is replaced by the next MoveNext and dies with this iterator.
        public ref immutable(String) Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current line");
                return current;
            }
        }
    }
}

public FileLineSequence Lines(FileStream* input) => new FileLineSequence(input);

extension FileStream
{
    public FileLineSequence Lines()
    => Standard::IO::Lines(this);
}
