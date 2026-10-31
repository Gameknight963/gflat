# Sliding windows over borrowed spans

`span.Windows(width, step = 1)` yields full-width `Span<T>` windows:

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

int[5] samples = [1, 3, 5, 7, 9];
Span<int> values = new(samples, 5);
foreach (Span<int> pair in values.Windows(2)) {
    int difference = pair[1] - pair[0];
}
```

Starts are zero, step, twice step, and so on while a complete window fits.
Width and step must be positive. An oversized width produces no windows.
For a shorter final piece, use [Chunks](SPAN_CHUNKS.md).

The result is SpanWindows<T>. Windows preserve element qualifications. Overlapping
writable windows share elements, so an edit can affect a later window. Keep the
backing storage alive for every retained window and iterator.

AsEnumerable enables queries over windows:

```csharp
using Standard::Memory;

int Average(Span<readonly(int)> window)
    => (window[0] + window[1] + window[2]) / 3;

var averages = values.AsImmutable().Windows(3).AsEnumerable().Select(Average);
```

Source: `core/Collections/SpanWindows.gf`
