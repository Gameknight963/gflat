# gflat Language Specification & Architecture Audit

**Version:** 0.2.0 (Pre-Alpha)  
**Target Backend:** LLVM IR  
**Document Status:** Living Reference  

---

## 1. Executive Summary & Design Philosophy

`gflat` is a statically-typed, compiled systems programming language designed to combine the performance, low-level memory control, and layout predictability of C/C++ with modern safety and ergonomics inspired by C#, Rust, and Zig.

### Core Philosophy
1. **Predictable Performance:** No hidden heap allocations in language constructs. Structs are value types; stack classes provide polymorphic behavior without heap indirection.
2. **Explicit Nullability:** Pointers are non-nullable by default (`T*`). Nullable pointers must be explicitly declared (`T*?`).
3. **Deterministic Lifetime (RAII):** Scope-based destruction for stack-allocated structs and classes, unified with a first-class `defer` mechanism.
4. **Compile-Time Evaluation:** First-class `const` functions and expressions capable of executing arbitrary procedural logic during compilation.
5. **Cross-Platform Type Consistency:** Fixed integer bitwidths across all operating systems to avoid the C/C++ data model fractures (LP64 vs. LLP64).

---

## 2. Lexical Structure & Grammar

### 2.1 Identifiers & Keywords
- **Identifiers:** Match `^[a-zA-Z_][a-zA-Z0-9_]*$`. Identifiers beginning with `__` are reserved for internal compiler use.
- **Sigils:** Sigils like `$` are lexical tokens (e.g. for string interpolation) and are strictly banned inside identifier names.
- **Reserved Keywords:**
  `if`, `else`, `while`, `for`, `foreach`, `in`, `return`, `break`, `continue`, `defer`, `throw`, `try`, `catch`, `const`, `readonly`, `static`, `virtual`, `override`, `abstract`, `public`, `private`, `protected`, `internal`, `struct`, `class`, `interface`, `enum`, `alias`, `namespace`, `using`, `extern`, `new`, `sizeof`, `nameof`, `default`, `operator`.

### 2.2 Comments
- Line comments: `// ...`
- Block comments: `/* ... */` (can span multiple lines).

---

## 3. Type System

### 3.1 Primitive Types & Sizing Guarantees

| Type | LLVM Type | Bitwidth | Representation | Description |
| :--- | :--- | :--- | :--- | :--- |
| `void` | `void` | 0 | Unit | Absence of a value |
| `bool` | `i1` | 1 | Boolean | `true` (1) or `false` (0) |
| `byte` | `i8` | 8 | Unsigned | 8-bit unsigned integer ($0$ to $255$) |
| `sbyte` | `i8` | 8 | Signed | 8-bit signed two's complement ($-128$ to $127$) |
| `short` | `i16` | 16 | Signed | 16-bit signed integer |
| `ushort` | `i16` | 16 | Unsigned | 16-bit unsigned integer |
| `char` | `i8` | 8 | ASCII / Raw Byte | UTF-8 byte representation |
| `int` | `i32` | 32 | Signed | **Guaranteed 32-bit signed integer** |
| `uint` | `i32` | 32 | Unsigned | **Guaranteed 32-bit unsigned integer** |
| `long` | `i64` | 64 | Signed | **Guaranteed 64-bit signed integer** |
| `ulong` | `i64` | 64 | Unsigned | **Guaranteed 64-bit unsigned integer** |
| `isize` | `i64` (on x64) | Pointer | Signed | Native pointer-sized integer |
| `usize` | `i64` (on x64) | Pointer | Unsigned | Native pointer-sized unsigned integer |
| `float` | `float` | 32 | IEEE 754 | 32-bit single-precision float |
| `double` | `double` | 64 | IEEE 754 | 64-bit double-precision float |

#### Architectural Guarantee: Fixed Integer Sizing
Unlike C and C++ where `long` is 32 bits on 64-bit Windows (LLP64) and 64 bits on 64-bit Linux (LP64), `gflat` enforces **strictly fixed sizes**:
- `int` is ALWAYS 32 bits.
- `long` is ALWAYS 64 bits.
- Pointer-sized integers are explicitly represented by `isize` and `usize`.

**Status:** **HOLDING UP.** The type checker and LLVM emitter map `int` -> `i32` and `long` -> `i64` uniformly on all platforms.

---

### 3.2 Pointer & Reference Types

`gflat` distinguishes raw memory pointers from managed references and pointer nullability:

1. **Non-Nullable Raw Pointer (`T*`):**
   - Unmanaged memory address.
   - **Guaranteed non-null at initialization.** Cannot be assigned `null`, cannot be assigned `default`, and cannot be initialized without a valid address or allocation.
2. **Nullable Raw Pointer (`T*?`):**
   - Memory address that may be `null`.
   - Can receive `null` or `default(T*?)`.
3. **Read-Only Pointers (`readonly T*`):**
   - The memory pointed to is immutable through this pointer.
   - Prevents mutation of underlying memory.
   - String literals `"..."` decay to `readonly char*`.
4. **Managed References (`T^`):**
   - Syntax for references intended for runtime tracking / managed memory.
   - Cannot be assigned `default`.
5. **Function Pointers (`fn(param_types)*`):**
   - Low-level function address. Non-nullable by default.

---

### 3.3 Arrays

1. **Fixed-Size Stack Arrays (`T[N]`):**
   - Allocated directly in the current stack frame.
   - Size `N` must be a compile-time constant expression.
   - Stack arrays of characters can be initialized directly with string literals:
     ```gflat
     char[16] buffer = "hello";
     ```
2. **Array Decay:**
   - Arrays decay to pointers when passed to functions or assigned to pointer variables.

---

### 3.4 Type Aliases & Enums

- **Aliases:** Declared via `alias NewName = OldType;`. Resolved transparently during type checking.
- **Enums:** Strongly typed integral enumerations.
  - Can specify an explicit underlying type: `enum Flags : u32 { ... }`.
  - Supports arbitrary constant expressions and bitwise operations in member initializers:
    ```gflat
    enum Permissions
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        All = Read | Write
    }
    ```
  - Bitwise operators (`|`, `&`, `^`) on identical enum types preserve the enum type.

---

## 4. Memory Model & Object Lifetimes

### 4.1 Value Types (`struct`)

- **Semantics:** Value type. Stored inline in stack frames, containing structs, or arrays. Copied on assignment.
- **Methods:** Can define constructors, destructors, instance methods, and static methods.
- **Destructors (`~StructName()`):**
  - Invoked automatically when a local struct variable leaves its enclosing block scope.
  - Chained in reverse order of declaration (LIFO).
  - Executed on normal block exit, early `return`, `break`, `continue`, and exception unwinding.

---

### 4.2 Reference / Class Types (`class`)

- **Semantics:** Reference type containing a virtual method table pointer (`vptr`) at offset 0.
- **Storage Locations:**
  1. **Stack Classes:** Allocated directly on the stack without `new`:
     ```gflat
     MyClass c(10, 20); // Stack-allocated instance of MyClass
     ```
     - Destructor is automatically queued on the scope's defer stack.
     - Destructors are null-safe and idempotent (clears the vtable slot upon execution).
  2. **Heap Classes:** Allocated via `new MyClass(...)`:
     - Returns a pointer `%MyClass*`.
     - Dynamically allocated via `malloc`.

---

### 4.3 Deterministic Cleanup: `defer` & Foreach Disposal

- **`defer` Statement:** Executes arbitrary statements or blocks when the enclosing lexical scope exits:
  ```gflat
  FILE* f = fopen("data.txt", "r");
  defer fclose(f);
  ```
- **Foreach Loop RAII:**
  - `foreach (var item in collection)` calls `.GetEnumerator()`.
  - If the enumerator defines `Dispose()`, it is automatically registered with `defer` to ensure cleanup even if exceptions occur or loops exit early via `break`.

---

## 5. Object Model & Polymorphism

### 5.1 Single Inheritance & Class Hierarchy
- Classes support single implementation inheritance:
  ```gflat
  class Animal { public virtual void Speak() {} }
  class Dog : Animal { public override void Speak() {} }
  ```
- **Virtual Dispatch:**
  - Every polymorphic class has a virtual table (`vtable`).
  - The first field of the class struct in LLVM is the vtable pointer: `%ClassName = type { i8**, fields... }`.
  - Overridden methods replace their corresponding slots in the class vtable.
  - Virtual calls are emitted as:
    1. Load `vptr` from offset 0 (`i8**`).
    2. GEP to slot index $i$.
    3. Load function pointer, bitcast, and invoke.

### 5.2 Interfaces
- Interfaces define pure method contracts:
  ```gflat
  interface IShape
  {
      int Area();
      int Perimeter();
  }
  ```
- Both `struct` and `class` can implement interfaces.
- Interface dispatch uses fat pointers / interface vtables.

---

## 6. Compile-Time Evaluation (`const` System)

`gflat` features an interpreter inside the compiler (`ConstEvaluator.cs`) capable of evaluating expressions and functions at compile-time:

1. **Const Expressions:**
   - Literals, enum values, `sizeof`, `nameof`, constant arithmetic, string lengths.
2. **Const Functions:**
   - Functions marked `const` can be executed during compilation:
     ```gflat
     const int Factorial(int n)
     {
         if (n <= 1) return 1;
         return n * Factorial(n - 1);
     }
     int[Factorial(4)] arr; // 24-element array allocated at compile-time!
     ```
3. **Safety Limits:**
   - Recursion depth limit: 1024 calls.
   - Step limit: 1,000,000 operations to guarantee the compiler never hangs in infinite loops.

---

## 7. Error Handling Architecture

`gflat` implements structured exception handling with `try`, `catch`, and `throw`:

```gflat
try
{
    if (failed) throw new Exception("Operation failed");
}
catch (Exception* e)
{
    printf("Caught: %s\n", e.Message);
}
```

### Underlying Implementation: Checked Return Value Unwinding
Unlike C++ or C# which use zero-cost DWARF table unwinding or Windows SEH (`__CxxFrameHandler3`), `gflat` lowers throwing functions in LLVM to **value-tuple status returns**:
- A function returning `T` that can throw is transformed into returning:
  `{ T, %Exception*, i1 }` (where `i1` is `true` if an exception is in flight).
- Every call site inspects the status bit:
  - If `true`, the function runs all pending `defer` actions and scope destructors, then bubbles the exception up the call stack to the nearest enclosing `catch` block.

---

## 8. Core Guarantees: Reality Audit & Status

This section evaluates the promises of `gflat` against the actual current compiler implementation.

---

### Guarantee 1: C++ ABI Compatibility
> **The Goal:** Can a `gflat` library link directly with C++ code and pass structs/classes across the binary boundary without a C wrapper?

#### Reality Audit:
| Aspect | Compatibility Status | Technical Reality |
| :--- | :--- | :--- |
| **Struct Field Layout** | **PARTIAL** | Struct fields are emitted in declaration order, matching standard C layout (`#pragma pack(8)`). However, `gflat` does not currently support explicit alignment/packing attributes (`__attribute__((aligned))` or `#pragma pack`). |
| **Polymorphic Class Layout** | **INCOMPATIBLE** | In gflat, a class layout is `{ i8**, base_fields..., derived_fields... }`. The vtable pointer is at offset 0, which superficially resembles C++. **However**, native C++ (Itanium ABI) expects metadata at negative offsets from the vtable: `vtable[-1]` (RTTI pointer) and `vtable[-2]` (offset-to-top). gflat emits raw function pointers starting at index 0. Handing a gflat class to a C++ virtual call or `dynamic_cast` will crash. |
| **Name Mangling** | **INCOMPATIBLE** | gflat uses internal mangling: `gflat${Namespace}${Type}${Method}`. It does NOT follow Itanium C++ ABI (`_ZN...`) or MSVC ABI (`?...`). C++ linkers cannot resolve gflat symbols directly. |
| **Calling Convention** | **COMPATIBLE (C-only)** | Free functions use default C calling convention (`cdecl`). |
| **Exception Handling ABI** | **INCOMPATIBLE** | C++ uses Itanium EH / Windows SEH unwinding tables. gflat uses value-tuple error returns. A throwing gflat function cannot be called by C++ expecting a standard ABI return. |

#### Verdict on C++ ABI:
> **C++ binary ABI compatibility does NOT currently hold up.**  
> Interoperability with C++ currently requires `extern "C"` functions with C-compatible data types (pointers, primitives, and flat structs). True C++ class interoperability would require implementing Itanium/MSVC name mangling and vtable header offsets.

---

### Guarantee 2: Integer Sizing & Data Models
> **The Goal:** Prevent cross-platform integer width bugs.

#### Reality Audit:
- C/C++ uses the LLP64 model on Windows (`long` = 32 bits, `long long` = 64 bits) and LP64 on Linux/macOS (`long` = 64 bits).
- In `gflat`:
  - `int` is strictly `i32` (32 bits).
  - `long` is strictly `i64` (64 bits).
  - Explicit pointer-width integers `isize` / `usize` map to target pointers.

#### Verdict on Integer Sizing:
> **HOLDING UP COMPLETELY.** This is one of gflat's strongest design guarantees. Bitwidths are uniform regardless of the compilation host or target OS.

---

### Guarantee 3: Non-Nullable Pointer Safety
> **The Goal:** Non-nullable pointers `T*` can never be null.

#### Reality Audit:
1. **What holds up:**
   - `T*` cannot be assigned literal `null`.
   - `T*` cannot be assigned `default` or `default(T*)`.
   - Functions returning `T*` cannot return `default` or `null`.
   - `readonly char*` prevents string literal mutation.
2. **Where safety escapes exist:**
   - **Pointer Arithmetic:** `p + 5` or `p - 1` can produce invalid or null addresses without compiler errors.
   - **Uninitialized Pointers:** Declaring a pointer without an initializer (`int* p;`) leaves an uninitialized LLVM register/memory slot rather than producing a compilation error.

#### Verdict on Pointer Safety:
> **PARTIALLY HOLDING UP.** Definite assignment analysis is needed to prevent uninitialized pointer variables (`int* p;`). Once definite assignment is implemented, `T*` will be sound.

---

### Guarantee 4: Deterministic Destruction (RAII)
> **The Goal:** Stack resources are cleaned up deterministically upon scope exit, returns, or errors.

#### Reality Audit:
- Stack structs with `~StructName()` run destructors via `defer` scopes.
- Stack classes run destructors via `ClassDestructorDeferAction` and check `icmp ne vtable, null` to prevent executing destructors on uninitialized memory.
- Early `return`, `break`, `continue`, and exception unwinding properly execute defer actions in reverse declaration order.

#### Verdict on RAII:
> **HOLDING UP FOR STACK ALLOCATIONS.** Heap allocations (`new Class()`) currently do not have automatic garbage collection or reference counting and require manual management or a future memory manager.

---

## 9. Architectural Pain Points & Roadmap

1. **`TypeChecker.cs` Monolith (5,500+ lines):**
   - The type checker currently handles symbol table generation, alias resolution, type inference, monomorphization, operator resolution, and AST mutation in a single pass.
   - *Roadmap:* Separate into distinct compiler passes:
     - `SymbolCollectionPass` (namespaces, types, methods)
     - `TypeResolutionPass` (aliases, type references)
     - `TypeCheckPass` (expression typing and statement validation)
2. **Attributes:**
   - Attributes are currently strings in the AST (`AttributeNode`). 
   - *Roadmap:* Upgrade to constant expression arguments when a formal compile-time attribute or macro system is built.
3. **Definite Assignment Analysis:**
   - Ensure all local variables (especially non-nullable pointers `T*`) are assigned before use.
4. **C++ ABI Interop Bridge:**
   - If true C++ interoperability is desired, add an `[abi("c++")]` attribute that enables Itanium/MSVC vtable prefixes and symbol mangling for designated types.
