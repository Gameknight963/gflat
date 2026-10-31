using Standard;

namespace Console;

// Format primitive values through the current stdout writer.
public void Write(sbyte value) { value.Format(Out); }
public void Write(byte value) { value.Format(Out); }
public void Write(short value) { value.Format(Out); }
public void Write(ushort value) { value.Format(Out); }
public void Write(int value) { value.Format(Out); }
public void Write(uint value) { value.Format(Out); }
public void Write(long value) { value.Format(Out); }
public void Write(ulong value) { value.Format(Out); }
public void Write(nint value) { value.Format(Out); }
public void Write(nuint value) { value.Format(Out); }
public void Write(char value) { value.Format(Out); }
public void Write(bool value) { value.Format(Out); }

public void WriteLine(sbyte value) { value.Format(Out); WriteLine(); }
public void WriteLine(byte value) { value.Format(Out); WriteLine(); }
public void WriteLine(short value) { value.Format(Out); WriteLine(); }
public void WriteLine(ushort value) { value.Format(Out); WriteLine(); }
public void WriteLine(int value) { value.Format(Out); WriteLine(); }
public void WriteLine(uint value) { value.Format(Out); WriteLine(); }
public void WriteLine(long value) { value.Format(Out); WriteLine(); }
public void WriteLine(ulong value) { value.Format(Out); WriteLine(); }
public void WriteLine(nint value) { value.Format(Out); WriteLine(); }
public void WriteLine(nuint value) { value.Format(Out); WriteLine(); }
public void WriteLine(char value) { value.Format(Out); WriteLine(); }
public void WriteLine(bool value) { value.Format(Out); WriteLine(); }

// Borrow formattable owners using the same call syntax as String output.
public void Write<T : IFormattable>(ref immutable(T) value)
{
    value.Format(Out);
}

public void WriteLine<T : IFormattable>(ref immutable(T) value)
{
    value.Format(Out);
    WriteLine();
}
