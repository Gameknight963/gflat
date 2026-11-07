using Standard::Queries;

namespace Standard::Collections;

extension PriorityQueue<T>::UnorderedItemsCollection
{
    public immutable Enumerable<immutable(T)*, PriorityQueue<T>::UnorderedItemsCollection, PriorityQueue<T>::UnorderedEnumerator> AsEnumerable()
        => new Enumerable<immutable(T)*, PriorityQueue<T>::UnorderedItemsCollection, PriorityQueue<T>::UnorderedEnumerator>(*this);
}
