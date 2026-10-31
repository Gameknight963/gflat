# Bounded delimiter fields

`BinaryBufferReader.TryReadTo(byte delimiter, ref BinaryBufferReader slice)` borrows the bytes before the first delimiter in the remaining counted input. It excludes the delimiter from the child and always consumes it in the parent. It allocates nothing and adds no text decoding or ownership transfer.

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

The delimiter can be any byte, including zero and 255. No Unicode, CRLF, escaping, multi-byte delimiter, or C-string validation is implied. This is useful for required terminated fields inside a captured binary table: the table's length bounds the entire search.

When found, the child starts at Position=0 and contains only the preceding bytes. A delimiter at the current position succeeds with an empty child, Length=0 and Data=null, exactly like `TryReadSlice(0)`, while the parent advances one byte. The empty child is a counted empty view, not a C-string pointer retaining the terminator.

When absent, the operation returns false, preserves parent Position/Remaining, and resets the output child to default empty, including Position=0 and Data=null. Repeating that failed call repeats the bounded search; there is no retained continuation state. Empty remaining input returns false without dereferencing its buffer.

Keep the child object disjoint from the parent object and all captured source bytes. Parent and child borrow the same storage; neither owns or extends its lifetime. Keep that source alive and valid while either reader is used. Copies have independent cursor positions over the same bytes. A child read never advances the parent, and the parent can continue immediately after the delimiter.
