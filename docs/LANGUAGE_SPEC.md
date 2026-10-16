# gflat language reference

Version 0.3, experimental. This document specifies the supported language contract and distinguishes it from future work.

## Design charter

gflat combines familiar object-oriented APIs with control over storage and layout.
`T` is a value, not a choice of stack allocation: it can be a local, an inline
field, or an element inside a heap-allocated vector. `T*` borrows or manually
addresses storage; `T^` uses the configured managed allocator. Allocation and
access qualification are separate from the type's methods and interfaces.

Structs retain constructors, methods, properties, interfaces, and destruction while
providing inline layout. Classes add a vtable and implementation inheritance. Heap
storage is requested through allocation expressions or library operations; ordinary
values are not silently boxed. Fixed integer widths and deterministic cleanup of
owning values remain language guarantees. Readonly views qualify the same types;
libraries should not need a duplicate container just to expose readonly elements.

Raw pointers, explicit reinterpretation casts, and extern declarations are the low-level boundary. They do not establish ownership, bounds, or a valid lifetime. `readonly` restricts access through that view; raw-pointer casts can remove it, but managed-reference casts cannot. The language does not claim complete memory safety. Foreign declarations are trusted contracts.

Taking an address does not initialize storage. Locals must be initialized before their address is passed to an ordinary pointer parameter. There is currently no `out` parameter contract. Initialize a value explicitly before handing it to a foreign output function.

The native target is **x86_64-pc-windows-msvc** on Windows and **x86_64-unknown-linux-gnu** on Linux. Linux execution is covered by the Ubuntu x64 CI jobs at O0 and O2. The driver accepts only its host target; cross-compilation and other architectures are not supported. The target triple, pointer width, and LLVM data layout are recorded in `TargetInfo`. Fixed-width types are language rules.

Only a non-generic global `main` is the native entry point. It must return `int`
and cannot be overloaded. Namespace and type members named `main` are ordinary
functions, with ordinary name mangling and exception propagation.

## Lexical and grammar rules

Identifiers contain ASCII letters, digits, and underscores and cannot begin with a digit. Double-underscore names are used by compiler hooks. They must not be treated as a security boundary. Comments are `//` to end of line or non-nesting `/* ... */`; unterminated comments are errors.

Strings use double quotes and escape sequences; raw newlines are rejected. Decimal numeric separators must appear between digits. Prefixed interpolation uses `s$"text {expression}"`; doubled braces `{{` and `}}` represent literal braces. Hole expressions may contain nested strings, comments, and interpolation.

Character literals hold one 8-bit value; characters above U+00FF are rejected.
The supported escapes in strings and characters are `\n`, `\r`, `\t`, `\0`,
`\\`, `\'`, and `\"`. Unsupported escapes are errors. Character arithmetic and
ordering interpret the stored byte as signed, including during constant evaluation.

There is no built-in `string` type or keyword. Plain `"text"` literals have type
`readonly(char*)`: a non-null pointer to static, null-terminated UTF-8 storage.
Returning that pointer is valid; writing through it is rejected. Embedded nulls
remain in the stored bytes, although C-string consumers stop at the first null.
Within a compilation, immutable literals with identical decoded UTF-8 bytes share
storage, including literals with different escape spellings. Pointer equality
compares storage addresses, not C-string contents. Constant evaluation preserves
byte offsets for pointer arithmetic, indexing, and dereferencing, including the
terminator. One-past addresses may be formed but cannot be read. Ordering or
subtracting pointers into different constant strings is not a constant operation;
reinterpreted pointer views are also unsupported during constant evaluation.
A fixed buffer declaration such as `char[6] buffer = "hello"` copies the bytes and
terminator into writable inline storage. Larger buffers are zero-padded; smaller
buffers are rejected. `char[] buffer = "hello"` infers the size including the
terminator. Literal-to-buffer copying also works for fixed buffer fields, returns,
and arguments. It does not implicitly convert arbitrary string pointers to buffers.
The standard library's `s"text"` creates an owning `std::String`. There is no built-in
`c` prefix; it is available for custom literal operators.
The identifier `string` is available for user declarations.

The following EBNF describes the core expression/type grammar; declaration modifiers, generics, interfaces, and operators are detailed below. Repetition is `{ ... }`, optional syntax is `[ ... ]`.

```ebnf
type          = ( qualifiedType | "readonly", "(", type, ")" ), { suffix } ;
qualifiedType = name, [ "<", type, { ",", type }, ">" ],
                { "::", name, [ "<", type, { ",", type }, ">" ] } ;
suffix        = "*", [ "?" ] | "^", [ "?" ]
              | "[", [ expression ], "]"
              | "(", [ type, { ",", type } ], ")", ("*" | "^"), [ "?" ] ;
variable      = [ "const" ], type, name, [ "=", expression ], ";" ;
function      = { modifier }, type, name, "(", [ parameters ], ")",
                [ "nothrow" ], body ;
body          = block | "=>", expression, ";" | statement ;
block         = "{", { statement }, "}" ;
foreach       = "foreach", "(", type, name, "in", expression, ")", body ;
```

`>>` closes two nested type argument lists in a type context and remains a shift in expression contexts. All array lengths are parsed as expressions and then checked as constants; they must be positive and fit supported layout sizes.

Expression precedence, from weakest to strongest: assignment (right associative); `||`; `&&`; bitwise `|`, `^`, `&`; equality; ordering; shifts; addition/subtraction; multiplication/division/remainder; prefix operators/casts; postfix calls, indexing, access, nullability suppression (`!`), and increment/decrement. `&&` and `||` short-circuit at both compile time and runtime.

`::` accesses namespaces, nested types, enum values, and static members. `.` accesses instance members, including through pointers. The legacy arrow spelling is rejected semantically. Function pointers are written with their return type, such as `int(int)*`.

A bare function signature, such as `int(int)` or `void()`, is a type that describes
a function but has no value storage. It can be used as an alias target or generic
type argument; applying `*` gives a function pointer. Named namespace functions
and static methods convert implicitly to matching function pointers. Explicit
`&Function` remains valid. An overloaded function name requires an expected
function pointer signature that selects exactly one overload. This conversion
does not capture an instance receiver.

```csharp
alias Transform = int(int);
int Double(int value) => value * 2;
int Apply(Transform* transform) => transform(21);
int Example() => Apply(Double);
```

`namespace A::B { ... }` is shorthand for nested namespace blocks. `namespace A::B;` puts the rest of the source file in that namespace. A file-scoped namespace must be the first declaration after any imports, cannot be nested, and occurs at most once per file. Imports may also appear immediately after its header. Nested block namespaces within it are relative to that namespace. Imports and the file-scoped namespace do not affect other source files.

```csharp
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
| extralong | Signed 128-bit integer |
| nuint / nint | Unsigned / signed target pointer width, currently 64 bits |
| float / double | IEEE binary32 / binary64 |
| void | No value |

`void` is valid as a function return type and behind an opaque pointer. It cannot
be a local, field, parameter, array element, or the value of `default(void)`.
An empty parameter list is written `()`, not `(void)`.

Explicit integer narrowing retains low bits and interprets them using the destination signedness. Constant casts use the same widths as runtime lowering. `uint -> int` and `ulong -> long` require explicit casts. `float` conversions round to binary32 during constant evaluation. Non-finite and out-of-range constant float-to-integer conversions are errors.

Explicit `u` and `ul` integer suffixes require values within their unsigned 32-bit
and 64-bit ranges. Decimal `l` literals use the signed 64-bit range; its minimum
value can be written with unary minus. Hexadecimal `l` literals retain their
64-bit signed bit-pattern interpretation. A wider destination does not waive
an explicit suffix's bounds. Unsuffixed literals retain contextual widening.

Integer-to-floating conversions round directly to the destination precision,
using nearest with ties to even. Constant evaluation preserves all 128 bits of
`extralong` values before conversions and arithmetic width normalization.

In integer arithmetic, bitwise operations, and comparisons, a representable integer
literal can adopt the other operand's `nint` or `nuint` type. For example,
`length != 0` and `2 * length` do not need casts when `length` is `nuint`.
This applies on either side and preserves the native type's width and signedness.
Negative literals cannot adopt `nuint`; signed variables do not implicitly become
unsigned. Shift counts remain independent of the left operand's type.

Integer arithmetic without overflow flags wraps at its result width. Division by zero, signed division overflow, out-of-range shift counts, and out-of-range runtime float-to-integer conversions are not defined operations; callers must avoid them. Compile-time evaluation diagnoses operations it cannot evaluate. Wider numeric support and more complete runtime checks remain experimental.

Floating arithmetic and comparisons support `float` and `double`; mixed floating
operands widen to `double`. Literal and intermediate `float` values round to
binary32 in constant evaluation as at runtime. Negation preserves signed zero.
Floating division and remainder follow IEEE behavior, including infinities and
NaNs: `NaN != x` is true, while equality and ordered comparisons with NaN are false.
Bitwise and shift operators do not accept floating operands. Depending on the
target, floating remainder may require the toolchain's math runtime.

```csharp
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

Enums are distinct integral types, optionally with an underlying type such as `uint`. Conversions between enums and numbers, or between different enum types, require explicit casts, including assignments, arguments, returns, and comparisons. Bitwise operations on the same enum preserve its type. Aliases use `alias Name = Type;` and may be declared in namespaces or local scopes. Alias declarations inside classes or structs, including static and generic types, are not supported and are diagnosed at the declaration.

Enum storage currently supports integer underlying types up to 64 bits; `extralong` enum storage is not yet supported. Each member value, including an automatically incremented value, must fit the selected underlying type. Different member names may share the same numeric value.

## Optional parameters

Functions, methods, extern declarations, and constructors can declare trailing
optional parameters: `int Read(int count = 42)`. Required parameters must come
first. Defaults must be compile-time constants and use the declaring namespace,
type, and imports; they cannot depend on caller locals or parameter values.
The compiler supplies omitted arguments after selecting the declaration, without
changing its ABI. Function pointers require every argument because their types
do not carry defaults. Default values appear in editor signatures and hovers.

Calls evaluate the receiver or function pointer before evaluating arguments from
left to right. Later argument effects do not change the receiver or callable
already captured for that call.

Calls through interfaces and base pointers use the defaults of the statically
selected declaration, even when another implementation runs. An explicit
`Child(int value) : base(value)` initializer passes constructor arguments to the
base. `base()` and implicit base initialization can use optional arguments.

Defaults do not distinguish overloads. Matching supplied argument types take
priority; when those are tied, an overload needing no defaults wins. Other ties
are ambiguous. Defaults are not supported on ref parameters, operators,
indexers, copy constructors, or lambdas. Optional values cannot require
nontrivial copying or destruction.

## Extension methods

A namespace function can mark its first parameter with contextual `this`:

```csharp
namespace Numbers {
    int Plus(this int value, int extra = 2) => value + extra;
}

int main() {
    int value = 40;
    return Numbers::Plus(value);
}
```

With `using Numbers;`, the same function can be called as `value.Plus()`.
Current and enclosing namespaces and the source file's using directives determine
which extensions are visible. Extensions remain ordinary functions with ordinary
signatures: no object metadata or different calling convention is introduced.
The receiver evaluates once, before the remaining arguments.

`this T` passes a value using the usual copy or transfer rules. `this ref T`
borrows mutable existing storage, and `this ref readonly(T)` borrows a readonly
view. Ref receivers cannot be temporaries, value properties, or storage inside
managed objects. A pointer receiver can instead be declared explicitly as
`this T*` or `this T^`; nullable receiver parameters are allowed. Ref arguments
retain the slot's declared nullability, even after a null guard narrows a read.

Callable instance members take priority and retain their normal diagnostics.
Missing or non-callable members allow extension lookup. Exact receiver candidates
are considered first; only when none accepts the receiver does a non-null raw
pointer try one pointee dereference, including borrowing its existing storage.
This does not chase multiple pointer levels or borrow into managed storage.
Overloads within the chosen receiver tier use normal argument ranking. Mixing
value and ref receiver shapes within that tier is ambiguous. Qualify the ordinary
function call when an explicit choice is needed.

An extension receiver must be first and cannot be const or optional. Only
namespace functions support the marker. Generic extensions use existing generic
constraints and inference; overloaded generic families remain unsupported.
Extensions do not add properties, operators, bound method groups, or static
type calls such as `int.Parse()`. Replacements must preserve the receiver marker.

## Weak defaults and explicit replacements

Ordinary free functions, static methods, and concrete instance methods support overloads by parameter type and count. Exact argument types are preferred over implicit conversions; a candidate must be no worse for every argument and better for at least one. Equally applicable candidates are ambiguous, independent of declaration order. Constructor calls use the same ranking. Return types, readonly qualifications, and declaration modifiers cannot distinguish overloads.

Namespace functions cannot be `abstract`; use an `expect` declaration for an implementation supplied through `replace`.

Overloaded generic function families and overloaded virtual/interface declarations are not yet supported and are diagnosed. An ordinary overloaded implementation can satisfy an interface's matching signature. An overloaded function group can be used with or without `&` when the expected function-pointer signature selects one overload unambiguously; a wrapper is unnecessary in that context. Function addresses cannot discard const-parameter requirements or a variadic calling convention; those forms are rejected until callable types can express them. Lambda parameters cannot be `const` for the same reason.

`weak` marks a function's default implementation. `replace` explicitly selects a different body for that function in the same namespace or containing type. Declaration order does not matter; reopened namespace blocks share the same scope.

```csharp
namespace Settings {
    public weak int Capacity() => 16;
    public replace int Capacity() => 64;
}
class Buffer {
    public static weak int Capacity() => 16;
    public static replace int Capacity() => 64;
}
```

Selection happens in the compiler before body checking, const evaluation, hierarchy resolution, and code generation. With no replacement, the default is used. With a replacement, the discarded body is parsed but is not type-checked or emitted. All calls and function addresses bind to the selected definition. LLVM receives one ordinary definition, with no weak linkage or linker-level override behavior.

A replacement must match parameter types, return type, generic arity and constraints, accessibility, `static`, `virtual`, `override`, `readonly`, `const`, const parameters, and `nothrow`. Parameter names and generic parameter names may differ. Duplicate defaults, multiple replacements, unmatched replacements, and ordinary definitions attempting to replace a default are errors. Selection happens independently for each overload: replacing one signature leaves other signatures intact, and every expected signature needs an implementation. A function family cannot combine language definitions with an extern declaration.

Free functions and concrete class/struct methods support these modifiers. Static methods use `Type::Method(...)` and have no implicit `this`; instance methods use `value.Method(...)`. Replacement stays within the same containing type: inherited dispatch still uses `override`. Constructors, destructors, operators, abstract methods, and interface declarations cannot be weak or replaced. Partial classes are not implemented.

## Pointers and allocation

`T*` expresses non-null intent; `T*?` permits null. Null and zero constants cannot be explicitly cast to a non-null pointer. Explicit casts to non-null pointer types check their result at runtime and trap on null. This check proves neither bounds nor lifetime. Default construction and foreign contracts must not be assumed to prove complete object validity.

`new Type(...)` constructs an inline value. `new* Type(...)` allocates storage, checks for allocation failure, initializes it, and invokes its constructor. If allocation returns null, it throws `std::BadAllocException` before any store or constructor call. Its exception storage uses the normal allocator first, then the exception reserve. `delete p` destroys and frees an allocated object; it does not make aliases safe or permit repeated deletion.

Primitive scalar types use the same construction forms. Zero arguments produce
the default value; one argument must be implicitly assignable to the scalar type.
For example, `new int(42)` produces an `int`, `new* int(42)` produces an allocated
`int*`, and `new* int(42) at address` initializes existing integer storage.

Constructors must initialize fields that cannot have a default value on every normally completing path. Field initializers and automatic property assignments count. Reading such a field, exposing `this`, or calling an instance method requires the relevant initialization first. The analysis follows branches, loops, returns, and deferred assignments; it does not infer initialization performed inside arbitrary helper methods. Required array fields must be assigned a complete array value. An implicit parameterless constructor is available only when each field has an initializer or a valid default, and the base can be constructed without arguments.

Instance constructors cannot be `readonly`, `virtual`, `override`, or `abstract`; copy constructors also cannot be `static`. Destructors cannot be `static`, `const`, `readonly`, `override`, or `abstract`.

`default(T)` does not execute field initializers or user constructors. It rejects non-null pointers, types with user-defined constructors, and aggregates containing fields that cannot be default-initialized. Nullable pointers become null. Class values receive valid virtual tables, including classes nested in arrays and other aggregates; their destructors run normally. `new T()` invokes the parameterless constructor when one is declared.

The public allocation hooks are `Memory::Allocate(nuint size) -> void*?` and `Memory::Free(void* ptr) -> void`. `std/libc/Memory/Memory.gf` provides automatically included public weak definitions calling libc `malloc` and `free`. `new*`, `delete`, and exception cleanup use these same functions. Applications may call them directly. Custom allocators must replace **both** functions with matching public signatures and non-throwing callback behavior:

```csharp
extern void*? malloc(nuint size);
extern void free(void* ptr);
namespace Memory {
    public replace void*? Allocate(nuint size) { return malloc(size); }
    public replace void Free(void* ptr) { free(ptr); }
}
```

The non-throwing requirement is a behavioral callback contract, not a `nothrow`
modifier on these declarations. `Allocate` must report allocation failure by
returning null; neither hook may let an exception escape. The exception runtime
uses these same hooks for payload allocation and release. Throwing from an
allocator can recursively request exception storage through that allocator, and
throwing during release can fail while another exception is being cleaned up.
Ordinary direct calls participate in unwinding, but this does not make throwing
implementations safe runtime callbacks. Signature checking does not prove this
behavioral requirement.

`Allocate` must return suitably aligned storage or null. Compiler-generated `new*` throws `std::BadAllocException` on null before initialization; direct calls expose the nullable result. Default-runtime `new^` can collect and retry once before throwing that exception. The default extern declaration is `extern void*? malloc(nuint size)`. Allocation sizes and custom literal byte lengths use pointer-sized `nuint`, not fixed-width `ulong`; hook signatures must match that type even on a target where both have the same width. Extern declarations with stronger contracts are the programmer's responsibility. Libc remains a toolchain dependency for now; replacing these functions does not select a freestanding build. The old `__gflat_alloc`, `__gflat_free`, and `#no_default_allocator` interfaces are rejected with migration diagnostics.

Arrays have inline storage. Array-to-pointer decay does not establish bounds or ownership. Uninitialized stack arrays are buffers; allocation alone does not guarantee initialized elements. Initialize elements before reading them.

Fixed-size array parameters receive independent copies, including nested arrays. Use an explicit element pointer or pointer to a whole fixed-size array to share storage. Array values require fixed sizes in every dimension, including parameters, return types, and fields. Spell buffer parameters of unknown length as pointers. Array value conversions preserve dimensions and element representation; cast individual elements explicitly when changing numeric types. Owning values transfer from fresh values or explicit moves, or copy using a copy constructor.

Nullable raw pointers and function pointers require a proven non-null local read, checked non-null cast, or unchecked postfix `!` before operations requiring a non-null value. Raw pointers support dereference, indexing, member access, and pointer arithmetic as appropriate for their element type. Function pointers support calls, not arithmetic: functions do not form an array with a fixed element size. A raw pointer to function-pointer storage supports ordinary array traversal. Null guards narrow local and parameter reads while preserving their declared storage types. The cast traps if the value is null. Function pointer conversions preserve the exact return and parameter types, including readonly qualifications; implicit numeric return conversions do not change a function's calling convention.

```csharp
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

## Readonly types and methods

`readonly(T)` is a transitive readonly view of `T`. Ordinary types remain mutable. It restricts writes through that view, including through fields, pointers, managed pointers, and array elements. It does not freeze an object globally: an existing mutable alias can still change it.

| Declaration | Meaning |
| --- | --- |
| `readonly(int) n` | A value that cannot be reassigned after initialization |
| `readonly(int)* p` | A reassignable pointer through which the integer cannot be changed |
| `readonly(int*) p` | Neither the pointer nor its pointee can be changed through this view |
| `readonly(Buffer)* p` | Only readonly methods can be called; reads of fields retain transitive readonly qualification |

Bare `readonly` on a method qualifies its receiver independently of its return type. For example, `public readonly readonly(char)* Data()` is a readonly method returning a pointer to readonly characters. `public readonly(char)* Data()` has a mutable receiver and the same return type. A readonly method may return a mutable pointer obtained independently of its receiver, such as a pointer parameter.

Fields use the same type syntax. A `readonly(T)` field can be initialized by its declaration or assigned on `this` in its declaring constructor. Other instances and nested storage do not gain a constructor exception. Normal destructor cleanup still runs for readonly values.

Fields cannot declare generic parameters or be marked `virtual`, `override`, or `abstract`.

Copying a readonly scalar into a mutable scalar is allowed. Copying a pointer retains its pointee qualification. Copying an aggregate into a mutable aggregate is rejected if that would expose mutable aliases through pointer fields. Arrays of scalar values can be copied by value. Nested pointer conversions must preserve readonly at every writable level: `int**` can become `readonly(int*)*`, but cannot become `readonly(int)**`.

Aliases and generic arguments preserve qualification. Explicit raw-pointer casts may remove readonly; managed-pointer casts cannot. `const` remains the compile-time constant mechanism.

Declaration modifiers must apply to the declaration itself: types, enums, and aliases cannot be declared `const` or bare `readonly`. Use an explicitly qualified alias target such as `alias View = readonly(int)*;`. Only classes and interfaces accept `abstract`; aliases cannot be `static`, and nested type, enum, or alias declarations cannot be `virtual` or `override`.

This replaces the old prefix type syntax: write `readonly(char)*` instead of `readonly char*`. For an old readonly pointer field, use `readonly(char*)` to keep both the field and pointee readonly. Bare `readonly` is now exclusively a receiver modifier on methods and property accessors.

## Initialization, ownership, and cleanup

### Managed pointers

`managed<T, C>` is a copyable managed reference whose type identity includes the
pointee `T` and collector provider `C`. `T^` abbreviates `managed<T, GC>`, where
`GC` is a global type alias selected once for the whole compilation. File order
and `using` directives do not select a different collector. Type-only queries can
describe the default-reference shape without a provider; creating and tracking
live references requires a concrete binding. `T^?` additionally permits null.

The provider implements `std::IGarbageCollector`, a static interface declared in
`std/core/IGarbageCollector.gf`. Its operations are `Allocate(nuint bytes, nuint
alignment)` returning non-null `void*`, `ReferenceAdded(void* slot) nothrow`,
`ReferenceRemoved(void* slot) nothrow`, and `Collect()` returning `nuint`.
The first three operations are the compiler integration contract. `Collect` is
an ordinary library operation: the compiler never inserts collection requests.
Provider calls are direct; references do not carry a collector tag or dispatch
pointer. Existing interface references retain their object-and-interface-table
representation.

`new^ T(...)` creates storage using the global provider; `new^ T(...) with C`
selects an explicit provider and returns `managed<T, C>`. Structs, classes, and
primitive scalars are supported. Scalar creation accepts zero arguments (zero
initialization) or one initial value. Construction runs with a live temporary
managed reference; failure retires that reference through normal exception
cleanup. References from different collectors cannot be implicitly converted or
stored in each other's managed object graphs.

`std/libc/GC.gf`, included by projects referencing std, declares the default
`public weak alias GC : std::IGarbageCollector = std::TracingCollector;`.
The implementation lives in `std/libc/TracingCollector.gf`; direct source builds
using this default alias must include that file too.
Applications select another provider with `replace alias GC = MyCollector;`.
The constraint validates the selected implementation and limits member access
through `GC` to the common contract. Provider-specific operations, such as
`std::TracingCollector::ManagedCount`, use the concrete provider's name.

The default provider allocates through `Memory::Allocate`, records ownership,
and zeroes storage before construction. It collects and retries once after an
allocation failure, then throws `std::BadAllocException`. A returned native
address that cannot satisfy the requested alignment is freed and rejected.
Zero-sized managed objects receive one byte of storage. Allocation and collection
policy belong to the library. There are no legacy `__gflat_gc_*` allocation hooks.
Collector bookkeeping also uses `Memory::Allocate` and `Memory::Free`. Failure to
allocate its process state or root-slot records terminates: the non-throwing
reference-notification contract cannot report those failures as exceptions.

`std::TracingCollector::Adopt<T>(T* storage)` is an ordinary generic library
method returning `managed<T, std::TracingCollector>`. It transfers an already
constructed, destructor-free allocation to that collector. This is an unsafe
operation: the caller must own a complete allocation from the matching `Memory`
allocator, not stack storage, an interior address, or foreign-allocator storage.
There is no compiler-maintained registry of native allocations. The library
rejects duplicates and overlap with allocations it already owns; it cannot prove
that an arbitrary raw pointer satisfies the precondition. Failed adoption retains
existing ownership. After successful adoption, raw aliases must not destroy or
free the object. Adoption does not collect before establishing the returned root.

The bundled provider is single-threaded and non-moving. `GC::Collect()` explicitly
collects unreachable allocations and returns the number reclaimed. The default
allocation method also collects when bytes allocated/adopted since the previous
collection reach `std::TracingCollector::CollectionThreshold()`. This ordinary
weak static method defaults to 65536 bytes; a qualified replacement can return
another budget, or zero to disable automatic collection. Merely assigning or
copying a reference does not request collection. There is no compiler polling,
collection at unrelated calls or loop iterations, or finalization.

The compiler reports initialized managed-reference slots through the selected
provider's `ReferenceAdded` after stores and `ReferenceRemoved` before overwrite
or lifetime end. The address identifies reference storage, not its target object.
These callbacks must not throw, create managed values, or initiate collection.
Normal and exceptional cleanup retire temporary/local slots. Managed-bearing
results transfer into caller-owned result storage before callee slots retire.

The tracing provider treats native/local slots as roots, including elements in
native `Vector<T^>` buffers. Slots inside managed allocations are edges followed
only when their owner is reachable. Ownership is looked up during collection;
adopting an object changes its fields from roots into edges. Ordinary payload
bytes are not roots. Native containers must end element lifetimes before freeing
their storage; `Memory::Allocate` and `Memory::Free` have no hidden GC callbacks.
Raw manual freeing of collector-owned storage is invalid and is not guarded by
compiler interception. Collection removes registrations for reclaimed objects'
fields before freeing their storage.

All default tracing bookkeeping and collection algorithms are written in gflat.
Collector state uses ordinary static fields. Private inline LLVM provides thread
ownership validation and an unaligned pointer load. There is no native collector runtime linked by
symbol-name inspection. Other providers are ordinary implementations of the same
static contract; moving collectors, pinning, and thread rendezvous still require
additional compiler support.

Scope exit does not destroy or free a managed pointee. Types with destructors,
including inherited destructors and inline fields requiring destruction, cannot
be managed pointees. Managed-reference arithmetic and `delete` on managed views
are rejected. Explicit raw-pointer/managed-reference casts are unsafe adapter
operations; they do not register ownership or validate the allocation. Casting
away managed readonly is rejected. Method receivers can expose borrowed interior
pointers under current non-moving rules; these borrows are not lifetime checked.
Managed class upcasts and interface views preserve the collector identity.
`T^*` denotes native storage containing managed references; its storage lifetime
remains the caller's responsibility.

Foreign declarations cannot take or return managed references or aggregates
containing them. A foreign bridge can return a raw pointer which an explicitly
unsafe library adapter converts to its collector's managed reference type.
Nullable references require a proven non-null read, a checked non-null cast, or
unchecked postfix `!` before use. Exceptions are thrown by value. Managed
function pointers and compile-time managed allocation remain unsupported.

### Array literals

`[expression, ...]` creates a fixed-size array. Elements evaluate from left to right and must have the same resolved type, including nested array dimensions. A trailing comma is allowed. Empty literals and mismatched destination sizes are rejected; use explicit casts when elements need a different numeric type. A local or field declaration can infer its outer array bound from an array literal or a string literal. Local declarations can also infer that bound from another fixed-size array value. Nested bounds must be explicit. Incomplete array types can still be described by `typeof` or named behind opaque pointers; reading, constructing, or measuring such a value, or using its size for pointer arithmetic, requires a complete shape.

```csharp
int main()
{
    int[3] values = [1, 2, 3];
    int[] more = [4, 5, 6,];
    int[2][2] grid = [[1, 2], [3, 4]];
    return values[1] + more[0] + grid[1][1];
}
```

Literals can be indexed, returned, used in `foreach`, and passed to pointer parameters. Constant arrays support constant elements. A pointer initialized directly from a literal refers to temporary stack storage in the enclosing scope; pointers must not outlive that storage. Pointer arguments to calls remain valid for that call.

Destructor-bearing elements must be fresh values, explicit moves, or values supporting copying. If evaluating an element throws, previously initialized elements are destroyed in reverse order. Once initialization succeeds, the array owns all elements and uses normal array cleanup. Constant arrays cannot contain destructor-bearing elements.

### Scope cleanup

Local scalar reads require definite assignment. Struct fields can be initialized individually. An assignment on only a skipped logical operand or only inside a potentially empty loop does not initialize a value after that operation. Return values are evaluated before deferred actions.

Assignments evaluate the destination receiver and indices once, before the right-hand side. A simple `=` computes the destination address without reading its stored value. Compound assignments capture both the destination address and its old value before evaluating the right operand. This order also applies to pointer dereferences and ref-returning properties or indexers. If evaluating the destination throws, the right-hand side is not evaluated. If the right-hand side throws, the assignment does not overwrite or destroy the destination's old value; side effects performed while evaluating either operand still occur.

`defer` executes in reverse registration order at lexical scope exit, including return, break, continue, and supported exception paths. Deferred code binds variables at its declaration site, even if a later scope shadows a name. Deferred reads are checked at cleanup; deferred writes do not take effect at registration. Nested defer is not supported.

Deferred code cannot return from its enclosing function or use `break` or
`continue` to leave the deferred body. Loops declared inside that body may use
their own `break` and `continue` statements normally.

```csharp
int main()
{
    int value;
    {
        defer value = 9;
    }
    return value;
}
```

Owning values (types requiring destruction) can be initialized, passed by value,
returned, and replaced. Fresh values transfer directly. Owned parameters are destroyed
by the callee unless transferred onward. If a later argument fails, earlier arguments
are cleaned up by the caller. Replacement builds the new value before destroying the
old one; a failed right-hand side leaves the old value intact.

An ordinary value copy duplicates the value using its copy constructor when one exists:

```csharp
struct Number {
    public int value;
    public Number(int n) { value = n; }
    public copy Number(readonly(Number)* source) { value = source.value; }
}
int main() {
    Number a = new Number(3);
    Number b = a;
    b.value = 4;
    return a.value;
}
```

A copy constructor takes exactly one non-null readonly pointer to its own type.
It may throw like other constructors. A type with a user-written destructor is noncopyable unless
it supplies a copy constructor. Aggregates without a user-written destructor copy
their fields and base value; all of those must be copyable. Fixed buffers copy each
element. If a copy fails, completed subobjects are destroyed. Nontrivial copying is
not supported during constant evaluation. `foreach` copies elements and requires
a copyable element type.

An explicit value cast follows the same copy contract: casting an existing value requires its accessible copy implementation, while a fresh or moved operand transfers without another copy. An owning cast result is destroyed when discarded or after temporary member use. `default(T)` and cast results support field, property, method, and fixed-buffer element access with the normal temporary lifetime.

For a custom collection, `foreach` calls `GetEnumerator()` using ordinary method
resolution, including visible extension methods. The returned iterator must provide
a `bool MoveNext()` method and a readable `Current` member. `Current` may be a
parameterless method, property, reference-returning property, or field; a method
takes precedence when present. Access restrictions, readonly receivers, and value
copy rules apply as they do to ordinary calls and member reads. A reference-returning
`Current` is copied into the loop variable; this does not introduce reference iteration.

The collection expression is evaluated once. A temporary collection is kept alive
until iteration ends, and the iterator is destroyed before that temporary collection.
Normal completion, `break`, and exceptions use the usual scope cleanup rules.

`move local` transfers a whole mutable local or parameter, including its destruction
responsibility. The source becomes uninitialized: it cannot be read or moved again
until assigned a new value, and its previous value is not destroyed there. This rule
also applies to primitive values. No move constructor runs, and no empty replacement
is created. Owning locals may be declared without an initializer; initialize them as
whole values before use. Owning fields must also be initialized as whole values before
writing nested fields. Returning a whole owning local implicitly transfers it.
Returning borrowed fields or indexed elements requires copying.

```csharp std
using std;
int main() {
    String a = s"hello";
    String b = move a;
    a = s"new value";
    return 0;
}
```

General field and indexer moves are not supported. Raw storage has an explicit manual
path: `move *source` transfers the value stored at a raw pointer, and `move *source at destination` constructs it at another address. `move local at destination` consumes
a local directly into that storage. These return a value or destination `T*`, respectively.
The destination must be uninitialized, non-null, suitably aligned storage for exactly
that type. Null and alignment are checked; allocation size, liveness, and overlap are
not. Source and destination must not overlap. After raw relocation, do not destroy the
old value; release its old allocation separately if appropriate. The compiler does not
track initialization through raw pointers.

By-value calls, returns, and relocation do not promise stable object addresses. Types
storing pointers into themselves need a different representation or stable indirection.
There are no custom move constructors or pinning guarantees in this version. Raw-pointer
validity remains the caller's responsibility; there is no borrow checker or alias
invalidation analysis.

Extern declarations must use explicit pointers for owning types. Owning objects cannot
be sliced to a base value. Operator overloads currently require trivial value operands
and results. Lambdas borrow nontrivial parameters through pointers and cannot return
nontrivial values; use a named function for those signatures. Indexer arguments must
also be trivial values (their element type need not be).

Struct and class destruction runs the destructor body, then inline object fields in reverse declaration order, then the class base destructor. Returning early from a destructor still runs this cleanup. Containers with destructor-bearing fields receive synthesized cleanup. Discarded fresh object values and temporary method or field receivers are destroyed after their use. Temporaries on skipped logical operands are neither constructed nor destroyed; exceptions during evaluation clean up only successfully created temporaries. These rules are not a complete ownership proof.

Fixed-size arrays with destructor-bearing elements clean up from the last element to the first, recursively for nested arrays, on scope exit and exception propagation. This also applies to inline array fields and returned array values. Deleting a pointer to a fixed-size array destroys its elements before freeing the allocation. Such local arrays require an initializer and copy only when their elements support copying; initialization preserves the array size. `default(T[N])` requires default-initializable elements, initializes class metadata, and does not call user constructors. Arrays of raw pointers do not destroy their pointees.

Structs provide inline fields, constructors, methods, properties, and optional destructors. Classes add a vtable and single implementation inheritance. Interfaces provide method and property contracts with fat pointer dispatch. These are gflat layouts, not a C++ ABI. Foreign aggregate compatibility requires dedicated ABI tests; use pointers and primitive C interfaces for interoperation.

## Properties

Static classes are closed declaration scopes: their methods and properties must be static, and they cannot be constructed, inherited from, or used as instance types (including pointers). Nested types remain ordinary types. Static classes may be generic, and a closed specialization can be named with an alias. They emit no instance layout, vtable, instance constructor, or destructor. Static fields and a parameterless static constructor are supported.

```csharp
static class Values<T> {
    public static T Identity(T value) => value;
    public struct Box { public T value; }
}
alias Integers = Values<int>;
int Example() => Integers::Identity(42);
```

Declare a provider contract with `static interface`: all methods and property accessors are implicitly static, including in an empty contract. An ordinary interface with explicitly static members also remains supported. Static classes, ordinary classes, and structs can implement the contract with public static methods or static properties. Generic code calls these members with `P::Member`; specialization selects the implementation directly, without an instance or dispatch table. Such interfaces cannot be used as pointer or managed-reference types. Mixing static and instance members in one interface, generic interface methods, and overloaded interface methods are not yet supported. Constraints resolve in the declaring library's scope, not the caller's.

```csharp
interface ITransform { static int Apply(int value); }
static class Increment : ITransform {
    public static int Apply(int value) => value + 1;
}
static class Pipeline<P : ITransform> {
    public static int Run(int value) => P::Apply(P::Apply(value));
}
alias Selected = Pipeline<Increment>;
int Example() => Selected::Run(40);
```

Classes and structs support explicit accessors, expression-bodied getters, and auto-properties:

```csharp
class Player
{
    int score;
    public int Score
    {
        readonly get => score;
        private set { score = value; }
    }
    public int DoubleScore => score * 2;
    public int Id { get; }
    public int Level { get; set; } = 1;

    public Player(int id) { Id = id; }
    public void Award(int points) { Score += points; }
}
```

`get` and `set` are contextual identifiers. The setter receives an implicit parameter named `value` with the property's type and returns `void`. Accessors accept blocks or `=> expression;`; a property-level `=> expression;` is shorthand for a getter. An explicit property may have only a getter or only a setter. An auto-property must have a getter, and all its accessors must use `;`. Auto-property storage is private and follows normal field initialization in declaration order: eligible fields default-initialize, while required fields need an initializer or constructor assignment. A get-only auto-property can also be assigned through `this` in its declaring instance constructor.

Properties use ordinary member accessibility. On a property with both accessors, one accessor may declare more restrictive access, such as `private set` or `protected set`. Access is checked separately for reads and writes. `readonly get` allows calls through a readonly receiver while permitting a mutable setter. Non-virtual instance auto-getters are implicitly readonly when the stored value can be returned through a readonly receiver. `readonly(T)` qualifies the property's result type; bare `readonly` qualifies the receiver, as on methods.

Scalar, readonly-pointer, and readonly managed-reference results remain available through readonly receivers. Auto-getters returning writable raw pointers, writable managed references, or aggregates containing these require a mutable receiver, preserving the full result type. An explicitly `readonly get` must satisfy the ordinary readonly return rules; it cannot expose writable storage through its result.

Reads call the getter. Assignments call the setter and evaluate to the assigned value, even if the setter changes its `value` parameter. Compound assignments and `++`/`--` evaluate the receiver once, then read, update, and write. Postfix updates return the old value; prefix updates return the new value. The receiver is evaluated before the right-hand side. Normal operator resolution, definite-assignment checks, and temporary cleanup apply. An ordinary by-value property is not addressable storage: taking its address or assigning to a field of an inline property result is rejected. A pointer returned by a property still permits access to its pointee under ordinary pointer rules.

Classes support `virtual`, `override`, and `abstract` properties through their accessors. Interface property contracts use accessor signatures, for example `interface ICount { int Count { get; set; } }`; implementing accessors must be public. A declaration in a derived class hides the whole inherited property, rather than borrowing missing accessors from it. Explicit static properties use `Type::Property`. Accessors follow ordinary exception rules and may declare `get nothrow` or `set nothrow`.

Current restrictions: `init`, throwing interface accessors, and compile-time property evaluation are not implemented. Properties cannot have type `void`, return arrays, or be `const`, `weak`, `replace`, or individually generic. Pointer properties can expose owned storage without copying it. Properties in generic containing types are supported. Property annotations are retained on the property declaration; they are not copied onto synthetic accessors or backing fields.

### Namespace properties

Properties may also be declared at global or namespace scope, with auto-accessors,
explicit accessor bodies, or an expression-bodied getter:

```csharp
namespace Settings;
private extern int putchar(int character);
public int Limit => 256;
public int Output { set { putchar(value); } }
```

Read or assign them with `Settings::Limit` and `Settings::Output`. Unqualified
access follows the enclosing namespace and `using` directives; local variables
take precedence. Global properties can be qualified with `global::`.
Getters and setters follow normal call and exception rules, including compound
assignment and increment. A private property or accessor is accessible only
within its declaring source file.

Namespace properties have no receiver and do not use `static`, receiver
`readonly`, `virtual`, `override`, or `abstract`. Auto-properties may have
initializers; a get-only namespace auto-property cannot subsequently be assigned.
Namespace indexers and `ref` returns are not supported.

### Global and static storage

Global/namespace fields and static fields in classes or structs have persistent,
addressable storage. Static fields are excluded from instance layout, copying,
construction, and destruction. Access them through `Type::Field`, or unqualified
inside the containing type. Global fields follow enclosing namespace lookup;
private globals are private to their source file. A closed generic type has its
own static fields, for example `Cache<int>::Count` and `Cache<char>::Count`.

Storage is default-initialized before dynamic initialization. A type without a
valid default value, including a non-null pointer, requires an explicit initializer.
Reading a non-defaultable value before its dynamic initializer completes traps,
including an indirect read through another function. Defaultable values may be
read in their initial zero/default state during startup.
Constant initializers are emitted directly into mutable static storage. Other
initializers run before `main`, in ordinal source-path order and declaration order
within each source file. They may call ordinary functions and construct values.

A parameterless `static TypeName() { ... }` runs once, after that type's static
field initializers. It has no receiver or base-constructor initializer. Static
auto-properties use static backing fields; get-only static auto-properties and
readonly static scalar fields can be assigned by their declaring static constructor.
A non-defaultable field still requires an explicit initializer; assigning it only
inside a static constructor is not currently sufficient.

Successfully initialized owning globals are destroyed in reverse initialization
order on normal process shutdown, including `exit`. Managed static storage stays
registered as roots until shutdown. An initializer failure destroys completed
owning globals before invoking the unhandled-exception boundary; the failing value
is never treated as fully constructed. Hosted shutdown registration uses the C
runtime's `atexit`, only when static cleanup is needed. `NoStdLib` excludes bundled
library inputs; it does not select a freestanding executable or remove the host CRT.

Trivial static state with constant/default initializers does not require startup
functions or shutdown registration. Explicit calls that terminate the process
without normal shutdown, such as `abort`, do not run static destructors.

### Reference properties

`public ref T this[nuint index] { get => data![index]; }` exposes existing storage.
Named properties can use the same `ref` modifier. There is one getter body, no
setter: assignment writes to the returned storage. The compiler checks a mutable
and a readonly form of that body, selecting from the receiver's qualification.
A readonly receiver yields a readonly element, recursively under normal readonly
rules. The body must therefore work without mutating its receiver.

Reference properties support address-taking and in-place method calls, including
on owning values. They can implement interface contracts and virtual class properties.
Obvious local/temporary returns are rejected, including fields and inline array
elements of those values. Storage obtained through an explicit pointer or another
reference getter remains a borrow whose lifetime the caller must respect. Keep
the returned storage alive. Explicit by-value getters may return fresh owning
values. An owning setter takes ownership like an ordinary function;
its assignment expression has type `void` to prevent duplicating that ownership.
Automatic owning getters remain invalid because they would copy their backing field.

### Indexers

Instance indexers use property syntax with `this` and one or more typed parameters:

```csharp
struct IntBuffer {
    int* data;
    public IntBuffer(int* storage) { data = storage; }
    public int this[nuint index] {
        readonly get => data[index];
        set { data[index] = value; }
    }
}
```

`buffer[i]` calls the getter; `buffer[i] = value` calls the setter. `this[int row, int column]` is accessed as `grid[row, column]`. Getter-only expression bodies (`public int this[int i] => ...;`), setter-only bodies, and restricted setters follow property rules. The receiver is evaluated first, then indices from left to right. Assignments evaluate the value afterward; compound updates call the getter before evaluating the right operand, then the setter. Receiver and indices are evaluated once, including for `++` and `--`. Accessor calls use normal exception and temporary cleanup rules. Indexers do not add automatic bounds checks; the implementation supplies them.

Indexers work in generic types, classes, structs, and interfaces, including virtual and abstract accessors. Readonly getters follow ordinary readonly receiver and return-type rules. Managed pointers and interface views dispatch the pointee's indexer. Raw `T*` indexing retains its built-in array-of-pointees meaning; use `(*p)[i]` to call an indexer on a raw pointee. Interface pointers have no inline element layout, so `p[i]` on an `I*` dispatches the interface indexer.

Ordinary indexer results are values, not storage references. Modifying a field of a returned aggregate or taking its address is rejected; an explicitly returned pointer can expose underlying storage. Current restrictions: one indexer declaration per type, no static or automatic indexers, no indexer initializers or `const` index parameters, and no parameter named `value`. The existing property restrictions on result types, constant evaluation, and throwing interface accessors also apply.


## Reference parameters

`ref T` parameters borrow existing storage. Passing an argument requires an explicit `ref`:

```csharp
void Increment(ref int value) { value += 1; }
int Read(ref readonly(int) value) => value;

int Example() {
    int n = 41;
    Increment(ref n);
    return Read(ref n);
}
```

The parameter is an alias: assignment writes to the caller's value. No object is copied on entry or destroyed on exit. The caller must supply initialized, addressable storage of the exact type; numeric conversions, temporaries, ordinary value properties, and raw references into managed storage are rejected. A mutable argument can bind to `ref readonly(T)`. Borrowed parameters cannot be consumed with `move`; use explicit raw-pointer operations for manual lifetime management. References do not extend an object's lifetime or provide exhaustive alias tracking.

Fields, fixed-buffer elements, pointer dereferences, and ref properties/indexers can supply storage. Generic, constructor, interface, virtual, and lambda parameters support the same contract. A function pointer spells the parameter as `void(ref int)*`; it is distinct from `void(int)*`. Reference parameters use native pointer arguments. This does not add general ref locals or ref-returning methods.

Generic call inference inspects typed function pointers recursively, using their
return and parameter types, including nested callbacks. Repeated type parameters
must agree. This also applies to generic extension arguments. Callback arity, ref
parameters, nullability, readonly qualifiers, and calling conventions still obey
normal function-pointer compatibility; inference does not add conversions or
untyped lambda parameters.

Ordinary function declarations require a body, including generic functions and
struct methods. A declaration such as `int Missing();` is an error. Use `extern`
for a function implemented elsewhere; interface and abstract method declarations
retain their bodyless forms. Namespace extern declarations support accessibility
modifiers: `private extern int abs(int value);` and `extern private int abs(int value);`
are equivalent. A private extern is accessible throughout its source file, including
from other namespaces in that file, but cannot be called or addressed from another
file. Its native linker symbol is unchanged. Namespace extern declarations accept only accessibility modifiers; `static` and `protected` are not supported.

## Control flow, exceptions, and constants

Non-void functions must return or throw on every reachable path, or never terminate. Shared control-flow graphs model branches, loops, return, throw, break, and continue. Definite assignment uses these termination facts and tracks assignment states separately.

Functions, constructors, callbacks, and accessors propagate exceptions automatically using native stack unwinding. `throws` has been removed. Function pointers use the ordinary return convention. Windows uses the MSVC exception runtime; Linux uses the Itanium C++ exception runtime. The native transport preserves foreign exceptions across gflat frames, but gflat types do not yet have C++ RTTI or typed C++ catch interoperability.

Exception payloads first use `Memory::Allocate`; if it returns null, the native
exception runtime uses a process-wide emergency reserve. Its own ownership records
and catch handles similarly fall back from system allocation to that reserve.
Released blocks are reused, including when released by another thread. Reserve
storage is never passed to `Memory::Free`. Exhaustion terminates rather than
recursively throwing another allocation failure. The default reserve is 65536
bytes, including internal block metadata; `--exception-reserve-bytes N` selects a
nonnegative byte count, and zero disables it. This is a native link setting, not a
language feature or a requirement for future builds without the exception runtime.
The host C++ runtime still owns native exception transport storage and its own
allocation-failure policy; this setting does not configure its emergency pool.

If the payload allocator reenters exception allocation, the nested request uses
the reserve directly instead of calling that allocator again. An unavailable or
exhausted reserve terminates. This prevents recursive allocation failures; it
does not relax the allocation hooks' non-throwing callback contract.

`throw new Error(...)` transfers a fresh exception-derived value to runtime storage. `throw value` copies and requires a copyable type; `throw move value` consumes it. Pointers cannot be thrown. `catch (ref Error error)` borrows the runtime-owned object, which remains live until the handler exits. The binding aliases the exception value, without a copy or ownership transfer. The older `catch (Error* error)` form also remains available; its pointer is borrowed and must not be deleted or retained. `throw;` inside a handler rethrows the original object, preserving its dynamic type. Throwing a borrowed base value with `throw error` instead copies its static base type. Bare `catch` also handles foreign exceptions; typed gflat catches do not.

Scope destructors and deferred actions run during unwinding. Failed construction destroys completed owning fields and the completed base, without invoking the incomplete object's destructor. Failed `new*` frees its fresh storage; failed placement construction leaves storage ownership with the caller. Raw resources acquired during construction still require explicit cleanup on failure paths.

`void Boundary() nothrow { ... }` permits local handlers but terminates if an exception escapes. Destructors and `main` are also termination boundaries. Cleanup runs up to the boundary; outer callers cannot catch past it. An exception escaping cleanup while another exception is unwinding terminates; cleanup may catch its own failures locally.

Before termination, `Exceptions::Unhandled(Exception*? error)` receives a borrowed pointer, or null for a foreign exception. When declared, this runtime entry point must provide a public, non-generic namespace function returning `void` and taking exactly one mutable nullable `Exception` pointer. Other overloads may coexist; termination selects the canonical signature, including its overload symbol. This weak `void` function lives in `std/libc/Exceptions/ExceptionHandling.gf`. Its default implementation calls libc `exit` with `error.GetCode()`, or 1 for a foreign exception, without printing. Exception constructors default the code to 1. A replacement hook should terminate explicitly; if it returns or throws, the runtime exits with status 1. A custom root `Exception` class must supply `GetCode()`. To use allocating `new*`, it must also support the standard `(message, code)` constructor used by the automatically included `std::BadAllocException`, or supply its own `std::BadAllocException` with a public constructor callable without arguments. Process termination does not unwind the remaining stack.

The compiler driver links a small C++ runtime, requiring Clang's C++ driver and the platform C++ development libraries in addition to LLVM.

`const` functions can execute supported procedural logic during compilation. The evaluator limits execution to **100,000 steps and 256 calls**. It shares numeric conversion rules with the compiler; unsupported operations produce a constant-evaluation diagnostic. Compile-time execution is not a general macro or reflection facility.

Constants follow lexical scope: a local or parameter can shadow a namespace
constant without becoming constant itself. Constant evaluation uses the resolved
declaration, rather than another constant with the same spelling.

Taking a constant variable's address materializes immutable storage for that
declaration. Repeated addresses of the same declaration are stable; distinct
declarations retain distinct storage even when their constant values are equal.
Constant array reads and addresses refer to the same underlying buffer.

Generic bodies resolve names in their declaration context. Specialization does
not capture the caller's locals, aliases, local enums, or control-flow state.
Local enums belong to their enclosing block; identically named enums in different
functions remain distinct types and retain their source names in type metadata.

Ordinary overload signatures in generic types are checked when the containing
type is specialized. Declaration modifier checks and weak/expect/replace selection
still occur before specialization.

## Explicit destructor calls

`pointer.~T()` ends the lifetime of the pointed-to `T` without freeing its
storage. The receiver is evaluated once and must be a non-null, mutable raw
pointer to the named type. `T` may be a generic type parameter or a qualified
type such as `std::String`.

```csharp
void Destroy<T>(T* slot) {
    slot.~T();
}
```

This runs complete destruction: the user-written destructor, if any, followed
by field and base cleanup. Compiler-generated destructors work too. For a type
with no cleanup, including primitives and pointer values, no destructor code
is needed. Destroying a pointer value does not destroy its pointee. Class
destruction uses the same virtual destructor dispatch as `delete`. All classes have a destructor slot, even when their own cleanup is empty. Interface tables also carry a destruction entry; `delete I*` destroys the concrete object and releases its storage. As with any `delete`, the pointer must own an appropriate allocation, not refer to a local or borrowed object.

Destructor calls take no arguments and return `void`. Null receivers trap,
including null hidden by `!`. Managed pointers, readonly pointees, `void*`,
are not accepted. Interface receivers dispatch complete destruction of their concrete object. Explicit destruction cannot execute
during constant evaluation.

The pointer retains its address, but the object is no longer live. Its storage
can be reused with `new* T(args) at pointer` or released separately through
`Memory::Free`. Do not subsequently `delete` the destroyed object unless
it has been reconstructed: `delete` already performs both destruction and
deallocation. Explicit destruction does not cancel automatic scope cleanup;
an automatically cleaned-up local destroyed through its address must be
reconstructed before scope exit. Lifetime correctness through raw pointers
remains the caller's responsibility.

## Placement construction

`new* T(arguments) at destination` initializes a scalar, struct, or class directly in
caller-supplied storage and returns its `T*`. It performs normal field and class
metadata initialization and constructor overload resolution, without allocating.
Only `new*` supports `at`; value and managed construction do not. `at` is contextual
and remains available as an identifier elsewhere.

```csharp
struct Item {
    public int value;
    public Item(int n) { value = n; }
}
int main() {
    Item* storage = (Item*)Memory::Allocate((nuint)sizeof(Item));
    Item* item = new* Item(42) at storage;
    int result = item.value;
    delete item; // This example owns the entire allocation, not an interior slot.
    return result;
}
```

Arguments are evaluated left to right, then the destination is evaluated once,
then initialization begins. If argument or destination evaluation throws, no
initialization has occurred. A placement expression does not schedule automatic
destruction or give a catch handler ownership of a placement-created exception.
The caller manages the object's lifetime and its surrounding allocation.

The destination must be a non-null raw pointer to the exact mutable constructed
type. Null and misaligned addresses trap before initialization, including when
nullability was suppressed with `!`. The caller must provide writable, sufficiently
large storage not occupied by a live object; these conditions are not checked.
Use `delete` only when the pointer is also a valid independently allocated block.
Placement construction cannot execute during constant evaluation.

The destination accepts pointer arithmetic and postfix expressions; comparisons
and assignments bind outside the placement expression. Parenthesize a more
complex destination. To access a member of the constructed object, write
`(new* T() at destination).Member`.

## Nullable local flow

Built-in `== null` and `!= null` checks narrow local variables and parameters on
the appropriate control-flow path. An early return or throw leaves the other
path's facts available. `!`, `&&`, and `||` respect their actual conditional
evaluation, including side effects. User-defined equality operators do not
establish nullability facts. Assigning a known non-null pointer value also
establishes a fact. Paths that both continue retain only facts common to both.

```csharp
int Read(int*? pointer) {
    if (pointer == null) return 0;
    return *pointer;
}
```

Narrowing changes the type of a value read, not the declared variable. A nullable
slot still accepts null assignments and retains its nullable type when passed
with `ref` or addressed with `&`. Readonly qualifications remain intact. Facts
follow bindings across shadowing, rather than matching names alone. Fields and
properties are not narrowed by this first implementation.

Assignment invalidates or updates the affected local's fact. Passing a slot by
`ref` or taking its address marks it as exposed; later calls and unknown writes
invalidate facts for exposed slots. Implicit user calls, copies and destructor
cleanup count too. A write to an exposed slot also invalidates other exposed
slots' facts because ref parameters may alias. Ref parameters are exposed from entry. This is conservative
null checking, not alias, lifetime, or ownership proof; unsafe raw-memory access
can bypass its assumptions.

Loops initially discard incoming and initializer facts. A loop condition can
establish fresh facts for its body; facts do not carry out of the loop or through
its increment. Catch bodies start without assumptions about outer nullable
locals. Deferred bodies must establish their own facts because they execute
later; their possible writes invalidate affected facts at registration and scope
exit. These restrictions can require an additional guard or explicit `!`.

## Nullability suppression

Postfix `!` removes only the outer nullability of a raw pointer, managed reference,
or function pointer. It preserves readonly qualification, evaluates the operand
once, and does not change the original variable's type. It also accepts already
non-null pointer types. Bare `null!` has no pointer type and is rejected.

```csharp
int Read(int*? pointer) {
    return *pointer!;
}
```

`pointer!` performs no runtime check and emits no LLVM non-null assumption. It is
an explicit escape hatch: using an invalid result can fault or cause undefined
behavior. Explicit casts to non-null pointer types remain checked and trap on
null in every build. To examine an unchecked pointer for null, it can be widened
to its nullable type again. Suppression does not bypass checks performed by other
operations, such as placement construction's destination validation.

## Custom string literals

A non-generic class or struct can declare a public static string literal operator. The operator takes exactly `readonly(char)* data, nuint length` and returns its containing type. The parameter names can differ. It executes as an ordinary runtime function and follows the normal exception rules.

```csharp
using Text;

namespace Text
{
    class View
    {
        public readonly(char*) data;
        public nuint length;

        public View(readonly(char)* p, nuint n) { data = p; length = n; }

        public static View operator s""(readonly(char)* p, nuint n)
        {
            return new View(p, n);
        }
    }
}

int main()
{
    View message = s"hello\n";
    return (int)message.length; // 6
}
```

The prefix is an ordinary case-sensitive identifier, including names such as `my_2string`. The empty `""` marker must immediately follow the identifier in the declaration. At the use site, the prefix must immediately precede the literal's opening quote.

Prefixes are visible in their declaring namespace, its descendants, and namespaces imported by `using`. An explicit qualifier such as `Text::s"hello"` works without an import. Nested namespaces use `::`, including in `using` directives. Duplicate prefixes within one namespace are errors; multiple visible candidates from different namespaces require qualification. Result types do not disambiguate prefixes.

`data` points to immutable, null-terminated static storage containing UTF-8 bytes after escape processing. Supported escapes are `\n`, `\r`, `\t`, `\0`, `\\`, `\"`, and `\'`; unknown escapes are errors. `length` is the number of bytes excluding the final terminator; embedded null bytes count toward it. The storage lasts for the program's lifetime. An operator may keep a readonly view or copy the bytes into its own storage, and decides whether to allocate. The compiler adds no intermediate string object. Returned objects follow normal scope and temporary cleanup rules.

Literal operators have no implicit instance (`this`), but retain access to their containing type's private members through explicit instances. `const` literal operators, generic owners, and raw-literal extensions are not supported in this version.

## String interpolation

```csharp std
using std;

int main()
{
    String text = s$"Score: {42}; status: {true}";
    String nested = s$"[{text}] {{literal braces}}";
    return 0;
}
```

Include `std/core/String.gf` and `std/core/Formatting.gf`, or reference `std/std.gfproj`.
Floating-point conversion additionally uses `std/libc/Formatting.gf` and libc's `snprintf`.

The prefix follows the same namespace lookup and ambiguity rules as ordinary custom literals,
including qualified syntax such as `std::s$"{42}"`. Unprefixed `$"..."` is
not supported. Implementing a literal operator alone does not opt into interpolation.
Its containing/result type must implement the automatically available global interface `ITextWriter` from `std/core/Text.gf`:

```text
interface ITextWriter
{
    void Write(readonly(char)* data, nuint length);
}
interface IFormattable
{
    readonly void Format(ITextWriter* output);
}
```

The compiler calls the destination's literal operator with empty text, then processes
segments and holes in source order. Literal segments call `Write` with UTF-8 bytes
and their explicit count. A writer must consume or copy these bytes before returning.
It may not retain either the input pointer or a formatter's borrowed destination.

Custom values, pointers, and managed references implement `IFormattable`; holes call
`Format` with the destination. Alternatively, `IStringConvertable` declares
`readonly std::String ToString()`: the hole converts once, writes the returned
string's counted bytes, and destroys the owning temporary. `IFormattable` takes
precedence when both contracts are implemented. Interface views support dynamic dispatch. Nullable
receivers require a checked cast or unchecked postfix `!`. Primitives call the exact
`global::std::Format(value, writer)` overload independently of local using directives.
Supported primitives are `bool`, `char`, `byte`, `sbyte`, `short`, `ushort`, `int`,
`uint`, `long`, `ulong`, `nint`, `nuint`, `float`, and `double`. Character pointers and
arrays are not implicitly converted to text; provide an explicit string value.

Formatting does not imply a heap allocation, a terminator scan, or a caller-owned
C-string. `std::String` writes its existing counted bytes, preserving embedded nulls.
Its `ToString()` method returns an independent owning string. Its optional
`ToCString()` method borrows readonly data, valid only while the original buffer
remains alive and unchanged; interpolation never implicitly uses that operation.

Each hole is evaluated once. Temporary receivers remain alive through formatting
and are then destroyed. If formatting or writing throws, the partial destination
and successfully constructed temporaries are destroyed. Normal exception propagation
and termination boundaries apply. Completed results follow ordinary owned-value rules.

This version does not define alignment, format specifiers (`{value:format}`), raw strings,
implicit interpolation-to-type conversions, or compile-time interpolation.

## Attributes

Attributes are classes derived, directly or indirectly, from the abstract `Attribute` class in `std/core/Attribute.gf`. The base class has a public parameterless constructor and no libc dependency. Its name is reserved at global scope.

```csharp
class Priority : Attribute
{
    public int level;
    public Priority(int value) { level = value; }
}

[Priority(2 + 3)]
int main() { return 0; }
```

`[Tag]` and `[Tag()]` invoke the parameterless constructor. `[Tag(arguments)]` uses ordinary constructor overload resolution and conversions, and checks constructor accessibility. Names must match exactly; there is no implicit `Attribute` suffix. Namespace and nested-type qualification use `::`. Multiple annotations are allowed and retained in source order.

Arguments and construction must be evaluable at compile time. Constructors need not be marked `const`; functions they call must be. Evaluation runs base constructors, field initializers, and the selected constructor body, subject to the compile-time execution limits. Runtime input, extern calls, managed allocation, destructors, and unsupported evaluator operations are rejected. Attribute objects exist only as compiler metadata; annotation sites perform no runtime construction or allocation.

Annotations can precede types, functions, extern declarations, fields, properties, constructors, destructors, operators, and aliases. They are retained on generic declarations and copied to specializations; arguments on generic definitions must be evaluable independently of their type parameters. Parameter, local-variable, and assembly annotations are not supported. Arguments are positional; named property arguments, target restrictions, suffix lookup, and runtime reflection remain future work. User-defined attributes do not change compiler behavior by themselves.

## Compilation across source files

A compilation accepts multiple named source snapshots and produces one LLVM module. Each file is parsed independently; declarations are collected across all files before bodies are checked. Source argument order does not select definitions. Namespaces may span files, while classes cannot be split across declarations (partial classes remain unsupported).

`using` directives apply only to their source file. They affect name lookup, not which files are compiled. Types and generic functions have namespace-qualified identities; same-named types in separate namespaces remain distinct. Ambiguous imports require explicit qualification. Generic bodies retain their definition's namespace and imports when instantiated from another file.

Compilation setup automatically includes `std/core/Attribute.gf`, `std/core/Exception.gf`, `std/core/Text.gf`, and `std/libc/Memory/Memory.gf`. These ordinary source files are shipped in the compiler?s `Std` directory. Their current global names remain available without imports. The default `Exception` is omitted if explicitly supplied by the program. Explicit inputs at these std-relative paths take precedence over bundled files and are not included twice. The syntax parser itself does not inject declarations. `weak`/`replace` selection covers the complete compilation. Duplicate declaration diagnostics retain both source locations.

The C# compiler API accepts immutable `SourceFile(path, text)` snapshots through `Compiler.Check(IEnumerable<SourceFile>, diagnostics)` and `Compiler.Emit(IEnumerable<SourceFile>, diagnostics)`. The path identifies the snapshot; the compiler does not read its text from disk. This permits unsaved editor buffers and independent command-line or IDE hosts. Source spans use UTF-16 offsets and one-based lines and columns. Diagnostics expose their source span, code, severity, message, and related declaration locations.

## Compiler use

```text
dotnet run --project gflat -- main.gf helpers.gf -o program.exe
dotnet run --project gflat -- source.gf --emit-ir -o program.ll
dotnet run --project gflat -- source.gf --run
```

The CLI accepts one or more explicit source paths. No project file is required, and `using` does not load files. Without `-o`, the output name is derived from the first input path. Duplicate input paths are rejected; output may not overwrite any input.

Compilation does not execute output unless `--run` is requested. `--clang path` or `GFLAT_CLANG` selects Clang; otherwise PATH and Visual Studio installations are searched. `--target` accepts the supported triple only. Native compilation and execution have timeouts and concurrently captured output.

`GFLAT_OPT_LEVEL` selects Clang optimization (`0`, `1`, `2`, or `3`; default `0`). It applies to native CLI compilation and executable tests. CI runs the suite at both `-O0` and `-O2`. To reproduce the optimized run in PowerShell:

```powershell
$env:GFLAT_OPT_LEVEL = '2'
dotnet test
Remove-Item Env:GFLAT_OPT_LEVEL
```

Exit status: 1 for source errors, 2 for usage/toolchain errors, 3 for internal compiler errors. With `--run`, successful compilation returns the program's exit status. Source/output paths must differ.

## Future work

Additional target ABIs, verified out parameters, full ownership and partial moves, managed finalizers and moving collectors, member reflection, and C++ interoperability are proposals, not current guarantees. Compiler architecture can progressively replace AST-based lowering with a richer checked representation; the shared control-flow graph is the current foundation.

## Name queries

`nameof(name)` produces a null-terminated static `readonly(char*)` containing the
final identifier's source spelling. It accepts local and parameter names, types,
aliases, namespaces, and qualified member names. Overloaded methods name the
method group without selecting an overload. Instance members may be named through
a type, nullable reference, or uninitialized variable; no receiver is read and no
getter or setter runs. Normal member accessibility still applies. Calls, object
creation, indexing, and other runtime expressions are not named entities and are
rejected. Generic parameter names retain their source spelling after specialization.

## Layout queries

`sizeof(T)` returns the size in bytes and `alignof(T)` returns the required ABI
alignment in bytes. Both produce an `int` compile-time constant, can appear in
constant expressions and fixed-array lengths, and work through aliases and generic
specialization. Like `sizeof`, `alignof` also accepts a simple variable name to query
its declared type; it does not evaluate a value expression.

Concrete array and aggregate layouts are currently limited to 2147483647 bytes.
`sizeof` and construction diagnose layouts exceeding this implementation
limit, including field and tail padding; size arithmetic does not wrap. Declaring
a raw pointer does not require calculating its pointee's complete size. Inline value
fields and base classes must form a finite layout: direct or indirect cycles are
rejected even in unused declarations. Raw pointers and managed references do not
embed their pointee's instance layout and may be recursive. Static fields do not
form part of their declaring type's instance layout.
Nested generic type and function specialization share a limit of 64 active specializations;
this compiler resource limit prevents unbounded generic expansion from exhausting
the compiler stack and does not limit the total number of specialized types or functions.

For a class type these queries describe the complete object, including its vptr
and padding. For `T*` and `T^` they describe the reference representation instead.
An interface reference has two pointer-sized words but pointer alignment. Fixed
arrays have their element type's alignment. `readonly(T)` preserves T's layout.
Following the existing `sizeof(void) == 0` convention, `alignof(void)` is 1.
Values follow the host target ABI: `x86_64-pc-windows-msvc` on Windows or
`x86_64-unknown-linux-gnu` on Linux.

```csharp
struct Packet { public char tag; public long payload; }
const int Alignment = alignof(Packet);
int main() {
    int[alignof(double)] scratch;
    if (Alignment != 8 || sizeof(Packet) != 16) return 1;
    return 0;
}
```

## Inline hints

`inline` requests inlining of a concrete function, method, constructor, destructor,
operator, or property accessor. It is an optimization hint, not a guarantee, and
changes neither visibility nor linkage. On a property it applies to its accessors.
Abstract and extern declarations cannot use it. Optimization level 0 generally
keeps calls; optimized builds let LLVM decide whether inlining is beneficial.

```csharp
inline int Add(int left, int right) => left + right;
int main() { return Add(20, 22); }
```

## Static type metadata

`typeof(T)` produces a compile-time constant `std::TypeInfo` describing the static
queried type. It works through aliases and generic specialization. Its readonly
fields are `Name`, `Size`, `Alignment`, `IsPointer`, `IsManaged`, `IsNullable`,
`IsReadOnly`, and `HasDestructor`. It also exposes `Kind`, `HasLayout`,
`ElementType`, `HasFixedLength`, `Length`, `ReturnType`, `ParameterTypes`, and
`ParameterCount`. Sizes, alignments, lengths, and counts use `nuint`; names are
borrowed, null-terminated strings with static lifetime. Aliases resolve to their
underlying type name.

`Kind` distinguishes primitives, classes, structs, enums, interfaces, raw pointers,
managed pointers, arrays, function pointers, and references. `ElementType` points
to immutable static metadata for a pointer/managed/array/reference element; it is
null for other kinds. Nested elements remain available during constant evaluation.
Function pointers have a non-null `ReturnType` descriptor, including for void
returns, and a `ParameterCount`. `ParameterTypes` points to a contiguous immutable
array of descriptors; it is null when the count is zero. Index only within that
count. Nested function signatures and reference/readonly/nullable parameter
layers remain available recursively and during constant evaluation. Non-function
kinds use null return/parameter pointers and a zero count. No GC allocation is
needed for descriptors, and their storage survives the function producing them.
Function-pointer types do not include parameter names, optional defaults, or
method modifiers, so this metadata does not invent those properties.

`IsReadOnly` qualifies the current layer's value storage. For `readonly(int)*`,
the pointer's flag is false and its element's flag is true. For `readonly(int*)`,
both flags are true because readonly qualification propagates through the pointer.
For `int**`, follow `ElementType` twice to inspect the `int` metadata.

`HasLayout` is false for void and interface elements, whose `Size` and `Alignment`
are zero. Their pointers have their own valid layouts. Direct `typeof(I)` remains
invalid when `I` is an interface, while `typeof(I*).ElementType` describes it.
`HasFixedLength` is true only for fixed arrays; `Length` is their element count,
zero otherwise. Array and pointer element metadata do not flatten qualifiers.

Traits describe the queried type itself: `typeof(Resource*).HasDestructor` is
false even when `Resource` has a destructor. `IsPointer` describes raw and function
pointers; managed references use `IsManaged`. This is not a runtime type registry
and does not yet enumerate fields, methods, or properties. `typeof(expression)`
is not supported.

```csharp
struct Packet { public int sequence; }
const nuint Size<T>() => typeof(T).Size;
const nuint PacketSize = Size<Packet>();
int main() {
    std::TypeInfo info = typeof(Packet);
    if (info.Size != PacketSize) return 1;
    return 0;
}
```

```csharp
const nuint ArgumentSize = typeof(void(int**)*).ParameterTypes![0].ElementType!.ElementType!.Size;
int main() {
    std::TypeInfo signature = typeof(void(ref readonly(int), int**)*);
    if (signature.ParameterCount != (nuint)2) return 1;
    if (signature.ReturnType!.HasLayout || ArgumentSize != (nuint)4) return 2;
    return 0;
}
```

## Inline LLVM

Advanced code can call `__llvm<Result>("body IR", arguments...)`. This reserved
unqualified builtin emits a private `alwaysinline` helper whose argument types
come from the gflat expressions and whose result type is explicit. LLVM arguments
are `%0`, `%1`, and so on; the body must provide its own terminator.

```csharp
int Add(int a, int b) => __llvm<int>("%sum = add i32 %0, %1\nret i32 %sum", a, b);
int main() => Add(20, 22);
```

`__llvm_module<Result>("module IR", "entry", arguments...)` also accepts external
declarations and helper definitions, for example LLVM intrinsics. Its entry must
be an unquoted symbol defined in that module, with exactly matching types and the
default calling convention. Defined symbols receive unique names per call site;
identical external declarations are shared. Quoted symbol names, target
directives, attribute groups, metadata, comdats, variadic entries, and entry ABI
parameter attributes are currently unsupported. Function attributes written
inline remain available.

Both forms require ordinary string literals. Arguments are evaluated once, in
source order. Primitive values, enums, function pointers, and raw pointers are
supported; managed references, language references, interface fat pointers, and
aggregate values are rejected. Use explicit raw pointers for aggregate storage.
The raw code must respect pointed-to storage lifetimes, must not unwind through
the helper, and must not bypass managed-reference bookkeeping. LLVM instructions,
calling conventions, alignment requirements, and target portability are the
caller's responsibility. Native compilation verifies the supplied LLVM syntax;
`--check` does not parse or validate LLVM instructions.

This escape hatch is experimental. It does not configure a GC strategy on gflat
functions, emit stack maps/statepoints, or supply a collector integration protocol.
Those remain separate work; using an LLVM GC intrinsic alone does not establish
that integration.


### Expected implementations and provider aliases

`expect` declares a namespace function or static member whose implementation must
be supplied by exactly one matching `replace` declaration in the compilation.
`weak` supplies a default body instead. These modifiers are mutually exclusive.
They apply to namespace functions and static methods. Instance methods use
`abstract`, `virtual`, and `override` for implementations selected through inheritance.
Qualified namespace function declarations use their named owner independently of
file order. Qualified static definitions implement an `expect` or `weak` member
already declared inside its closed type.

```csharp
namespace Services { public expect int Read(); }
replace int Services::Read() => 42;

interface ILogger { static void Write(readonly(char*) text); }
static class ApplicationLogger : ILogger {
    public static void Write(readonly(char*) text) { }
}
public expect alias Logger : ILogger;
replace alias Logger = ApplicationLogger;
```

Aliases may declare an optional default with `weak alias Name = Type`.
An `expect alias` has no initializer. Missing implementations, multiple defaults,
multiple replacements, and replacement of an ordinary alias are errors. Alias
constraints follow `:`. They validate the selected implementation and limit
static member access through the alias to the declared contract.
Provider aliases are namespace declarations; nested provider aliases inside types
are not supported yet. Ordinary local aliases retain a constrained provider's
contract, and generic forwarding does not expose implementation-only members.
