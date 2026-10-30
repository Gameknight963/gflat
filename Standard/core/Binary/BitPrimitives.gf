namespace Standard::Binary;

// Bytes advance in address order. The order controls bit traversal within each
// byte and whether the first visited bit is the field's least or most significant.
public enum BitOrder { LeastSignificantFirst, MostSignificantFirst }

// Stateless borrowed-buffer bit fields, without alignment or allocation needs.
// The read result ref must not overlap the input buffer.
public static class BitPrimitives
{
    public static bool TryReadUInt64(readonly(byte)* data, nuint count, nuint bitOffset,
        int bitCount, BitOrder order, ref ulong value) nothrow
    {
        value = 0ul;
        if (!Fits(count, bitOffset, bitCount, order)) return false;
        nuint firstByte = bitOffset / 8;
        int firstBit = (int)(bitOffset % 8);
        ulong result = 0ul;
        for (int i = 0; i < bitCount; i++)
        {
            int position = firstBit + i;
            int physicalBit = position % 8;
            int valueBit = i;
            if (order == BitOrder::MostSignificantFirst)
            {
                physicalBit = 7 - physicalBit;
                valueBit = bitCount - 1 - i;
            }
            nuint index = firstByte + (nuint)(position / 8);
            ulong bit = ((ulong)data[index] >> physicalBit) & 1ul;
            result = result | (bit << valueBit);
        }
        value = result;
        return true;
    }

    // Value must fit in bitCount bits; all other buffer bits survive.
    // Invalid arguments or an insufficient extent leave every buffer byte intact.
    public static bool TryWriteUInt64(byte* data, nuint count, nuint bitOffset,
        int bitCount, BitOrder order, ulong value) nothrow
    {
        if (!Fits(count, bitOffset, bitCount, order)) return false;
        if (bitCount < 64 && (value >> bitCount) != 0ul) return false;
        nuint firstByte = bitOffset / 8;
        int firstBit = (int)(bitOffset % 8);
        for (int i = 0; i < bitCount; i++)
        {
            int position = firstBit + i;
            int physicalBit = position % 8;
            int valueBit = i;
            if (order == BitOrder::MostSignificantFirst)
            {
                physicalBit = 7 - physicalBit;
                valueBit = bitCount - 1 - i;
            }
            nuint index = firstByte + (nuint)(position / 8);
            uint mask = 1u << physicalBit;
            uint bit = (uint)((value >> valueBit) & 1ul);
            data[index] = (byte)(((uint)data[index] & (255u ^ mask)) | (bit << physicalBit));
        }
        return true;
    }

    private static bool Fits(nuint count, nuint bitOffset, int bitCount, BitOrder order) nothrow
    {
        if (bitCount < 0 || bitCount > 64) return false;
        if (order != BitOrder::LeastSignificantFirst && order != BitOrder::MostSignificantFirst) return false;
        nuint firstByte = bitOffset / 8;
        if (firstByte > count) return false;
        // within+width+7 is at most 78; count is never multiplied by eight.
        nuint requiredBytes = (bitOffset % 8 + (nuint)bitCount + 7) / 8;
        return requiredBytes <= count - firstByte;
    }
}
