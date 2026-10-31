namespace Standard;

// Creates version 4 UUIDs using a supplied random-byte callback.
public static class UuidGenerator
{
    // Calls fill once with a temporary 16-byte buffer. True requires all bytes filled.
    // The callback must not retain the buffer. False resets result to Nil.
    public static bool TryCreateVersion4(bool(byte*, nuint)* fill, ref Uuid result)
    {
        byte[16] bytes;
        if (!fill(bytes, 16))
        {
            result = new Uuid();
            return false;
        }
        bytes[6] = (byte)((bytes[6] & 15) | 64);
        bytes[8] = (byte)((bytes[8] & 63) | 128);
        Uuid generated = new();
        Uuid::TryFromBytes(bytes, 16, &generated);
        result = generated;
        return true;
    }

    // Returns a version 4 UUID, throwing Exception if fill returns false.
    public static Uuid CreateVersion4(bool(byte*, nuint)* fill)
    {
        Uuid result = new();
        if (!TryCreateVersion4(fill, ref result))
            throw new Exception("UUID random source failed");
        return result;
    }
}
