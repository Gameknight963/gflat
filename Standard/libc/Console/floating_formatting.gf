using Standard;

namespace Console;

// Floating formatting follows the current C locale.
public void Write(float value) { value.Format(Out); }
public void Write(double value) { value.Format(Out); }
public void WriteLine(float value) { value.Format(Out); WriteLine(); }
public void WriteLine(double value) { value.Format(Out); WriteLine(); }
