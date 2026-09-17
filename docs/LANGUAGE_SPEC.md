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
  `if`, `else`, `while`, `for`, `foreach`, `in`, `return`, `break`, `continue`, `defer`, `throw`, `throws`, `try`, `catch`, `const`, `readonly`, `static`, `virtual`, `override`, `abstract`, `public`, `private`, `protected`, `internal`, `struct`, `class`, `interface`, `enum`, `alias`, `namespace`, `using`, `extern`, `new`, `delete`, `sizeof`, `nameof`, `default`, `operator`.

### 2.2 Comments
- Line comments: `// ...`
- Block comments: `/* ... */` (can span multiple lines).

### 2.3 Member & Scope Resolution Operators (`::` vs `.`)
`gflat` enforces strict semantic separation between static scope access and instance member access:
- **Scope Resolution (`::`):** Exclusively used for static members, namespace qualifications, nested types, and enum members:
  - Namespaces: `Math::PI`, `System::IO::File`
  - Nested types & static members: `Outer::Inner`, `Math::Sin(x)`
  - Enum members: `Color::Red`, `Option::None`
  - Attempting to access static or namespace members using `.` produces a compile error.
- **Instance Member Access (`.`):** Exclusively used on instance expressions:
  - Struct and class fields: `person.name`
  - Instance methods: `list.get(0)`
  - Direct pointer member access: `ptr.x` (transparent dereference)
  - Attempting to access instance members using `::` produces a compile error.
- **Removed Operators:** The C-style `->` operator has been completely removed from the language in favor of uniform `.`.

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
| `nint` | `i64` (on x64) | Pointer | Signed | Native pointer-sized integer |
| `nuint` | `i64` (on x64) | Pointer | Unsigned | Native pointer-sized unsigned integer |
| `float` | `float` | 32 | IEEE 754 | 32-bit single-precision float |
| `double` | `double` | 64 | IEEE 754 | 64-bit double-precision float |

#### Architectural Guarantee: Fixed Integer Sizing
Unlike C and C++ where `long` is 32 bits on 64-bit Windows (LLP64) and 64 bits on 64-bit Linux (LP64), `gflat` enforces **strictly fixed sizes**:
- `int` is ALWAYS 32 bits.
- `long` is ALWAYS 64 bits.
- Pointer-sized integers are explicitly represented by `nint` and `nuint`.

**Status:** **HOLDING UP.** The type checker and LLVM emitter map `int` -> `i32` and `long` -> `i64` uniformly on all platforms.

---

### 3.2 Pointer & Reference Types

`gflat` distinguishes raw memory pointers from managed references and pointer nullability:

1. **Non-Nullable Raw Pointer (`T*`):**
   - Unmanaged memory address.
   - **Guaranteed non-null.** Cannot be assigned `null`, cannot be assigned `default`, and cannot be initialized without a valid address or allocation.
   - **Definite Assignment:** Local pointer declarations without an initializer (`T* p;`) cannot be read or dereferenced before being explicitly assigned or initialized via address-of (`&x`).
2. **Nullable Raw Pointer (`T*?`):**
   - Memory address that may be `null`.
   - Can receive `null` or `default(T*?)`.
3. **Read-Only Pointers (`readonly T*`):**
   - The memory pointed to is immutable through this pointer.
   - Prevents mutation of underlying memory.
   - String literals `"..."` decay to `readonly char*`.
4. **Managed References (`T^`):**
   - Syntax reserved for future garbage-collected runtime references.
   - Currently rejected at compile-time with a descriptive diagnostic pending GC runtime engine integration.
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

- **Semantics:** Value type. Stored inline in stack frames, containing structs, or arrays.
- **Copy Semantics & Double-Free Protection:**
  - **Plain Structs (No Destructor):** Copied bitwise on assignment (`b = a;`) and passed by value.
  - **Types with Destructors (Non-Copyable):** Structs and stack classes defining a destructor (`~Type()`) or containing fields that define destructors cannot be copied by value:
    - Assignment (`b = a;`) is a compile-time error.
    - Copy initialization (`Type b = a;`) from an existing variable, field, dereference, or index is a compile-time error.
    - Pass-by-value (`void foo(Type t)`) is a compile-time error. Must pass by pointer (`Type*` or `readonly Type*`).
    - Foreach iteration by value (`foreach (Type t in arr)`) is a compile-time error. Must iterate by pointer or index.
    - Return-by-value (`Type create()`) is allowed and transfers ownership, suppressing the local variable's destructor upon function return.
    - Explicit cloning (e.g. `Type b = a.clone();`) can be provided as a method on the type.
- **Methods:** Can define constructors, destructors, instance methods, and static methods.
- **Destructors (`~StructName()`):**
  - Invoked automatically when a local struct variable leaves its enclosing block scope.
  - Chained in reverse order of declaration (LIFO).
  - Executed on normal block exit, early `return`, `break`, `continue`, and exception unwinding.

---

### 4.2 Reference / Class Types (`class`)

- **Semantics:** Reference type containing a virtual method table pointer (`vptr`) at offset 0.
- **Storage Locations:**
  1. **Stack Classes:** Allocated directly on the stack without `new*`:
     ```gflat
     MyClass c(10, 20); // Stack-allocated instance of MyClass
     ```
     - Destructor is automatically queued on the scope's defer stack.
     - Destructors are null-safe and idempotent (clears the vtable slot upon execution).
     - Non-copyable by value if a destructor is present.
  2. **Heap Classes & Objects:** Allocated via `new* MyClass(...)`:
     - Returns a pointer `%MyClass*`.
     - Dynamically allocated via `malloc`.
  3. **Explicit Deletion (`delete` Statement):**
     - Heap-allocated objects and structs are freed using `delete ptr;`.
     - `delete` invokes the target type's destructor (`~MyClass()`, virtual if applicable), followed by releasing the underlying memory via `free()`.
     - Raw memory buffers allocated directly with `malloc()` use standard `free(ptr)`.

---

### 4.3 Deterministic Cleanup: `defer` & Foreach RAII

- **`defer` Statement:** Executes arbitrary statements or blocks when the enclosing lexical scope exits:
  ```gflat
  FILE* f = fopen("data.txt", "r");
  defer fclose(f);
  ```
- **Foreach Loop RAII:**
  - `foreach (item in collection)` calls `.GetEnumerator()`.
  - gflat adheres to zero-allocation value-type cursor iteration.
  - The compiler does NOT require or inject an implicit `Dispose()` call. If the enumerator defines a destructor (`~Enumerator()`), gflat's standard scope RAII executes it deterministically when the loop exits or breaks.

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

## 7. Error Handling & Control Flow Architecture

### 7.1 Explicit `throws` Annotation & Checked Exceptions
In `gflat`, throwing an exception is part of the function signature contract:
```gflat
int readFile(string path) throws
{
    if (path == null)
    {
        throw new Exception("Path cannot be null");
    }
    // read file ...
    return 0;
}
```
- **Signature Enforcement:** Any function or method that contains a `throw` statement or calls another throwing function must either:
  1. Handle the exception via an enclosing `try/catch` block, or
  2. Declare `throws` in its header.
- **Predictable ABI:** The presence of `throws` on a function deterministically establishes its LLVM ABI without needing whole-program heuristic call graph inference (`PropagateCanThrow` has been eliminated).
- `main()` cannot be declared `throws`.

### 7.2 Return Path & Control Flow Analysis Guarantee
- Non-void functions and operator overloads must return a value or throw an exception along **every reachable code path**.
- The compiler's `ControlFlowPass` performs static path analysis across branches (`if/else`), loops (`while`, `for`), and error handling (`try/catch`).
- Infinite loops without unhandled `break` statements (`while (true)` or `for (;;)`) are recognized as terminating.
- Falling off the end of a non-void function is a compile-time error (`TypeCheckException`). Void functions, constructors, and destructors allow implicit return.

### 7.3 Underlying Implementation: Value-Tuple Status Returns
Unlike C++ or C# which use zero-cost DWARF table unwinding or Windows SEH (`__CxxFrameHandler3`), `gflat` lowers throwing functions in LLVM to **value-tuple status returns**:
- A function returning `T` declared `throws` is transformed in LLVM IR to return:
  `{ T, %Exception*, i1 }` (where `i1` is `true` if an exception is in flight).
- Every call site inspects the status bit:
  - If `true`, the runtime executes all pending `defer` actions and scope destructors, bubbling the exception up the call stack to the nearest enclosing `catch` block.

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

### Guarantee 3: Non-Nullable Pointer & Variable Safety
> **The Goal:** Non-nullable pointers `T*` can never be null or uninitialized, and variables cannot be read before assignment.

#### Reality Audit:
1. **What holds up:**
   - `T*` cannot be assigned literal `null`.
   - `T*` cannot be assigned `default` or `default(T*)`.
   - Functions returning `T*` cannot return `default` or `null`.
   - `readonly char*` prevents string literal mutation.
   - **Definite Assignment Analysis (`DefiniteAssignmentPass`):**
     - Declaring any local variable without an initializer (`int* p;`, `int x;`) tracks it as unassigned.
     - Reading or dereferencing unassigned variables is a compile-time error.
     - **Rule A (Address-of Initialization):** Taking the address of an uninitialized variable (`&x` or `&x.field`) initializes it, supporting idiomatic C-style out-parameters (`int x; get_val(&x);`).
     - **Field-by-Field Struct Initialization:** Assigning individual fields (`p.x = 10;`) is permitted as an initialization write. Individual field reads verify that the specific field is assigned, and reading the entire composite struct requires all declared fields to be assigned.
2. **Where safety escapes exist:**
   - **Pointer Arithmetic:** `p + 5` or `p - 1` can produce out-of-bounds addresses without runtime bounds checking (standard for systems languages).

#### Verdict on Pointer & Variable Safety:
> **HOLDING UP COMPLETELY.** Non-nullable pointers `T*` cannot be null and cannot be read uninitialized. Definite assignment ensures memory is initialized before consumption.

---

### Guarantee 4: Deterministic Destruction (RAII)
> **The Goal:** Resources are cleaned up deterministically upon scope exit, returns, deletion, or errors.

#### Reality Audit:
- Stack structs with `~StructName()` run destructors via `defer` scopes.
- Stack classes run destructors via `ClassDestructorDeferAction` and check `icmp ne vtable, null` to prevent executing destructors on uninitialized memory.
- Heap allocations (`new Class()`, `new Struct()`) are explicitly destroyed and freed via `delete ptr;`, executing destructors before releasing memory.
- Early `return`, `break`, `continue`, and exception unwinding properly execute defer actions in reverse declaration order.

#### Verdict on RAII:
> **HOLDING UP.** Stack resources use scope-based RAII, while heap objects have deterministic destructor execution via `delete`.

---

## 9. Completed Architecture Milestones & Ongoing Roadmap

### Completed Milestones:
1. **Multi-Pass Compiler Decomposition:**
   - Extracted standalone `SymbolTable` to isolate type, namespace, and member registration from checking logic.
   - Decoupled compile-time evaluation engine via `IConstEvaluationContext`.
   - Pipeline structured into distinct, composable compiler passes:
     - **Pass 1:** `SymbolCollectionPass` (namespaces, types, methods, structs, classes, interfaces, enums, aliases)
     - **Pass 2:** `HierarchyResolutionPass` (inheritance validation, vtable slot allocation, interface conformance)
     - **Pass 3:** `TypeChecker` (expression typing and statement validation)
     - **Pass 4:** `ControlFlowPass` (return path and fallthrough verification)
     - **Pass 5:** `DefiniteAssignmentPass` (definite assignment with Rule A and struct field tracking)
2. **Checked Exceptions & Explicit `throws`:**
   - Required explicit `throws` annotation on throwing functions.
   - Strictly verifiable LLVM ABI tuple `{ T, %Exception*, i1 }`.
3. **Strict Scope Resolution:**
   - Enforced `::` strictly for static/namespaces/types/enums and `.` strictly for instance members.
   - Removed C-style `->` operator.
4. **Deterministic Deletion:**
   - Added first-class `delete` statement for heap allocations with automatic destructor invocation.

### Ongoing Roadmap:
1. **Attributes System:**
   - Upgrade string attributes (`AttributeNode`) to evaluate constant expression arguments when a formal compile-time macro or reflection system is built.
2. **Custom Allocator Integration:**
   - Provide clean mechanisms for user-defined allocators to override `new` / `delete`.
3. **C++ ABI Interop Bridge:**
   - If true C++ interoperability is desired, add an `[abi("c++")]` attribute that enables Itanium/MSVC vtable prefixes and symbol mangling for designated types.
