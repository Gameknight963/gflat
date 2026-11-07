using Standard::Collections;
using Standard::Memory;

namespace Standard::Queries;

struct SelectManyResultSource<T, U, R, Source, Iterator, InnerSource, InnerIterator>
{
    private Source source;
    private Enumerable<U, InnerSource, InnerIterator>(T)* selector;
    private R(T, U)* resultSelector;

    public SelectManyResultSource(Source input, Enumerable<U, InnerSource, InnerIterator>(T)* project,
        R(T, U)* combine)
    {
        source = move input;
        selector = project;
        resultSelector = combine;
    }

    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator(), selector, resultSelector);

    private struct Cursor
    {
        private T outerValue;
        private Enumerable<U, InnerSource, InnerIterator> source;
        private InnerIterator iterator;
        private R(T, U)* resultSelector;

        public Cursor(T value, Enumerable<U, InnerSource, InnerIterator> input, R(T, U)* combine)
        {
            outerValue = move value;
            source = move input;
            iterator = source.GetEnumerator();
            resultSelector = combine;
        }

        public bool MoveNext() => iterator.MoveNext();
        public R Current { get => resultSelector(outerValue, iterator.Current); }
    }

    public struct Enumerator : IEnumerator<R>
    {
        private Iterator outer;
        private Enumerable<U, InnerSource, InnerIterator>(T)* selector;
        private R(T, U)* resultSelector;
        private Unique<Cursor> active;
        private bool valid;
        private bool finished;

        public Enumerator(Iterator input, Enumerable<U, InnerSource, InnerIterator>(T)* project, R(T, U)* combine)
        {
            outer = move input;
            selector = project;
            resultSelector = combine;
            active = new();
            valid = false;
            finished = false;
        }

        private void ClearActive()
        {
            Cursor*? retired = active.Release();
            if (retired != null) delete retired!;
        }

        public bool MoveNext()
        {
            valid = false;
            if (finished) return false;
            try
            {
                while (true)
                {
                    if (!active.IsEmpty && active.Value.MoveNext())
                    {
                        valid = true;
                        return true;
                    }
                    this.ClearActive();
                    if (!outer.MoveNext())
                    {
                        finished = true;
                        return false;
                    }
                    T value = outer.Current;
                    Enumerable<U, InnerSource, InnerIterator> projected = selector(value);
                    active = new Unique<Cursor>(new* Cursor(move value, move projected, resultSelector));
                }
            }
            catch
            {
                finished = true;
                this.ClearActive();
                throw;
            }
        }

        public R Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                try { return active.Value.Current; }
                catch
                {
                    valid = false;
                    finished = true;
                    this.ClearActive();
                    throw;
                }
            }
        }
    }
}
