using Standard::Queries;

namespace Standard::Collections;

extension HashMap<K : IHashable, V>::KeyCollection
{
    public immutable Enumerable<immutable(K)*, HashMap<K, V>::KeyCollection, HashMap<K, V>::KeyEnumerator> AsEnumerable()
        => new Enumerable<immutable(K)*, HashMap<K, V>::KeyCollection, HashMap<K, V>::KeyEnumerator>(*this);
}

extension HashMap<K : IHashable, V>::ValueCollection
{
    public immutable Enumerable<immutable(V)*, HashMap<K, V>::ValueCollection, HashMap<K, V>::ValueEnumerator> AsEnumerable()
        => new Enumerable<immutable(V)*, HashMap<K, V>::ValueCollection, HashMap<K, V>::ValueEnumerator>(*this);
}

extension SortedMap<K, V>::KeyCollection
{
    public immutable Enumerable<immutable(K)*, SortedMap<K, V>::KeyCollection, SortedMap<K, V>::KeyEnumerator> AsEnumerable()
        => new Enumerable<immutable(K)*, SortedMap<K, V>::KeyCollection, SortedMap<K, V>::KeyEnumerator>(*this);
}

extension SortedMap<K, V>::ValueCollection
{
    public immutable Enumerable<immutable(V)*, SortedMap<K, V>::ValueCollection, SortedMap<K, V>::ValueEnumerator> AsEnumerable()
        => new Enumerable<immutable(V)*, SortedMap<K, V>::ValueCollection, SortedMap<K, V>::ValueEnumerator>(*this);
}

