using Standard::Collections;
namespace Standard::IO;

/// Deferred, single-pass bytes borrowed from a reader's current position.
/// Copies and enumerators share the source cursor; none rewind or close it.
/// Keep the reader alive, open and unmoved while this view or its iterators are used.
public struct ByteSequence : IEnumerable<byte, ByteSequence::Enumerator>
{
    private IByteReader* input;

    public ByteSequence(IByteReader* source) { input = source; }
    public readonly Enumerator GetEnumerator() => new Enumerator(input);

    public struct Enumerator : IEnumerator<byte>
    {
        private IByteReader* input;
        private byte current;
        private bool valid;
        private bool finished;

        public Enumerator(IByteReader* source)
        {
            input = source;
            current = 0;
            valid = false;
            finished = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            int value = Standard::IO::ReadByte(input);
            if (value == -1)
            {
                finished = true;
                return false;
            }
            current = (byte)value;
            valid = true;
            return true;
        }

        public byte Current
        {
            immutable get
            {
                if (!valid) throw new IOException("Enumerator has no current byte");
                return current;
            }
        }
    }
}
