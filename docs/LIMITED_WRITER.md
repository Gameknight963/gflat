# Bound output through a borrowed writer

`Standard::Text::LimitedWriter` wraps an `ITextWriter*` and accepts at most the
specified number of bytes through that adapter. It borrows the destination for
the lifetime of the adapter.

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

A call exceeding `Remaining` throws `IOException` before accessing input or
calling the sink. It does not truncate, change the allowance, or poison the
adapter, so a fitting write can follow. A zero-byte call is a no-op after checking
failure state, even when the allowance is exhausted. It does not ask the sink to
validate its state. Successful nonempty calls forward once with the original
pointer/count, then deduct that count. There is no buffer allocation on success.
Exceptions use normal runtime storage.

A sink exception propagates unchanged and poisons this adapter, because some
output may already have been emitted. Later writes, including zero, throw
IOException without calling the sink. Remaining retains the allowance after the
last fully successful call. After failure it does not measure how many bytes
physically remain unwritten. There is no retry, rollback or reset.

The cap applies to accepted bytes at this point in the pipeline. A BufferedWriter
can accept them before they reach a device. LimitedWriter does not flush it, even
on destruction. Place the adapter after an encoder if its *encoded* output must
be bounded. Earlier successful writes remain when a later call exceeds the cap.
Whole-frame atomicity is not promised. The caller checks `Remaining == 0` before
starting the next field. Dropping an underfilled adapter does not pad or complete
that field.

For example, passing a LimitedWriter to Base64Encoder bounds the encoded output,
including padding emitted by `Complete()`. Wrapping the encoder instead bounds
its raw input. A rejection during completion can leave an encoded prefix already
accepted. The encoder's own failure state still applies even when LimitedWriter
rejected the call before forwarding it.

Keep the concrete sink and its captured storage alive, usable and unmoved. Input
must satisfy the sink's stability/overlap requirements and must not overlap the
adapter's state. This adapter retains no input pointers. Do not interleave writes
to the underlying sink when enforcing a frame's one allowance.

Copies share the sink but have independent allowances and failure flags. They do
not form a shared cap: two copies with limit 3 can accept six bytes in total. Use
one retained adapter and pass its borrowed ITextWriter* to consumers. Nested
adapters can apply both budgets to the same calls when both remain alive and
unmoved. Any exception from the wrapped writer poisons this adapter, even if that writer
rejected the call before emitting anything.

Source: `Standard/core/Text/LimitedWriter.gf`. Direct dependencies are the core
ITextWriter contract, IO::IOException and its Exception base. BufferWriter bounds a caller memory buffer instead,
while CountingWriter only measures output without forwarding it.
