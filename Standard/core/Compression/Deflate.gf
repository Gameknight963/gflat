namespace Standard::Compression;

/// Decodes one raw RFC 1951 stream into caller-owned storage, without allocating.
/// This is a one-shot operation; counts report bytes examined and emitted, not resumable state.
public static class Deflate
{
    public enum Status { Done, NeedInput, OutputTooSmall, InvalidData }

    private static const byte[19] CodeOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
    private static const ushort[29] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static const byte[29] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static const ushort[30] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static const byte[30] DistanceExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    private struct BitReader
    {
        private readonly(byte)* source;
        private nuint length;
        private nuint position;
        private uint bits;
        private int available;

        public BitReader(readonly(byte)* input, nuint count) nothrow
        {
            source = input; length = count; position = 0; bits = 0u; available = 0;
        }
        public nuint Position { immutable get nothrow => position; }
        public nuint Remaining { immutable get nothrow => length - position; }
        // Internal widths are 0..16. At most seven unused bits remain after success.
        public bool Read(int width, ref uint value) nothrow
        {
            value = 0u;
            while (available < width)
            {
                if (position == length) return false;
                bits = bits | ((uint)source[position] << available);
                position++; available += 8;
            }
            value = bits & ((1u << width) - 1u);
            bits = bits >> width;
            available -= width;
            return true;
        }
        public void Align() nothrow { bits = 0u; available = 0; }
    }

    private struct Huffman
    {
        private ushort[16] counts;
        private ushort[288] symbols;
        private int maximum;
        private nuint total;

        public Huffman() nothrow
        {
            for (nuint i = 0; i < 16; i++) counts[i] = 0;
            for (nuint i = 0; i < 288; i++) symbols[i] = 0;
            maximum = 0; total = 0;
        }

        public bool Build(readonly(byte)* lengths, nuint count, bool allowEmpty, bool allowSingleton) nothrow
        {
            if (count > 288) return false;
            for (nuint i = 0; i < 16; i++) counts[i] = 0;
            maximum = 0; total = 0;
            for (nuint i = 0; i < count; i++)
            {
                nuint width = (nuint)lengths[i];
                if (width > 15) return false;
                counts[width]++;
                if (width != 0)
                {
                    total++;
                    if ((int)width > maximum) maximum = (int)width;
                }
            }
            if (total == 0) return allowEmpty;
            int unused = 1;
            for (nuint width = 1; width <= 15; width++)
            {
                unused = (unused << 1) - (int)counts[width];
                if (unused < 0) return false;
            }
            if (unused != 0 && !(allowSingleton && total == 1 && counts[1] == 1)) return false;
            ushort[16] offsets;
            for (nuint i = 0; i < 16; i++) offsets[i] = 0;
            for (nuint width = 1; width < 15; width++)
                offsets[width + 1] = (ushort)((uint)offsets[width] + (uint)counts[width]);
            for (nuint symbol = 0; symbol < count; symbol++)
            {
                nuint width = (nuint)lengths[symbol];
                if (width != 0)
                {
                    symbols[(nuint)offsets[width]] = (ushort)symbol;
                    offsets[width]++;
                }
            }
            return true;
        }

        public immutable Status Decode(ref BitReader input, ref uint value) nothrow
        {
            value = 0u;
            // Empty distance trees cannot supply a match; do not consume input for them.
            if (maximum == 0) return Status::InvalidData;
            uint code = 0u; uint first = 0u; nuint index = 0;
            for (int width = 1; width <= maximum; width++)
            {
                uint bit = 0u;
                if (!input.Read(1, ref bit)) return Status::NeedInput;
                code = (code << 1) | bit;
                uint count = (uint)counts[(nuint)width];
                if (code >= first && code - first < count)
                {
                    nuint at = index + (nuint)(code - first);
                    if (at >= total) return Status::InvalidData;
                    value = (uint)symbols[at];
                    return Status::Done;
                }
                index += (nuint)count;
                first = (first + count) << 1;
            }
            return Status::InvalidData;
        }
    }

    /// Borrows disjoint source/output buffers and disjoint count outputs until return.
    /// Done stops after the final block; whole trailing bytes remain unconsumed.
    /// Final-byte padding is ignored. All other statuses leave a possibly partial prefix.
    /// Retrying requires the complete input and a fresh output pass; no state is retained.
    /// Accepts the common duplicate length-258 encoding (code 284 plus extra value 31).
    public static Status Decompress(readonly(byte)* source, nuint count, byte* output, nuint capacity,
        ref nuint consumed, ref nuint written) nothrow
        => DecompressWithWindow(source, count, output, capacity, 32768, ref consumed, ref written);

    // Container framing may declare a smaller history window; no extra storage is needed.
    internal static Status DecompressWithWindow(readonly(byte)* source, nuint count, byte* output, nuint capacity,
        nuint maximumDistance, ref nuint consumed, ref nuint written) nothrow
    {
        consumed = 0; written = 0;
        if (maximumDistance == 0 || maximumDistance > 32768) return Status::InvalidData;
        BitReader input = new(source, count);
        nuint produced = 0;
        defer { consumed = input.Position; written = produced; }
        Huffman literals = new();
        Huffman distances = new();
        while (true)
        {
            uint header = 0u;
            if (!input.Read(3, ref header)) return Status::NeedInput;
            bool final = (header & 1u) != 0u;
            uint kind = header >> 1;
            Status status = Status::InvalidData;
            if (kind == 0u) status = Stored(ref input, output, capacity, ref produced);
            else if (kind == 1u)
            {
                Fixed(ref literals, ref distances);
                status = Codes(ref input, ref literals, ref distances, output, capacity, maximumDistance, ref produced);
            }
            else if (kind == 2u)
            {
                status = Dynamic(ref input, ref literals, ref distances);
                if (status == Status::Done)
                    status = Codes(ref input, ref literals, ref distances, output, capacity, maximumDistance, ref produced);
            }
            if (status != Status::Done) return status;
            if (final) return Status::Done;
        }
    }

    private static Status Stored(ref BitReader input, byte* output, nuint capacity, ref nuint produced) nothrow
    {
        input.Align();
        uint length = 0u; uint complement = 0u;
        if (!input.Read(16, ref length) || !input.Read(16, ref complement)) return Status::NeedInput;
        if ((length ^ complement) != 65535u) return Status::InvalidData;
        if ((nuint)length > input.Remaining) return Status::NeedInput;
        if ((nuint)length > capacity - produced) return Status::OutputTooSmall;
        for (uint i = 0u; i < length; i++)
        {
            uint value = 0u;
            if (!input.Read(8, ref value)) return Status::NeedInput;
            output[produced] = (byte)value; produced++;
        }
        return Status::Done;
    }

    private static void Fixed(ref Huffman literals, ref Huffman distances) nothrow
    {
        byte[288] lengths;
        for (nuint i = 0; i < 288; i++)
        {
            if (i < 144) lengths[i] = 8;
            else if (i < 256) lengths[i] = 9;
            else if (i < 280) lengths[i] = 7;
            else lengths[i] = 8;
        }
        literals.Build(lengths, 288, false, false);
        for (nuint i = 0; i < 32; i++) lengths[i] = 5;
        distances.Build(lengths, 32, false, false);
    }

    private static Status Dynamic(ref BitReader input, ref Huffman literals, ref Huffman distances) nothrow
    {
        uint bits = 0u;
        if (!input.Read(5, ref bits)) return Status::NeedInput;
        nuint literalCount = (nuint)bits + 257;
        if (literalCount > 286) return Status::InvalidData;
        if (!input.Read(5, ref bits)) return Status::NeedInput;
        nuint distanceCount = (nuint)bits + 1;
        if (!input.Read(4, ref bits)) return Status::NeedInput;
        nuint codeCount = (nuint)bits + 4;
        byte[19] codeLengths;
        for (nuint i = 0; i < 19; i++) codeLengths[i] = 0;
        for (nuint i = 0; i < codeCount; i++)
        {
            if (!input.Read(3, ref bits)) return Status::NeedInput;
            codeLengths[(nuint)CodeOrder[i]] = (byte)bits;
        }
        Huffman codeTree = new();
        if (!codeTree.Build(codeLengths, 19, false, false)) return Status::InvalidData;
        byte[320] lengths;
        nuint total = literalCount + distanceCount;
        nuint position = 0;
        byte previous = 0;
        while (position < total)
        {
            uint symbol = 0u;
            Status status = codeTree.Decode(ref input, ref symbol);
            if (status != Status::Done) return status;
            if (symbol < 16u)
            {
                previous = (byte)symbol;
                lengths[position] = previous; position++;
                continue;
            }
            nuint run = 0;
            if (symbol == 16u)
            {
                if (position == 0) return Status::InvalidData;
                if (!input.Read(2, ref bits)) return Status::NeedInput;
                run = (nuint)bits + 3;
            }
            else if (symbol == 17u)
            {
                if (!input.Read(3, ref bits)) return Status::NeedInput;
                previous = 0; run = (nuint)bits + 3;
            }
            else if (symbol == 18u)
            {
                if (!input.Read(7, ref bits)) return Status::NeedInput;
                previous = 0; run = (nuint)bits + 11;
            }
            else return Status::InvalidData;
            if (run > total - position) return Status::InvalidData;
            for (nuint i = 0; i < run; i++) { lengths[position] = previous; position++; }
        }
        if (lengths[256] == 0) return Status::InvalidData;
        if (!literals.Build(lengths, literalCount, false, true)
            || !distances.Build((byte*)lengths + literalCount, distanceCount, true, true)) return Status::InvalidData;
        return Status::Done;
    }

    private static Status Codes(ref BitReader input, ref Huffman literals, ref Huffman distances,
        byte* output, nuint capacity, nuint maximumDistance, ref nuint produced) nothrow
    {
        while (true)
        {
            uint symbol = 0u;
            Status status = literals.Decode(ref input, ref symbol);
            if (status != Status::Done) return status;
            if (symbol == 256u) return Status::Done;
            if (symbol < 256u)
            {
                if (produced == capacity) return Status::OutputTooSmall;
                output[produced] = (byte)symbol; produced++;
                continue;
            }
            if (symbol > 285u) return Status::InvalidData;
            nuint index = (nuint)(symbol - 257u);
            uint extra = 0u;
            if (!input.Read((int)LengthExtra[index], ref extra)) return Status::NeedInput;
            uint length = (uint)LengthBase[index] + extra;
            status = distances.Decode(ref input, ref symbol);
            if (status != Status::Done) return status;
            if (symbol > 29u) return Status::InvalidData;
            index = (nuint)symbol;
            if (!input.Read((int)DistanceExtra[index], ref extra)) return Status::NeedInput;
            nuint distance = (nuint)DistanceBase[index] + (nuint)extra;
            if (distance > produced || distance > maximumDistance) return Status::InvalidData;
            if ((nuint)length > capacity - produced) return Status::OutputTooSmall;
            for (uint i = 0u; i < length; i++)
            {
                output[produced] = output[produced - distance]; produced++;
            }
        }
    }
}
