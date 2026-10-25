using std::Collections;
namespace std::Queries;

// Owns one captured value. Each iterator owns a copy; Current copies normally.
public struct Repeated<T> : IEnumerable<T, Repeated<T>::Enumerator>
{
    private T value;
    private nuint count;

    public Repeated(T value, nuint count)
    {
        this.value = move value;
        this.count = count;
    }

    public nuint Count { immutable get nothrow => count; }
    public bool IsEmpty { immutable get nothrow => count == 0; }
    public readonly Enumerator GetEnumerator() => new Enumerator(value, count);
    public readonly Enumerable<T, Repeated<T>, Repeated<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Repeated<T>, Repeated<T>::Enumerator>(*this);

    public struct Enumerator : IEnumerator<T>
    {
        private T value;
        private nuint remaining;
        private bool valid;

        public Enumerator(T value, nuint count)
        {
            this.value = move value;
            remaining = count;
            valid = false;
        }

        public bool MoveNext() nothrow
        {
            valid = false;
            if (remaining == 0) return false;
            remaining--;
            valid = true;
            return true;
        }

        public T Current
        {
            readonly get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return value;
            }
        }
    }
}



