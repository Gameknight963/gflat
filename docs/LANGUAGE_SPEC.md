# gflat language reference

Version 0.3, experimental. This document specifies the supported language contract and distinguishes it from future work.

## Design charter

gflat is a systems language with explicit heap allocation, inline objects, fixed integer widths, and deterministic scope cleanup. Ordinary expressions do not implicitly allocate heap objects. A class is an inline object with a vtable; use a pointer when reference semantics are needed.

Raw pointers, explicit reinterpretation casts, and extern declarations are the low-level boundary. They do not establish ownership, bounds, or a valid lifetime. `readonly` restricts access through that view; raw-pointer casts can remove it, but managed-reference casts cannot. The language does not claim complete memory safety. Foreign declarations are trusted contracts.

Taking an address does not initialize storage. Locals must be initialized before their address is passed to an ordinary pointer parameter. There is currently no `out` parameter contract. Initialize a value explicitly before handing it to a foreign output function.

The supported native target is **x86_64-pc-windows-msvc**. Other targets are rejected by the driver. The target triple, pointer width, and LLVM data layout are recorded in `TargetInfo`. Fixed-width types are language rules; portability to other targets remains future work.

## Lexical and grammar rules

Identifiers contain ASCII letters, digits, and underscores and cannot begin with a digit. Double-underscore names are used by compiler hooks. They must not be treated as a security boundary. Comments are `//` to end of line or non-nesting `/* ... */`; unterminated comments are errors.

Strings use double quotes and escape sequences; raw newlines are rejected. Decimal numeric separators must appear between digits. Interpolation remains reserved syntax and is diagnosed as unsupported.

The following EBNF describes the core expression/type grammar; declaration modifiers, generics, interfaces, and operators are detailed below. Repetition is `{ ... }`, optional syntax is `[ ... ]`.

```ebnf
type          = [ "readonly" ], qualifiedType, { suffix } ;
qualifiedType = name, [ "<", type, { ",", type }, ">" ],
                { "::", name, [ "<", type, { ",", type }, ">" ] } ;
suffix        = "*", [ "?" ] | "^", [ "?" ]
              | "[", [ expression ], "]"
              | "(", [ type, { ",", type } ], ")", ("*" | "^"), [ "?" ] ;
variable      = [ "const" ], type, name, [ "=", expression ], ";" ;
function      = { modifier }, type, name, "(", [ parameters ], ")",
                [ "throws" ], body ;
body          = block | "=>", expression, ";" | statement ;
block         = "{", { statement }, "}" ;
foreach       = "foreach", "(", type, name, "in", expression, ")", body ;
```

`>>` closes two nested type argument lists in a type context and remains a shift in expression contexts. All array lengths are parsed as expressions and then checked as constants; they must be positive and fit supported layout sizes.

Expression precedence, from weakest to strongest: assignment (right associative); `||`; `&&`; bitwise `|`, `^`, `&`; equality; ordering; shifts; addition/subtraction; multiplication/division/remainder; prefix operators/casts; postfix calls, indexing, access, and increment/decrement. `&&` and `||` short-circuit at both compile time and runtime.

`::` accesses namespaces, nested types, enum values, and static members. `.` accesses instance members, including through pointers. The legacy arrow spelling is rejected semantically. Function pointers are written with their return type, such as `int(int)*`.

```gflat
struct Box<T> { T value; }
int main()
{
    Box<Box<int>> nested;
    int[1 + 2] buffer;
    return sizeof(buffer);
}
```

## Numeric types and conversions

| Types | Representation |
| --- | --- |
| bool | Boolean, i1 in LLVM |
| byte / sbyte | Unsigned / signed 8-bit integer |
| char | 8-bit character storage, signed integer arithmetic |
| ushort / short | Unsigned / signed 16-bit integer |
| uint / int | Unsigned / signed 32-bit integer |
| ulong / long | Unsigned / signed 64-bit integer |
| nuint / nint | Unsigned / signed target pointer width, currently 64 bits |
| float / double | IEEE binary32 / binary64 |
| void | No value |

Explicit integer narrowing retains low bits and interprets them using the destination signedness. Constant casts use the same widths as runtime lowering. `uint -> int` and `ulong -> long` require explicit casts. `float` conversions round to binary32 during constant evaluation. Non-finite and out-of-range constant float-to-integer conversions are errors.

Integer arithmetic without overflow flags wraps at its result width. Division by zero, signed division overflow, out-of-range shift counts, and out-of-range runtime float-to-integer conversions are not defined operations; callers must avoid them. Compile-time evaluation diagnoses operations it cannot evaluate. Wider numeric support and more complete runtime checks remain experimental.

```gflat
const int answer()
{
    int value = 5;
    value += 7;
    return value;
}
int main()
{
    const int expected = answer();
    int actual = answer();
    if (actual == expected) return 0;
    return 1;
}
```

Enums are distinct integral types, optionally with an underlying type such as `uint`. Bitwise operations on the same enum preserve its type. Aliases use `alias Name = Type;`.

## Pointers and allocation

`T*` expresses non-null intent; `T*?` permits null. Null and zero constants cannot be explicitly cast to a non-null pointer. Explicit casts to non-null pointer types check their result at runtime and trap on null. This check proves neither bounds nor lifetime. Default construction and foreign contracts must not be assumed to prove complete object validity.

`new Type(...)` constructs an inline value. `new* Type(...)` allocates storage, checks for allocation failure, initializes it, and invokes its constructor. Failed allocation traps before any store or constructor call. This fail-fast operation does not add a checked exception effect. `delete p` destroys and frees an allocated object; it does not make aliases safe or permit repeated deletion.

Allocation hooks are already implemented: `__gflat_alloc(ulong size)` returns a pointer, potentially nullable, and `__gflat_free(void* ptr)` releases it. The default allocation declaration is `extern void*? malloc(ulong size)`. The same failure check applies to custom allocation hooks. Extern declarations with stronger contracts are the programmer's responsibility.

Arrays have inline storage. Array-to-pointer decay does not establish bounds or ownership. Uninitialized stack arrays are buffers; allocation alone does not guarantee initialized elements. Initialize elements before reading them.

```gflat
struct Point { int x; int y; }
int main()
{
    Point* p = new* Point();
    p.x = 3;
    p.y = 4;
    int result = p.x + p.y;
    delete p;
    return result;
}
```

## Initialization, ownership, and cleanup

### Managed pointers

`T^` is a copyable reference to managed storage; `T^?` additionally permits null. `new^ T(...)` supports structs, classes, and primitive scalars. Scalar allocation accepts zero arguments (zero initialization) or one initial value, for example `new^ int(42)`. Allocation returns a non-null reference, zero-initializes object storage, and runs the selected constructor. A null allocator result traps before initialization.

Managed allocation requires a user-provided **global** hook with this ABI:

```gflat
extern void*? __gflat_gc_alloc(ulong size);

class Box
{
    public int value;
    public Box(int initial) { value = initial; }
}

int main()
{
    Box^ box = new^ Box(42);
    Box^ copy = box;
    copy.value = 7;
    return box.value;
}
```

The hook may instead be defined in gflat. It must be non-generic and non-throwing, accept exactly one `ulong` byte count, and return writable `void*?` or `void*` storage with suitable alignment. An extern declaration requires the implementation to be linked by the native toolchain. No default GC or Boehm adapter is bundled. Merely declaring managed references does not require the allocation hook. The hook is independent of `__gflat_alloc` and `__gflat_free`.

The initial runtime contract is a **conservative, nonmoving collector** that scans live stack/register roots and managed allocations, and recognizes interior pointers used during object access. The adapter supplies collector initialization and any thread registration required by its collector. The compiler does not emit root maps, relocation support, pinning, or finalization. References stored only in unscanned raw allocations or retained by foreign code need runtime-specific root registration; they are not automatically kept alive by the language.

Scope exit does not destroy or free a managed pointee. Types with destructors, including inherited destructors and inline fields or array elements requiring destruction, cannot be managed pointees in this version. `delete`, pointer arithmetic, managed/raw casts, integer casts, and taking raw addresses directly into managed storage are rejected. Managed class upcasts and interface views are supported. Nullable references require an explicit checked non-null cast before dereference, field access, or method calls. Managed exceptions and managed function pointers remain unsupported. Managed allocations cannot execute during constant evaluation.

### Array literals

`[expression, ...]` creates a fixed-size array. Elements evaluate from left to right and must have the same resolved type, including nested array dimensions. A trailing comma is allowed. Empty literals and mismatched destination sizes are rejected; use explicit casts when elements need a different numeric type. An unsized local declaration infers its size from the initializer.

```gflat
int main()
{
    int[3] values = [1, 2, 3];
    int[] more = [4, 5, 6,];
    int[2][2] grid = [[1, 2], [3, 4]];
    return values[1] + more[0] + grid[1][1];
}
```

Literals can be indexed, returned, used in `foreach`, and passed to pointer parameters. Constant arrays support constant elements. A pointer initialized directly from a literal refers to temporary stack storage in the enclosing scope; pointers must not outlive that storage. Pointer arguments to calls remain valid for that call.

Destructor-bearing elements must be fresh values rather than copies of existing objects. If evaluating an element throws, previously initialized elements are destroyed in reverse order. Once initialization succeeds, the array owns all elements and uses normal array cleanup. Constant arrays cannot contain destructor-bearing elements.

### Scope cleanup

Local scalar reads require definite assignment. Struct fields can be initialized individually. An assignment on only a skipped logical operand or only inside a potentially empty loop does not initialize a value after that operation. Return values are evaluated before deferred actions.

`defer` executes in reverse registration order at lexical scope exit, including return, break, continue, and supported exception paths. Deferred code binds variables at its declaration site, even if a later scope shadows a name. Deferred reads are checked at cleanup; deferred writes do not take effect at registration. Nested defer is not supported.

```gflat
int main()
{
    int value;
    {
        defer value = 9;
    }
    return value;
}
```

Objects with destructors cannot be copied by value through ordinary initialization, assignment, parameters, or foreach. Return of a fresh value or a whole owned local transfers ownership; returning a destructor-bearing field, dereference, or indexed element is rejected. Partial moves and a general borrow checker are not implemented. Raw-pointer ownership remains manual.

Struct and class destruction runs the destructor body, then inline object fields in reverse declaration order, then the class base destructor. Returning early from a destructor still runs this cleanup. Containers with destructor-bearing fields receive synthesized cleanup. Discarded fresh object values and temporary method or field receivers are destroyed after their use. Temporaries on skipped logical operands are neither constructed nor destroyed; exceptions during evaluation clean up only successfully created temporaries. These rules are not a complete ownership proof.

Fixed-size arrays with destructor-bearing elements clean up from the last element to the first, recursively for nested arrays, on scope exit and exception propagation. This also applies to inline array fields and returned array values. Deleting a pointer to a fixed-size array destroys its elements before freeing the allocation. Such local arrays require an initializer, cannot be copied or reassigned, and cannot be initialized from a differently sized array. `default(T[N])` zero-initializes the elements without calling constructors; zero-initialized class elements remain unconstructed and are skipped during cleanup. Arrays of raw pointers do not destroy their pointees.

Structs provide inline fields, constructors, methods, and optional destructors. Classes add a vtable and single implementation inheritance. Interfaces provide method contracts with fat pointer dispatch. These are gflat layouts, not a C++ ABI. Foreign aggregate compatibility requires dedicated ABI tests; use pointers and primitive C interfaces for interoperation.

## Control flow, exceptions, and constants

Non-void functions must return or throw on every reachable path, or never terminate. Shared control-flow graphs model branches, loops, return, throw, break, and continue. Definite assignment uses these termination facts and tracks assignment states separately.

Throwing functions declare `throws`. Callers must catch or declare propagation. The generated ABI uses a value/status tuple, not native C++ unwinding. `main` cannot declare `throws`. Catch cleanup runs deferred actions before destroying and freeing an owned exception, including exits by return, break, continue, or another throw. Rethrowing the caught pointer transfers its release responsibility. An exception escaping deferred cleanup replaces the pending exception, releasing the previous owned exception; remaining cleanup runs once.

`const` functions can execute supported procedural logic during compilation. The evaluator limits execution to **100,000 steps and 256 calls**. It shares numeric conversion rules with the compiler; unsupported operations produce a constant-evaluation diagnostic. Compile-time execution is not a general macro or reflection facility.

## Compiler use

```text
dotnet run --project gflat -- source.gf -o program.exe
dotnet run --project gflat -- source.gf --emit-ir -o program.ll
dotnet run --project gflat -- source.gf --run
```

Compilation does not execute output unless `--run` is requested. `--clang path` or `GFLAT_CLANG` selects Clang; otherwise PATH and Visual Studio installations are searched. `--target` accepts the supported triple only. Native compilation and execution have timeouts and concurrently captured output.

`GFLAT_OPT_LEVEL` selects Clang optimization (`0`, `1`, `2`, or `3`; default `0`). It applies to native CLI compilation and executable tests. CI runs the suite at both `-O0` and `-O2`. To reproduce the optimized run in PowerShell:

```powershell
$env:GFLAT_OPT_LEVEL = '2'
dotnet test
Remove-Item Env:GFLAT_OPT_LEVEL
```

Exit status: 1 for source errors, 2 for usage/toolchain errors, 3 for internal compiler errors. With `--run`, successful compilation returns the program's exit status. Source/output paths must differ.

## Future work

Additional target ABIs, verified out parameters, full ownership and partial moves, managed finalizers and moving collectors, interpolation, reflection, and C++ interoperability are proposals, not current guarantees. Compiler architecture can progressively replace AST-based lowering with a richer checked representation; the shared control-flow graph is the current foundation.
