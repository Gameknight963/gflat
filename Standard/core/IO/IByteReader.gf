namespace Standard::IO;

// Synchronous source of counted bytes.
// A positive request returning zero means EOF. Short positive reads can continue.
// An error may leave input consumed.
public interface IByteReader
{
    // Writes at most count bytes and returns the number written.
    // Zero count consumes nothing and leaves data untouched, but may validate source state.
    // Data is borrowed for this call and must be separate from reader state and backing storage.
    nuint Read(char* data, nuint count);
}
