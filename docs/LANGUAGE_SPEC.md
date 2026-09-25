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
type          = ( qualifiedType | "readonly", "(", type, ")" ), { suffix } ;
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

## Weak defaults and explicit replacements

Ordinary free functions, static methods, and concrete instance methods support overloads by parameter type and count. Exact argument types are preferred over implicit conversions; a candidate must be no worse for every argument and better for at least one. Equally applicable candidates are ambiguous, independent of declaration order. Constructor calls use the same ranking. Return types, readonly qualifications, and declaration modifiers cannot distinguish overloads.

Overloaded generic function families and overloaded virtual/interface declarations are not yet supported and are diagnosed. An ordinary overloaded implementation can satisfy an interface's matching signature. Taking an overloaded function's address requires an unambiguous wrapper. Function addresses cannot discard `throws`, const-parameter requirements, or a variadic calling convention; those forms are rejected until callable types can express them.

`weak` marks a function's default implementation. `replace` explicitly selects a different body for that function in the same namespace or containing type. Declaration order does not matter; reopened namespace blocks share the same scope.

```gflat
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

A replacement must match parameter types, return type, generic arity and constraints, accessibility, `static`, `virtual`, `override`, `readonly`, `const`, const parameters, and `throws`. Parameter names and generic parameter names may differ. Duplicate defaults, multiple replacements, unmatched replacements, and ordinary definitions attempting to replace a default are errors. Weak functions cannot be overloaded or combined with an extern declaration.

Free functions and concrete class/struct methods support these modifiers. Static methods use `Type::Method(...)` and have no implicit `this`; instance methods use `value.Method(...)`. Replacement stays within the same containing type: inherited dispatch still uses `override`. Constructors, destructors, operators, abstract methods, and interface declarations cannot be weak or replaced. Partial classes are not implemented.

## Pointers and allocation

`T*` expresses non-null intent; `T*?` permits null. Null and zero constants cannot be explicitly cast to a non-null pointer. Explicit casts to non-null pointer types check their result at runtime and trap on null. This check proves neither bounds nor lifetime. Default construction and foreign contracts must not be assumed to prove complete object validity.

`new Type(...)` constructs an inline value. `new* Type(...)` allocates storage, checks for allocation failure, initializes it, and invokes its constructor. Failed allocation traps before any store or constructor call. This fail-fast operation does not add a checked exception effect. `delete p` destroys and frees an allocated object; it does not make aliases safe or permit repeated deletion.

The public allocation hooks are `Allocator::Allocate(ulong size) -> void*?` and `Allocator::Free(void* ptr) -> void`. Prelude provides public weak definitions calling libc `malloc` and `free`. `new*`, `delete`, and exception cleanup use these same functions. Applications may call them directly. Custom allocators must replace **both** functions with matching public, non-throwing signatures:

```gflat
namespace Allocator {
    public replace void*? Allocate(ulong size) { return malloc(size); }
    public replace void Free(void* ptr) { free(ptr); }
}
```

`Allocate` must return suitably aligned storage or null. Compiler-generated allocations trap on null before initialization; direct calls expose the nullable result. The default extern declaration is `extern void*? malloc(ulong size)`. Extern declarations with stronger contracts are the programmer's responsibility. Libc remains a toolchain dependency for now; replacing these functions does not select a freestanding build. The old `__gflat_alloc`, `__gflat_free`, and `#no_default_allocator` interfaces are rejected with migration diagnostics.

Arrays have inline storage. Array-to-pointer decay does not establish bounds or ownership. Uninitialized stack arrays are buffers; allocation alone does not guarantee initialized elements. Initialize elements before reading them.

Fixed-size array parameters receive independent copies, including nested arrays. Use an explicit element pointer or pointer to a whole fixed-size array to share storage. Unsized array parameters are rejected; spell buffer parameters as pointers. Array value conversions preserve dimensions and element representation; cast individual elements explicitly when changing numeric types. Values requiring destruction cannot be passed by value.

Nullable raw pointers and function pointers require an explicit checked non-null cast before dereference, indexing, member access, calls, or pointer arithmetic. A comparison with `null` does not change the variable's static type. The cast traps if the value is null. Function pointer conversions preserve the exact return and parameter types, including readonly qualifications; implicit numeric return conversions do not change a function's calling convention.

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

Copying a readonly scalar into a mutable scalar is allowed. Copying a pointer retains its pointee qualification. Copying an aggregate into a mutable aggregate is rejected if that would expose mutable aliases through pointer fields. Arrays of scalar values can be copied by value. Nested pointer conversions must preserve readonly at every writable level: `int**` can become `readonly(int*)*`, but cannot become `readonly(int)**`.

Aliases and generic arguments preserve qualification. Explicit raw-pointer casts may remove readonly; managed-pointer casts cannot. `const` remains the compile-time constant mechanism.

This replaces the old prefix type syntax: write `readonly(char)*` instead of `readonly char*`. For an old readonly pointer field, use `readonly(char*)` to keep both the field and pointee readonly. Bare `readonly` is now exclusively a receiver modifier on methods and property accessors.

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

The hook may instead be defined in gflat. It must be non-generic and non-throwing, accept exactly one `ulong` byte count, and return writable `void*?` or `void*` storage with suitable alignment. An extern declaration requires the implementation to be linked by the native toolchain. No default GC or Boehm adapter is bundled. Merely declaring managed references does not require the allocation hook. The hook is independent of `Allocator::Allocate` and `Allocator::Free`.

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

Structs provide inline fields, constructors, methods, properties, and optional destructors. Classes add a vtable and single implementation inheritance. Interfaces provide method and property contracts with fat pointer dispatch. These are gflat layouts, not a C++ ABI. Foreign aggregate compatibility requires dedicated ABI tests; use pointers and primitive C interfaces for interoperation.

## Properties

Classes and structs support explicit accessors, expression-bodied getters, and auto-properties:

```gflat
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

`get` and `set` are contextual identifiers. The setter receives an implicit parameter named `value` with the property's type and returns `void`. Accessors accept blocks or `=> expression;`; a property-level `=> expression;` is shorthand for a getter. An explicit property may have only a getter or only a setter. An auto-property must have a getter, and all its accessors must use `;`. Auto-property storage is private, is zero-initialized by ordinary construction, and participates in field initialization in declaration order. A get-only auto-property can also be assigned through `this` in its declaring instance constructor.

Properties use ordinary member accessibility. On a property with both accessors, one accessor may declare more restrictive access, such as `private set` or `protected set`. Access is checked separately for reads and writes. `readonly get` allows calls through a readonly receiver while permitting a mutable setter. Non-virtual auto-getters are implicitly readonly. `readonly(T)` qualifies the property's result type; bare `readonly` qualifies the receiver, as on methods.

An implicitly readonly auto-getter cannot return a writable pointer stored in its receiver. Declare a pointer to readonly storage, or use an explicit mutable getter when callers need writable access. The same restriction applies to returned aggregates containing writable pointers.

Reads call the getter. Assignments call the setter and evaluate to the assigned value, even if the setter changes its `value` parameter. Compound assignments and `++`/`--` evaluate the receiver once, then read, update, and write. Postfix updates return the old value; prefix updates return the new value. The receiver is evaluated before the right-hand side. Normal operator resolution, definite-assignment checks, and temporary cleanup apply. A property is not addressable storage: taking its address or assigning to a field of an inline property result is rejected. A pointer returned by a property still permits access to its pointee under ordinary pointer rules.

Classes support `virtual`, `override`, and `abstract` properties through their accessors. Interface property contracts use accessor signatures, for example `interface ICount { int Count { get; set; } }`; implementing accessors must be public. A declaration in a derived class hides the whole inherited property, rather than borrowing missing accessors from it. Explicit static properties use `Type::Property`. Explicit class/struct accessors may declare `get throws` or `set throws` and follow ordinary exception rules.

Current restrictions: static auto-properties await static field storage; indexers, `init`, ref returns, throwing interface accessors, and compile-time property evaluation are not implemented. Properties cannot have type `void`, return arrays or destructor-bearing values by value, or be `const`, `weak`, `replace`, or individually generic. Pointer properties can expose owned storage without copying it. Properties in generic containing types are supported. Property annotations are retained on the property declaration; they are not copied onto synthetic accessors or backing fields.

## Control flow, exceptions, and constants

Non-void functions must return or throw on every reachable path, or never terminate. Shared control-flow graphs model branches, loops, return, throw, break, and continue. Definite assignment uses these termination facts and tracks assignment states separately.

Throwing functions declare `throws`. Callers must catch or declare propagation. The generated ABI uses a value/status tuple, not native C++ unwinding. `main` cannot declare `throws`. Catch cleanup runs deferred actions before destroying and freeing an owned exception, including exits by return, break, continue, or another throw. Rethrowing the caught pointer transfers its release responsibility. An exception escaping deferred cleanup replaces the pending exception, releasing the previous owned exception; remaining cleanup runs once.

`const` functions can execute supported procedural logic during compilation. The evaluator limits execution to **100,000 steps and 256 calls**. It shares numeric conversion rules with the compiler; unsupported operations produce a constant-evaluation diagnostic. Compile-time execution is not a general macro or reflection facility.

## Custom string literals

A non-generic class or struct can declare a public static string literal operator. The operator takes exactly `readonly(char)* data, ulong length` and returns its containing type. The parameter names can differ. It executes as an ordinary runtime function and may declare `throws`; callers must follow the normal exception rules.

```gflat
using Text;

namespace Text
{
    class View
    {
        public readonly(char*) data;
        public ulong length;

        public View(readonly(char)* p, ulong n) { data = p; length = n; }

        public static View operator s""(readonly(char)* p, ulong n)
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

The prefix is an ordinary case-sensitive identifier, including names such as `my_2string`. The empty `""` marker must immediately follow the identifier in the declaration. At the use site, the prefix must immediately precede the literal's opening quote. `c` remains reserved for built-in C strings.

Prefixes are visible in their declaring namespace, its descendants, and namespaces imported by `using`. An explicit qualifier such as `Text::s"hello"` works without an import. Nested namespaces use `::`, including in `using` directives. Duplicate prefixes within one namespace are errors; multiple visible candidates from different namespaces require qualification. Result types do not disambiguate prefixes.

`data` points to immutable, null-terminated static storage containing UTF-8 bytes after escape processing. Supported escapes are `\n`, `\r`, `\t`, `\0`, `\\`, `\"`, and `\'`; unknown escapes are errors. `length` is the number of bytes excluding the final terminator; embedded null bytes count toward it. The storage lasts for the program's lifetime. An operator may keep a readonly view or copy the bytes into its own storage, and decides whether to allocate. The compiler adds no intermediate string object. Returned objects follow normal scope and temporary cleanup rules.

Literal operators have no implicit instance (`this`), but retain access to their containing type's private members through explicit instances. `const` literal operators, generic owners, interpolation operators, and raw-literal extensions are not supported in this version.

## Attributes

Attributes are classes derived, directly or indirectly, from the abstract `Attribute` class in the prelude. The base class has a public parameterless constructor and no libc dependency. Its name is reserved at global scope.

```gflat
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

Prelude is supplied once by compilation setup, including `Attribute`, the default `Exception` unless explicitly supplied by the program, and the weak allocator defaults. The syntax parser itself does not inject declarations. `weak`/`replace` selection covers the complete compilation. Duplicate declaration diagnostics retain both source locations.

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

Additional target ABIs, verified out parameters, full ownership and partial moves, managed finalizers and moving collectors, interpolation, reflection, and C++ interoperability are proposals, not current guarantees. Compiler architecture can progressively replace AST-based lowering with a richer checked representation; the shared control-flow graph is the current foundation.
