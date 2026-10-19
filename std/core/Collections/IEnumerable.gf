namespace std;

// The iterator type stays concrete. This contract does not require allocating
// an interface object; foreach also accepts these members without an interface.
interface IEnumerable<T, Iterator>
{
    Iterator GetEnumerator();
}
