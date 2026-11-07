using Standard::Queries;

namespace Standard::Collections;

extension HashSet<T : IHashable>
{
    public immutable Enumerable<immutable(T)*, HashSet<T>::View, HashSet<T>::Enumerator> AsEnumerable()
        => new Enumerable<immutable(T)*, HashSet<T>::View, HashSet<T>::Enumerator>(new HashSet<T>::View(this));
}
