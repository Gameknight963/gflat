using Standard::Collections;
using Standard::Memory;

namespace Standard::Queries;

struct IndexedSelectManySource<T, U, Source, Iterator, InnerSource, InnerIterator>
{
    private Source source;
    private Enumerable<U, InnerSource, InnerIterator>(T, nuint)* selector;

    public IndexedSelectManySource(Source input, Enumerable<U, InnerSource, InnerIterator>(T, nuint)* project)
    {
        source = move input;
        selector = project;
    }

    public Enumerator GetEnumerator() => new Enumerator(source.GetEnumerator(), selector);

    // The query must outlive its iterator, whose implementation may borrow it.
    private struct Cursor
    {
        private Enumerable<U, InnerSource, InnerIterator> source;
        private InnerIterator iterator;

        public Cursor(Enumerable<U, InnerSource, InnerIterator> input)
        {
            source = move input;
            iterator = source.GetEnumerator();
        }

        public bool MoveNext() => iterator.MoveNext();
        public U Current { get => iterator.Current; }
    }

    public struct Enumerator : IEnumerator<U>
    {
        private Iterator outer;
        private Enumerable<U, InnerSource, InnerIterator>(T, nuint)* selector;
        private Unique<Cursor> active;
        private bool valid;
        private bool finished;
        private nuint index;
        private bool started;

        public Enumerator(Iterator input, Enumerable<U, InnerSource, InnerIterator>(T, nuint)* project)
        {
            outer = move input;
            selector = project;
            active = new();
            valid = false;
            finished = false;
            index = 0;
            started = false;
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
                    if (started)
                    {
                        if (index == (nuint)0 - 1) throw new Exception("Sequence index overflow");
                        index++;
                    }
                    else started = true;
                    Enumerable<U, InnerSource, InnerIterator> projected = selector(outer.Current, index);
                    active = new Unique<Cursor>(new* Cursor(move projected));
                }
            }
            catch
            {
                finished = true;
                this.ClearActive();
                throw;
            }
        }

        public U Current
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
