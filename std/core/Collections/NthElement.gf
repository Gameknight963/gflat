namespace std::Collections;

// In-place rank selection. Elements within either partition need not be sorted.
public void NthElement<T>(Span<T> values, nuint nth,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint length = values.Length;
    if (nth > length) throw new Exception("Selection index out of range", 3);
    if (nth == length || length < 2) return;

    nuint budget = 0;
    for (nuint remaining = length; remaining > 1; remaining /= 2) budget += 2;
    nuint first = 0;
    nuint last = length;
    while (last - first > 1)
    {
        if (budget == 0)
        {
            std::Collections::Sort<T>(values.Slice(first, last - first), compare);
            return;
        }
        budget--;
        nuint pivot = first + (last - first) / 2;
        nuint lower = first;
        nuint current = first;
        nuint upper = last;
        while (current < upper)
        {
            int order = compare(&values[current], &values[pivot]);
            if (order < 0)
            {
                std::Collections::Swap<T>(ref values[current], ref values[lower]);
                if (pivot == current) pivot = lower;
                else if (pivot == lower) pivot = current;
                lower++; current++;
            }
            else if (order > 0)
            {
                upper--;
                std::Collections::Swap<T>(ref values[current], ref values[upper]);
                if (pivot == current) pivot = upper;
                else if (pivot == upper) pivot = current;
            }
            else current++;
        }
        if (nth < lower) last = lower;
        else if (nth >= upper) first = upper;
        else return;
    }
}
