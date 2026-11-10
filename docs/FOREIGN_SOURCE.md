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

The importer exposes free functions using primitive scalars and pointers
to supported scalars, imported records, or `void`. Native typedefs are resolved
in imported signatures. They do not yet introduce aliases in host code.
C++ namespaces and overloads with distinct imported parameter types are supported.
Native `static` free functions are visible only in their owning root file.
Free functions in C++ anonymous namespaces follow the same rule unless they
have C language linkage.
Including a header in multiple root files creates a separate instance of its
static functions and state in each native translation unit.
Pointers are nullable and retain pointee `const`
as `readonly`, including `const void*`. Ownership and native lifetimes remain the
caller's responsibility.
Native `restrict`, `__restrict`, and `__restrict__` qualifiers do not add host
pointer modifiers. Native aliasing requirements still apply.
Trailing return declarations such as `auto Read() -> int` use their declared result type.

C++ Unicode character types keep their code-unit storage: `char8_t` maps to
`byte`, `char16_t` to `ushort`, and `char32_t` to `uint`. `wchar_t` follows Clang's
chosen integer width and signedness, including `-fshort-wchar`.

C++ lvalue-reference parameters use host `ref` parameters. Pass `ref value` for
`T&` or `const T&`, including references to opaque native objects. `const`
references preserve readonly access. Mutable references require mutable,
addressable storage. Lvalue-reference results also borrow native storage. Read
them as values, assign through a mutable result, pass `ref Function()`, or take
`&Function()` to retain its address. Copying a borrowed object uses its native
copy constructor. Const results retain readonly access. Keep the referenced
object alive for every use of the result or its address.
Rvalue-reference parameters and references in callback signatures are not
imported yet.

Function-pointer parameters and results support callbacks with scalar and raw-pointer
signatures, including nested callback parameters and callback typedefs. Native
methods can return callbacks directly or through a typedef. Pass an
ordinary G♭ function pointer. Native callback pointers are nullable. Keep the
callback and any context storage alive while native code can invoke them.
Captured managed callbacks, variadic callbacks, and non-default native calling
conventions are not supported.

Functions are called by their declared names without handwritten foreign
declarations. The compiler generates native ABI entry points as needed. Only
entry points referenced by the emitted program are compiled.

## Imported enums

Named C++ enums with a fixed underlying integer type are imported as host enums,
including scoped enums whose underlying type defaults to `int`. Their constants
and storage width come from Clang. Use `EnumType::Member` in host code. These
enums can cross native function and method calls by value, pointer, or lvalue
reference. Enums whose storage type is selected by the native compiler are not
imported yet.

## Imported records

Named C structs and trivial C++ structs or classes with public scalar and pointer
fields are available as host structs. Their size, alignment, and field offsets
must match Clang's layout for the active target. Native code and host code can
read or write the same storage through pointers. Pointee and field `const`
qualifiers remain readonly. Native pointer results remain borrowed and nullable.

Public C++ instance methods with supported scalar, record, and pointer signatures are
called as ordinary host members. Static methods use `Type::Method(...)`.
A `const` method accepts readonly receivers.
Other methods require mutable storage. Native methods take precedence over host
extensions with the same name. Overloads with distinct parameter types work.
Private methods, reference-qualified methods, overload families mixing static
and instance methods, and overloads that differ only in receiver qualifiers are
not imported yet.

This representation requires trivial construction, copying, and destruction.
Records with private fields, bases, virtual members, explicit constructors or
destructors, bitfields, arrays, nested record fields, or layout attributes are
exposed as opaque native types instead. Their value support is described below.
Anonymous typedef records are not imported yet. Record arguments
and results passed by value use compiler-generated native bridges. Native calls
receive independent copies, and a throwing call does not publish a partial
record result. By-value records in callback signatures are not supported.
A binary library's generated interface does not export these types yet.

## Native-owned classes

Other complete named native records, classes, and unions are available through raw
pointers and borrowed host references. Their fields remain opaque. Public
methods work through native entry points, including virtual dispatch and methods
inherited through public bases. Native calls perform base-pointer adjustment.
Hidden names and names reached through multiple direct bases are not imported.
Using-declarations do not reintroduce hidden overloads yet. `sizeof` and `alignof`
report Clang's native layout.

Forward-declared named records, such as `struct Handle;`, can be passed through
pointers and borrowed references. Their layout is unknown, so `sizeof`, `alignof`,
indexing, and pointer arithmetic are unavailable. Use the native library's creation
and destruction functions. A complete definition in the native translation unit supplies
the layout and the supported members instead.

Unions are always opaque. Pass their pointers or borrowed references to native
functions to read or change the active member. Host field access is not supported.

For a C++ class with a supported public constructor, `new* Type(arguments)` uses
C++ allocation and construction. `delete pointer` invokes C++ deletion when the
native destructor is public. Virtual destruction stays in C++. Constructor
failures unwind through the host's cleanup and catch-all handlers. `Unique<T>`
can own these objects, including early destruction through `Reset()`.

Public copy constructors declared in the C++ source can also be called with
`new* Type(ref *source)`. This creates a separate native allocation. The source
and the new object have independent lifetimes and must be destroyed separately.
Implicit copy constructors and rvalue-reference constructors are not exposed as
`new*` overloads. Host value copying uses the native copy constructor independently
of that allocation API.

Use the matching native destruction API for library-owned objects. A factory
result is not automatically an owning allocation suitable for `delete`.
Keep borrowed pointers within the native object's lifetime. Classes with private
constructors or destructors still require their native factory/release API.

Complete opaque types can be used as host values when Clang confirms nonthrowing
native move construction (or a nonthrowing copy fallback) and nonthrowing
destruction. Their storage uses the native size and alignment, including inside
host structs and arrays. Native fields remain opaque.

Initialize a value with `new Type(arguments)`. Ordinary copies use the native copy
constructor when accessible. Deleted or inaccessible copies are rejected.
`move` constructs the destination through the native operation and destroys the
consumed source. Scope exit destroys the resulting owner. Constructor and copy
failures leave completed owners subject to normal cleanup. `default(Type)` does
not construct a native object and is rejected.

Supported values also permit raw placement construction and explicit destruction
through pointers. The caller supplies aligned storage and manages its lifetime.
Types without the required lifetime operations remain usable through native
pointers and borrowed references. Native objects cannot use managed allocation.

```gflat
#lang cpp
namespace Native {
    class Counter {
        int value;
    public:
        explicit Counter(int initial) : value(initial) {}
        int Read() const { return value; }
    };
}
#lang gflat
int main()
{
    Native::Counter counter = new Native::Counter(42);
    int value = counter.Read();
    return value - 42;
}
```

Native base-pointer conversions are not inferred yet.
Consume a native function returning the appropriate base pointer when
calling through a base interface. This does not change host-defined class ABIs.

The following are also not imported yet:

- Nontrivial native function parameters and results passed by value
- Rvalue-reference results and parameters
- Templates, default arguments, globals, macro values, and namespace aliases
- Overload families that differ only in qualifiers erased by the host type system
- Variadic C++ functions without C linkage

External C-linkage variadic functions are imported directly. Variadic functions
with native internal linkage are not imported. Foreign functions cannot
participate in host constant evaluation.

## Exceptions across native calls

`noexcept` declarations, literal `noexcept(true)` and `noexcept(1)`, and native
`nothrow` attributes import as host `nothrow` contracts. The importer treats other
conditional specifications as potentially throwing. Native allocation can still
throw when its constructor is `noexcept`.

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
scalar types, raw pointers, and `ref` parameters to those types. Reference
parameters use native lvalue-reference mangling, including `const` for readonly
data. Host `long` and `ulong` map to C++ `long long` and
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
