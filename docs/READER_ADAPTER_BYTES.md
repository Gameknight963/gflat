# Byte buffers in reader adapters

`BufferedReader` and `LimitedReader` accept `byte*` output in `Read`.
The borrowed-scratch BufferedReader constructor also accepts `byte*` storage.

```csharp
using Standard::IO;

struct Input : IByteReader
{
    public nuint Read(char* output, nuint count)
    {
        for (nuint i = 0; i < count; i++) output[i] = (char)255;
        return count;
    }
}

int main()
{
    Input input = new();
    byte[16] scratch;
    BufferedReader buffered = new(&input, &scratch[0], 16);
    LimitedReader limited = new(&buffered, 3);
    byte[3] output;
    if (limited.Read(&output[0], 3) != 3 || output[2] != (byte)255) return 1;
    return 0;
}
```

Borrowed scratch stays alive and unmoved until the buffered adapter is destroyed.
Keep output, scratch, source storage, and adapter state independent. BufferedReader
can read ahead, so continue through that adapter to consume retained bytes.
LimitedReader tracks a byte allowance and may reach EOF before exhausting it.

Byte overloads preserve existing zero-read and failure-state behavior. A failed
source prevents further reads. BufferedReader validates its source on a zero-byte
read, while LimitedReader returns zero without consulting the source when the
count or allowance is zero. Borrowed scratch requires positive capacity and does
not allocate adapter storage.
