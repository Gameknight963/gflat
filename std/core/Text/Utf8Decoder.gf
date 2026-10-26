namespace std::Text;

// Incremental strict UTF-8 decoding. No input pointer is retained between calls.
public struct Utf8Decoder
{
    public enum Status { Scalar, NeedMore, End, Invalid }

    private uint value;
    private int remaining;
    private uint nextMinimum;
    private uint nextMaximum;
    private bool failed;
    private bool finished;

    public Utf8Decoder() nothrow { this.Reset(); }

    public void Reset() nothrow
    {
        value = 0u; remaining = 0;
        nextMinimum = 0x80u; nextMaximum = 0xbfu;
        failed = false; finished = false;
    }

    // Produces at most one scalar. consumed counts bytes examined in THIS input,
    // including an offending byte on Invalid. Scalar is zero for other statuses.
    // final reports that no more bytes follow this chunk. Call again with its
    // remaining bytes after Scalar; End and Invalid persist until Reset().
    public Status Read(readonly(char)* input, nuint count, bool final, uint* scalar, nuint* consumed) nothrow
    {
        *scalar = 0u; *consumed = 0;
        if (failed) return Status::Invalid;
        if (finished) return Status::End;
        while (*consumed < count)
        {
            uint next = (uint)(byte)input[*consumed];
            (*consumed)++;
            if (remaining == 0)
            {
                if (next < 0x80u) { *scalar = next; return Status::Scalar; }
                nextMinimum = 0x80u; nextMaximum = 0xbfu;
                if (next >= 0xc2u && next <= 0xdfu)
                {
                    value = next & 0x1fu; remaining = 1;
                }
                else if (next >= 0xe0u && next <= 0xefu)
                {
                    value = next & 0x0fu; remaining = 2;
                    if (next == 0xe0u) nextMinimum = 0xa0u;
                    if (next == 0xedu) nextMaximum = 0x9fu;
                }
                else if (next >= 0xf0u && next <= 0xf4u)
                {
                    value = next & 0x07u; remaining = 3;
                    if (next == 0xf0u) nextMinimum = 0x90u;
                    if (next == 0xf4u) nextMaximum = 0x8fu;
                }
                else { failed = true; return Status::Invalid; }
            }
            else
            {
                if (next < nextMinimum || next > nextMaximum)
                {
                    failed = true; return Status::Invalid;
                }
                value = (value << 6) | (next & 0x3fu);
                remaining--;
                nextMinimum = 0x80u; nextMaximum = 0xbfu;
                if (remaining == 0) { *scalar = value; return Status::Scalar; }
            }
        }
        if (!final) return Status::NeedMore;
        if (remaining != 0) { failed = true; return Status::Invalid; }
        finished = true;
        return Status::End;
    }
}
