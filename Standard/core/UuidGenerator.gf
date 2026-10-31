namespace Standard;

/// Creates version 4 UUIDs using an explicitly supplied random-byte callback.
/// Successful calls use a fixed stack buffer.
public static class UuidGenerator
{
    /// Calls fill once with a temporary writable 16-byte buffer. True requires all bytes filled.
    /// The buffer is valid only during the callback and must not be retained.
    /// False discards partial bytes and writes Nil. Callback exceptions propagate.
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

    /// Returns a version 4 UUID or throws Exception when fill returns false.
    /// Callback exceptions propagate unchanged.
    public static Uuid CreateVersion4(bool(byte*, nuint)* fill)
    {
        Uuid result = new();
        if (!TryCreateVersion4(fill, ref result))
            throw new Exception("UUID random source failed");
        return result;
    }
}
