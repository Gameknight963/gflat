# CMake consumers

Build a static or dynamic library first. Its `.cmake` companion creates an
imported target containing the binary, generated header directory, and native
link dependencies. It does not build the source project from CMake.

For a library named `answer.lib`, `answer.a`, `answer.dll`, or `answer.so`, the
target is `Gflat::answer`. Other filenames use their stem, replacing characters
outside letters, digits, `_`, `.`, `+`, and `-` with `_`.

```cmake
cmake_minimum_required(VERSION 3.15)
project(Consumer LANGUAGES CXX)

# Select the artifact for this platform and build configuration.
set(ANSWER_LIBRARY "/path/to/answer.lib")
include("${ANSWER_LIBRARY}.cmake")

add_executable(consumer main.cpp)
target_link_libraries(consumer PRIVATE Gflat::answer)

if(MSVC OR CMAKE_CXX_SIMULATE_ID STREQUAL "MSVC")
  get_target_property(runtime Gflat::answer GFLAT_RUNTIME_LIBRARY)
  set_property(TARGET consumer PROPERTY MSVC_RUNTIME_LIBRARY "${runtime}")
endif()

gflat_deploy_runtime(consumer Gflat::answer)
```

Include the corresponding generated header in `main.cpp`, such as
`answer.lib.hpp`. Calls use the public functions and namespaces from that header.
Reference parameters use C++ lvalue references.

## Binary and runtime compatibility

The import selects one built artifact. Choose a matching target architecture,
native ABI, and configuration. Windows consumers use the MSVC-compatible ABI.
The runtime property in the example matches the consumer's C runtime to the
library's `RuntimeLibrary` setting. Dependencies with conflicting runtime
settings produce a CMake compatibility error.

`gflat_deploy_runtime` copies required shared libraries beside the consumer
after building it. Static libraries can also require this when they depend on
a shared library. The helper does nothing when no runtime files are needed.

Keep the binary, its import library on Windows, generated header, and `.cmake`
file together when relocating them. Native file dependencies and search
directories retain their paths relative to that directory. Preserve that layout
when distributing dependencies. Runtime shared libraries use the copies placed
beside the built library.

Import each target name from one artifact per CMake configuration. Repeatedly
including its import file reuses the existing target.
