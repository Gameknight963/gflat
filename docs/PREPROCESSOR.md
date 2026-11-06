# Macros and includes

The preprocessor runs before parsing each root `.gf` file. Definitions apply from
where they appear until `#undef` or the end of that root input, including its
included files. Namespaces do not scope macros.

## Share definitions through a header

`features.gfh`:

```cpp
#pragma once

#define VERSION 12
#define STRINGIFY(value) #value
#define JOIN(left, right) left##right
```

`main.gf`:

```cpp
#include "features.gfh"

extern "C" int printf(readonly(char)* format, ...);

int main()
{
    int JOIN(answer_, VERSION) = 42;
    printf("%s = %s, answer = %d\n", STRINGIFY(@VERSION), STRINGIFY(VERSION), answer_12);
    return 0;
}
```

This prints `VERSION = 12, answer = 42`. Run the
[example project](../examples/macros/macros.gfproj) with:

```console
gflat build examples/macros/macros.gfproj --run
```

- `#include "file.gfh"` searches beside the including file, then the configured include directories
- `#include <file.gfh>` searches the configured include directories
- An include operand can be a macro that expands to either form
- `#pragma once` includes a path once per root input. Traditional `#ifndef` guards work too

Includes insert preprocessing tokens at their location. They can contain whole
declarations or fragments of code. `.gfh` is the editor-associated header
extension. Default project globs compile `.gf` files as roots.

Each root input starts with fresh macro and include-guard state. Included language
declarations follow the compiler's ordinary duplicate-definition rules. Use source
libraries to share language definitions across roots.

### Include members inside a type

A header can use `[:typeof(*this):]` to name the type receiving its declarations:

```cpp
struct Point
{
    public int X;
    public int Y;

    #include "equality.gfh"
}
```

For example, `equality.gfh` can define `Equals` with a parameter of that type and
compare its fields using `static foreach`. Such a header allows repeated
inclusion so each receiving type gets its own members. See
[reusable members with includes](../examples/member-includes/README.md).

## Define and expand macros

```cpp
#define LIMIT 64
#define DOUBLE(value) ((value) + (value))
#define LOG(format, ...) Print(format __VA_OPT__(,) __VA_ARGS__)
```

A function-like definition has no whitespace between its name and `(`. Calls may
contain whitespace there. Arguments split at commas outside parentheses.
`...` supplies `__VA_ARGS__`, and `__VA_OPT__(tokens)` supplies tokens when the
expanded variadic argument is nonempty.

- A backslash immediately followed by a newline continues the logical line
- `#undef NAME` removes a definition
- An identical redefinition is accepted. Use `#undef` before changing a definition
- Replacements are rescanned, with recursive expansion of the same macro suppressed
- Ordinary string and character literal contents stay literal. Interpolation expressions expand as code

### Stringification and concatenation

Arguments expand before substitution, including before `#` and `##`:

```cpp
#define VERSION 12
#define STRINGIFY(value) #value
#define JOIN(left, right) left##right

STRINGIFY(VERSION)     // "12"
JOIN(item_, VERSION)   // item_12
```

`#` produces a string literal from the expanded argument's token spellings, with
whitespace normalized between tokens. `##` joins its adjacent tokens. The result
must be one preprocessing token, and it is rescanned for macros.

### Escape the current scan

`@NAME` and `@(tokens)` pass their contents through the current expansion scan.
The `@` and grouping parentheses are removed. A later rescan expands those tokens
normally:

```cpp
#define VERSION 12
#define STRINGIFY(value) #value
#define FORWARD(value) STRINGIFY(value)

STRINGIFY(@VERSION)        // "VERSION"
STRINGIFY(@(VERSION > 0))  // "VERSION > 0"
FORWARD(@VERSION)          // "12"
```

`@NAME` also permits keyword identifiers, such as `extern "C" int @class();`. The
identifier's name is `class`, including in native symbols, `nameof`, and
reflection. `@(tokens)` preserves the tokens' usual keyword classification.

## Conditional compilation

```cpp
#if defined(ENABLE_TRACE) && ENABLE_TRACE
#define TRACE(value) Print(value)
#else
#define TRACE(value)
#endif
```

Supported directives:

- `#if`, `#ifdef`, `#ifndef`
- `#elif`, `#elifdef`, `#elifndef`, `#else`, `#endif`
- `#error message` and `#warning message`
- `#pragma once`

`#if` uses 64-bit signed and unsigned integer arithmetic, character constants,
`defined`, the arithmetic and bitwise operators, comparisons, logical operators,
and `?:`. Logical operators and `?:` short-circuit. Undefined identifiers evaluate
to zero. `true` evaluates to one.

`__FILE__` expands to the source path string, and `__LINE__` to the physical line
number. Unknown directives are errors. Unknown pragmas produce warnings.

## Configure a build

```console
gflat main.gf -I include -D ENABLE_TRACE=1
gflat build app.gfproj -D ENABLE_TRACE=1
```

`-Ipath` and `-DNAME=value` also work. The long forms are
`--include-directory path` and `--define NAME=value`. A definition without `=value`
expands to `1`.

Project settings:

```xml
<ItemGroup>
  <IncludeDirectory Include="include" />
  <Define Include="ENABLE_TRACE=1" />
</ItemGroup>
```

Include directories are relative to the declaring project. Search order is the
root project's directories followed by referenced projects' directories, then CLI
additions. Defines on the root project configure the whole compilation. CLI
definitions override matching project definitions. Referenced projects expose
include directories, and keep their `Define` items local to their own builds.

Incremental builds track transitive headers and earlier unsuccessful search paths.
Creating a header that shadows a later search result triggers rebuilding.
The editor reads unsaved header text and provides macro hovers and go to definition.
