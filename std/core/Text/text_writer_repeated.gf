namespace std;

// Borrow the writer; stack bytes are consumed synchronously by each Write.
public void WriteRepeated(this ITextWriter* writer, char value, nuint count)
{
    if (count == 0) return;
    char[64] buffer;
    nuint filled = count;
    if (filled > 64) filled = 64;
    for (nuint i = 0; i < filled; i++) buffer[i] = value;
    while (count != 0)
    {
        nuint chunk = count;
        if (chunk > 64) chunk = 64;
        writer.Write(buffer, chunk);
        count -= chunk;
    }
}
