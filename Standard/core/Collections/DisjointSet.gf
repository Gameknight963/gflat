namespace Standard::Collections;

// Integer-indexed components. Union by size keeps immutable root lookup logarithmic.
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

    // Appends a singleton and returns its stable index. Allocation failure
    // preserves the partition, including all existing representatives.
    public nuint Add()
    {
        nuint index = parents.Count;
        nuint maximum = ((nuint)0 - (nuint)1) / (nuint)sizeof(nuint);
        if (index == maximum) throw new Exception("DisjointSet capacity overflow");
        nuint requested = index + 1;
        if (parents.Capacity < requested || sizes.Capacity < requested)
        {
            nuint grown = 4;
            if (index != 0)
            {
                grown = maximum;
                if (index <= maximum / 2) grown = index * 2;
            }
            if (grown < requested) grown = requested;
            if (grown > maximum) grown = maximum;
            // Reserve both owners before either count changes. The remaining
            // appends only relocate primitive values into available storage.
            parents.Reserve(grown);
            sizes.Reserve(grown);
        }
        parents.Add(index);
        sizes.Add(1);
        setCount++;
        return index;
    }

    // Restore singleton components while retaining all current elements and storage.
    public void Reset() nothrow
    {
        for (nuint index = 0; index < parents.Count; index++)
        {
            parents[index] = index;
            sizes[index] = 1;
        }
        setCount = parents.Count;
    }

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

    // Shortens parent chains for later lookups without changing representatives.
    public nuint FindAndCompress(nuint index)
    {
        if (index >= parents.Count) throw new Exception("DisjointSet index out of range");
        while (true)
        {
            nuint parent = parents[index];
            if (parent == index) return index;
            nuint grandparent = parents[parent];
            parents[index] = grandparent;
            index = grandparent;
        }
    }

    public immutable bool Connected(nuint first, nuint second)
        => this.Find(first) == this.Find(second);

    public immutable nuint SizeOf(nuint index) => sizes[this.Find(index)];

    public bool Union(nuint first, nuint second)
    {
        // Validate both indices before changing either tree or component count.
        if (first >= parents.Count || second >= parents.Count)
            throw new Exception("DisjointSet index out of range");
        nuint left = this.FindAndCompress(first);
        nuint right = this.FindAndCompress(second);
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
