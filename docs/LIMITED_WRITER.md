# Bound output through a borrowed writer

`Standard::Text::LimitedWriter` forwards at most a specified byte count to an
`ITextWriter`.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    char[8] storage;
    BufferWriter output = new(storage, 8);
    {
        LimitedWriter payload = new(&output, 3);
        payload.Write("A\0B", 3);
        if (payload.Remaining != 0) return 1;
    }
    output.Write("!", 1);
    if (output.Length != 4 || storage[0] != 'A' || storage[1] != '\0'
        || storage[2] != 'B' || storage[3] != '!' || storage[4] != '\0') return 2;
    return 0;
}
```

## Allowance and errors

`Remaining` starts at the supplied limit and decreases after each successful
write. A call exceeding it throws `IOException` before forwarding anything.
The allowance stays unchanged, so a smaller write can follow. Zero-count writes
check the adapter's failure state and otherwise do nothing.

A destination exception permanently fails the adapter. Later writes throw.
`Remaining` then reflects the last successful call, while the destination may
already have accepted part of the failed write.

## Placing a limit in a pipeline

- Give a LimitedWriter to an encoder to cap encoded output, including final padding
- Wrap an encoder in LimitedWriter to cap raw input
- Check `Remaining == 0` when a field must contain exactly the declared number of bytes

Dropping an underfilled adapter leaves the field underfilled. Earlier writes
remain when a later call exceeds the cap. A buffered destination may have
accepted bytes that still need flushing.

## Lifetimes and copies

Keep the destination alive and at the same address. Input must satisfy the
destination's overlap rules. Use the adapter for every write belonging to its
allowance.

Copies share the destination but have independent budgets. Two copies with
limit three can accept six bytes in total. Pass one adapter by pointer when
several consumers must share a cap.

Source: `core/Text/LimitedWriter.gf`
