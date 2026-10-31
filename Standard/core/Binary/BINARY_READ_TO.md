# Bounded delimiter fields

`BinaryBufferReader.TryReadTo(delimiter, ref slice)` borrows the bytes before the
first matching byte in the remaining input. It advances the parent past the
delimiter and starts the child at position zero.

```csharp
using Standard::Binary;

int main() nothrow
{
    byte[4] bytes = [(byte)65, (byte)0, (byte)255, (byte)0];
    BinaryBufferReader reader = new(bytes, 4);
    BinaryBufferReader field = new();
    if (!reader.TryReadTo((byte)0, ref field) || field.Length != 1 || reader.Position != 2) return 1;
    byte first = 0;
    if (!field.TryReadByte(ref first) || first != (byte)65 || reader.Position != 2) return 2;
    return 0;
}
```

## Results

- A match returns true. `slice` contains the preceding bytes
- A delimiter at the current position produces an empty slice with null `Data`
- A missing delimiter returns false, leaves the parent position unchanged, and
  resets `slice` to empty
- Empty input returns false without reading storage

Any byte can be a delimiter, including zero and 255. The search is bounded by the
reader's length and allocates nothing. Repeating a failed call searches the same
remaining bytes again.

## Storage lifetime

Keep the child object separate from the parent and captured source bytes. Both
readers borrow the same storage, which must remain alive while either is used.
Their cursor positions are independent. Reading the child leaves the parent at
the byte after the delimiter.
