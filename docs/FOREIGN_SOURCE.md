# C and C++ source sections

Use `#lang c` or `#lang cpp` to switch to C or C++. Use `#lang gflat` to
resume the host language. Each root file starts in the host language. EOF
finishes the current section, so a final switch back is optional.

```gflat
#lang cpp
#include <string>

int GreetingLength()
{
    return (int)std::string("hello").size();
}

#lang gflat
int main()
{
    if (GreetingLength() != 5) return 1;
    return 0;
}
```

Clang parses and compiles the foreign source. Its headers and libraries must be
installed for the host target. `--clang` and `GFLAT_CLANG` select the driver.
Clang's default C/C++ language standard is used. Checking a file containing
foreign sections also requires Clang.

## Source and preprocessing

Switches must appear as directives on their own logical lines. Foreign sections
start at file scope. Function bodies, class bodies, and block namespaces cannot
contain a switch out of the host language.

The compiler preserves foreign source, including comments, raw strings,
continuations, and preprocessing directives. A `#lang` spelling inside a comment,
string, or continued macro body does not switch languages. Actual language
directives are recognized before foreign conditional preprocessing, including
inside a foreign `#if` branch. Keep a foreign conditional within one section.

`#include` remains a textual include. In foreign sections Clang resolves and
processes C/C++ headers from disk. Quoted includes search the source directories;
project include directories and command-line definitions also reach Clang.
Included host headers can themselves contain language switches.

Sections using the same foreign language in one root file form one translation
unit, in source order. Their foreign macros and declarations persist across
switches back to the host language. C and C++ have separate translation units,
and foreign source-level macros do not enter the host macro environment.

## Calling imported functions

The initial importer exposes free functions using primitive scalars and pointers
to supported scalars or `void`. Scalar typedefs, C++ namespaces, and overloads with distinct imported
parameter types are supported. Pointers are nullable and retain pointee `const`
as `readonly`. Ownership and native lifetimes remain the caller's responsibility.

Function-pointer parameters support callbacks with scalar and raw-pointer
signatures, including nested callback parameters and callback typedefs. Pass an
ordinary G♭ function pointer. Native callback pointers are nullable. Keep the
callback and any context storage alive while native code can invoke them.
Captured managed callbacks, variadic callbacks, and non-default native calling
conventions are not supported.

Functions are called by their declared names without handwritten foreign
declarations. The compiler generates native ABI entry points as needed. Only
entry points referenced by the emitted program are compiled.

The following are not imported yet:

- Record layouts, classes, methods, constructors, references, and function-pointer return types
- Templates, default arguments, globals, enum declarations, macro values, and namespace aliases
- Overload families that differ only in qualifiers erased by the host type system
- Variadic C++ functions without C linkage

C-linkage variadic functions are imported directly. Foreign functions cannot
participate in host constant evaluation.

## Exceptions across native calls

C++ exceptions unwind through G♭ calls and cleanup. A catch-all can retain and
rethrow the original native exception. A typed G♭ catch matches G♭ payloads,
not arbitrary C++ exception classes.

C++ callers can catch a G♭ exception as `const std::exception&`. Its `what()`
uses the exception's `GetMessage()` method, including virtual overrides. A null
result, missing compatible getter, or escaping getter exception produces
`"Gflat exception"`. `what()` is `noexcept`. The exception retains its payload
while C++ holds it, including through `std::exception_ptr`. Borrowed message
storage must remain alive for that duration. `GetCode()` remains available to
G♭ callers.

This adapter does not expose G♭ class layouts as C++ exception classes.
Typed G♭ catches across separately compiled binary libraries are not yet
supported. Code using Windows native libraries must select a compatible CRT
as described in [project references](PROJECTS.md#visual-c-project-references).

## Declare a separately linked C++ function

Use `extern "C++"` when its definition is supplied by a native library:

```cpp
namespace Native
{
    extern "C++" int Answer(int value);
}
```

Clang supplies its mangled symbol using the configured native target. Calls and
function addresses use that symbol. The initial explicit-linkage form accepts
scalar types and raw pointers. Host `long` and `ulong` map to C++ `long long` and
`unsigned long long`, including on Linux. For native declarations using C++
`long`, consume the original declaration in a `#lang cpp` section. `readonly(T)*`
maps to a pointer to `const T`. Native libraries still need to be linked through
the project's native library inputs.

Bare `extern` retains host-language mangling. `extern "C"` uses C linkage.

## Editor and emitted artifacts

Foreign sections use lexical C/C++ highlighting through TextMate grammars when
the editor supplies them. Visual Studio's C++ workload supplies these grammars.
Host semantic coloring, completion, navigation, and formatting leave the foreign
body alone. Imported functions have host call-site IntelliSense. Native C++
completion, type-aware highlighting, and refactoring inside foreign sections
are not provided.

`--emit-ir` writes the host LLVM module and a `.foreign.json` companion file
containing foreign translation units and generated entry points. Keep these
files together when using `NativeToolchain.Compile`, which reads the companion
automatically. Compiler API clients use `Compiler.EmitProgram` and pass its
`ForeignUnits` alongside the IR to the native toolchain.
