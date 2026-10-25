namespace std::Collections;

// Sort indices first so allocation and comparison finish before any owner moves.
public void StableSort<T>(Span<T> values, int(immutable(T)*, immutable(T)*)* compare)
{
    nuint count = values.Length;
    if (count < 2) return;
    Vector<nuint> first = new();
    Vector<nuint> second = new();
    first.Reserve(count);
    second.Reserve(count);
    for (nuint i = 0; i < count; i++)
    {
        first.Add(i);
        second.Add(0);
    }
    Span<nuint> order = first.AsSpan();
    Span<nuint> scratch = second.AsSpan();
    T* data = values.Data!;
    nuint width = 1;
    while (width < count)
    {
        nuint start = 0;
        while (start < count)
        {
            nuint leftLength = count - start;
            if (leftLength > width) leftLength = width;
            nuint middle = start + leftLength;
            nuint rightLength = count - middle;
            if (rightLength > width) rightLength = width;
            nuint end = middle + rightLength;
            nuint left = start;
            nuint right = middle;
            nuint destination = start;
            while (left < middle && right < end)
            {
                // Equal values retain the order of their original runs.
                if (compare(data + order[left], data + order[right]) <= 0)
                {
                    scratch[destination] = order[left];
                    left++;
                }
                else
                {
                    scratch[destination] = order[right];
                    right++;
                }
                destination++;
            }
            while (left < middle)
            {
                scratch[destination] = order[left];
                left++;
                destination++;
            }
            while (right < end)
            {
                scratch[destination] = order[right];
                right++;
                destination++;
            }
            start = end;
        }
        Span<nuint> previous = order;
        order = scratch;
        scratch = previous;
        if (width >= count - width) break;
        width *= 2;
    }

    // order[newPosition] is the original position. Consume each cycle once,
    // marking completed slots with the identity permutation.
    for (nuint start = 0; start < count; start++)
    {
        if (order[start] == start) continue;
        T temporary = move *(data + start);
        nuint current = start;
        while (order[current] != start)
        {
            nuint next = order[current];
            order[current] = current;
            move *(data + next) at data + current;
            current = next;
        }
        order[current] = current;
        move temporary at data + current;
    }
}
