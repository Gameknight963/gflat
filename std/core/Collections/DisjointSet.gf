namespace std::Collections;

// Fixed index universe. Union by component size keeps immutable root lookup logarithmic.
public struct DisjointSet
{
    private Vector<nuint> parents;
    private Vector<nuint> sizes;
    private nuint setCount;

    public DisjointSet() nothrow
    {
        parents = new(); sizes = new(); setCount = 0;
    }

    public DisjointSet(nuint count)
    {
        // Both owners are live before either allocation can fail.
        parents = new(); sizes = new(); setCount = count;
        parents.Reserve(count);
        sizes.Reserve(count);
        for (nuint index = 0; index < count; index++)
        {
            parents.Add(index);
            sizes.Add(1);
        }
    }

    public nuint ElementCount { immutable get nothrow => parents.Count; }
    public nuint SetCount { immutable get nothrow => setCount; }

    // Does not compress paths: callers can query through immutable views.
    public immutable nuint Find(nuint index)
    {
        if (index >= parents.Count) throw new Exception("DisjointSet index out of range");
        while (true)
        {
            nuint parent = parents[index];
            if (parent == index) return index;
            index = parent;
        }
    }

    public immutable bool Connected(nuint first, nuint second)
        => this.Find(first) == this.Find(second);

    public immutable nuint SizeOf(nuint index) => sizes[this.Find(index)];

    public bool Union(nuint first, nuint second)
    {
        // Validate both indices before changing either tree or component count.
        nuint left = this.Find(first);
        nuint right = this.Find(second);
        if (left == right) return false;
        if (sizes[left] < sizes[right])
        {
            nuint temporary = left; left = right; right = temporary;
        }
        parents[right] = left;
        // Distinct components are disjoint: their sum is at most ElementCount.
        sizes[left] += sizes[right];
        setCount--;
        return true;
    }
}
