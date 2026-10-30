namespace Standard::Text;

// Incremental strict UTF-16 decoding over host ushort code units.
// No input pointer is retained between calls.
public struct Utf16Decoder
{
    public enum Status { Scalar, NeedMore, End, Invalid }

    private uint high;
    private bool failed;
    private bool finished;

    public Utf16Decoder() nothrow { this.Reset(); }

    public void Reset() nothrow
    {
        high = 0u; failed = false; finished = false;
    }

    // Produces at most one scalar. consumed counts code units in THIS chunk,
    // including an offending unit on Invalid. Scalar is zero for other statuses.
    // After Scalar, call again with remaining units, even when final is true.
    // Outputs must be disjoint from each other, input storage, and decoder state.
    public Status Read(readonly(ushort)* input, nuint count, bool final,
        uint* scalar, nuint* consumed) nothrow
    {
        *scalar = 0u; *consumed = 0;
        if (failed) return Status::Invalid;
        if (finished) return Status::End;
        while (*consumed < count)
        {
            uint next = (uint)input[*consumed];
            (*consumed)++;
            if (high != 0u)
            {
                if (next < 0xdc00u || next > 0xdfffu)
                {
                    failed = true; return Status::Invalid;
                }
                *scalar = 0x10000u + ((high - 0xd800u) << 10) + next - 0xdc00u;
                high = 0u;
                return Status::Scalar;
            }
            if (next < 0xd800u || next > 0xdfffu)
            {
                *scalar = next; return Status::Scalar;
            }
            if (next > 0xdbffu) { failed = true; return Status::Invalid; }
            high = next;
        }
        if (!final) return Status::NeedMore;
        if (high != 0u) { failed = true; return Status::Invalid; }
        finished = true;
        return Status::End;
    }
}
