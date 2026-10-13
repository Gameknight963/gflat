# libc-dependent facilities

`ExceptionHandling.gf` supplies the automatically included weak
`Exceptions::Unhandled(Exception*? error)` hook. Its default `void` implementation calls libc `exit` with the exception's
`GetCode()` (1 by default), or 1 for a foreign exception, without printing.
Replace it to report the error and terminate explicitly. If it returns or throws,
the runtime exits with status 1.

`Formatting.gf` supplies `std::Format(float, ITextWriter*)` and
`std::Format(double, ITextWriter*)` through `snprintf`, using 9 and 17 significant
digits respectively. Formatting follows the application's current C locale.
The result is written from a stack buffer; there is no intermediate heap string.

Supply only core sources when these facilities are unwanted. `std.gfproj`
currently includes both directories.

`Memory.gf` supplies the automatically included weak `Memory::Allocate` and
`Memory::Free` implementations using libc. Replace both functions to provide a
custom allocator. This default is kept in libc even though the core library calls
the abstracted memory hooks; libc remains an unconditional dependency for now.
