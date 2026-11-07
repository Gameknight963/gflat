# Choosing random items

Load `Standard/core/Numerics/pseudo_random_items.gf` for `PseudoRandom.GetItems`.
It samples with replacement, so an item may appear more than once.

```csharp
using Standard::Collections;
using Standard::Memory;
using Standard::Numerics;

int main()
{
    int[3] choices = [10, 20, 30];
    Span<int> view = new(&choices[0], 3);
    PseudoRandom random = new(42ul);
    Vector<int> selected = random.GetItems(view.AsImmutable(), (nuint)12);
    if (selected.Count != 12) return 1;
    int[4] buffer = default(int[4]);
    random.GetItems(view.AsImmutable(), new Span<int>(&buffer[0], 4));
    for (nuint i = 0; i < 4; i++)
    {
        if (buffer[i] != 10 && buffer[i] != 20 && buffer[i] != 30) return 2;
    }
    return 0;
}
```

The span overload replaces existing initialized elements. The length overload
returns a new owning `Vector<T>`. Both copy selected elements and require them
to be copyable. Freestanding forms take `ref PseudoRandom` as the first argument.
Pointer elements copy pointers without owning their pointees.

Choices must be nonempty even when no output is requested. Empty output consumes
no random words. Each selected index uses the generator's unbiased bounded
`NextULong`, including for a single choice. Results are deterministic for the
same seed and sequence of calls, but do not match `System.Random`'s generator.
This generator is not suitable for cryptographic use.

The span overload writes from left to right. Overlapping choices and destination
observe earlier replacements when selecting later items. Each copy completes
before replacing its destination element. On a copy failure, the completed
prefix remains replaced and later elements remain unchanged. The generator
has already advanced for the failed selection. Structural mutation of choices
from a copy constructor is not supported.

The vector overload reserves before drawing. If allocation fails, no draws
occur. A later copy failure destroys the partial vector while retaining the
advanced generator state. The span overload allocates only if element copies do.
