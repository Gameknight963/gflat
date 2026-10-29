namespace std::IO;

/// Synchronous source of counted bytes; reading never transfers source or buffer ownership.
/// For a positive request, zero means EOF and a short positive read is normal.
/// No terminator, decoding or line processing is added. Errors may consume an unknown prefix.
public interface IByteReader
{
    /// Writes at most count bytes to caller storage and returns the number written.
    /// Zero count consumes nothing and must not dereference data; state validation may throw.
    /// The writable buffer must not overlap the reader's state or retained backing storage.
    /// Storage is borrowed only until this call returns and must not be retained.
    nuint Read(char* data, nuint count);
}
