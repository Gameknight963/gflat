using Standard::Queries;

namespace Standard::Collections;

extension SortedSet<T>
{
    public immutable Enumerable<immutable(T)*, SortedSet<T>::View, SortedSet<T>::Enumerator> AsEnumerable()
        => new Enumerable<immutable(T)*, SortedSet<T>::View, SortedSet<T>::Enumerator>(new SortedSet<T>::View(this));
}
