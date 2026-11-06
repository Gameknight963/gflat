# CI test execution

Compiler tests run in four separate jobs per operating system. Each job runs its
primary cases, then an O2 fallback pass. Editor and bundled-tool tests run in a
separate job per operating system.

## Test inputs

Focused fixtures select Standard sources through `StandardTestSources`. Its
explicit source sets cover related APIs and their dependencies. Add individual
files with `Load` when a fixture needs an additional API.

Keep full-library builds in project, example, and documentation tests. Loading
every Standard file in a focused fixture repeats unrelated parsing and checking
for each test program.

## Optimization coverage

- The primary pass runs every compiler case once with default optimization O0
- Tests with an integer parameter named `optimization`, `optimizationLevel`,
  `opt`, or `optLevel` keep their explicit optimization values
- `[Trait("Optimization", "Independent")]` marks checks whose work is unchanged
  by native optimization, such as documentation tests that emit IR
- The O2 fallback reruns methods without that parameter, preserving coverage for
  older tests that use the process default
- After the primary pass, observed default-optimization compilations add their
  methods to the fallback even when they have an optimization parameter

The fallback is conservative. It includes frontend-only tests and tests that
choose fixed optimization levels inside their bodies. Timing reports show where
further separation would save work.

## Shard plans

`build/TestPlan` discovers test methods from the built assembly and keeps each
class in one shard. Initial weights estimate data rows and include the fallback
pass. Member-data theories have a minimum estimated weight of one row.
`[Trait("EstimatedCases", "50")]` supplies an estimate for a larger data provider.

Documentation examples run in four disjoint test classes so they can be assigned
to different jobs. `[Trait("SeparateShards", "Documentation")]` distributes those
classes across the available shards before reusing a shard. Every example emits
IR against the complete Standard library.

Generate plans after building the test project:

```powershell
dotnet run --project build/TestPlan -- plan gflat.Tests/bin/Debug/net10.0/gflat.Tests.dll artifacts/test-plan 4
```

Each shard has primary and fallback `.runsettings` files. Their
[`TestCaseFilter`](https://learn.microsoft.com/en-us/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file#runconfiguration-element)
avoids command-line length limits. `manifest.json` lists the assigned classes and
methods. Verify filter coverage with:

```powershell
./build/TestPlan/test.ps1
```

The check compares VSTest discovery for the complete assembly against the union
of primary shards, including theory cases. It also counts fallback cases.

## Stage profiling

Set `GFLAT_PROFILE_DIRECTORY` to collect per-process JSONL traces. Compiler tests
attach their method names to events. Processes started through `NativeToolchain`
inherit that test context. CI enables tracing through each shard's runsettings.

The stages measure:

| Stage | Work |
| --- | --- |
| `syntax.lex` | Tokenization |
| `syntax.parse` | Parsing |
| `syntax.clone` | Copying cached parser templates into independent syntax trees |
| `frontend.parse-and-prelude` | Compiler input parsing and automatic library loading |
| `frontend.check` | Symbol preparation, checking, and semantic passes |
| `check.declarations`, `check.symbols` | Declaration expansion and symbol collection |
| `check.signatures`, `check.bind-signatures`, `check.overloads` | Signature binding, specialization, and overload preparation |
| `check.hierarchy`, `check.extensions` | Inheritance, alias contracts, and extension registration |
| `check.bodies` | Declaration body checking |
| `check.attributes` | Attribute evaluation and final declaration validation |
| `check.control-flow`, `check.definite-assignment` | Return paths and variable initialization |
| `editor.workspace` | Project discovery, source loading, and editor analysis |
| `editor.workspace.read-sources` | Reading the current source text |
| `editor.snapshot` | Parsing, checking, and capturing an editor snapshot |
| `editor.syntax.capture` | Capturing source syntax before checking rewrites it |
| `editor.index` | Building editor symbols and query indexes |
| `editor.index.*` | Individual indexing phases |
| `emit.ir` | LLVM module emission, including reachable function bodies |
| `emit.reachable-functions` | Executable function reachability and deferred body emission |
| `emit.trim-module` | Removal of unreferenced generated globals and declarations |
| `native.compile-link` | Native toolchain preparation, compilation, and linking |
| `native.runtime-build` | Compilation of a runtime helper on a cache miss |
| `process.clang` | Time waiting for Clang processes, including native runtime builds |
| `process.execute` | Time waiting for other tools and generated programs |
| `test` | Test method execution, including theory cases |

For individual method-check events, also set `GFLAT_PROFILE_DETAIL=1`. These
`check.method` events identify the namespace and method in `detail`. Detailed
tracing adds overhead, so use ordinary stage tracing for build-time comparisons.
Specialization can nest method checks, so method durations can overlap.

Set `GFLAT_PROFILE_ALLOCATIONS=1` to include `allocatedBytes` in each event.
This counts managed allocations across the process during that stage, including
concurrent work. Nested allocation totals overlap. The count measures allocation
volume rather than retained memory.

Summarize traces and TRX results:

```powershell
dotnet run --project build/TestPlan -- report artifacts/test-plan
```

CI publishes the report in its job summary and uploads traces, TRX results, and
the plan as one artifact per shard. Stage totals are cumulative worker time.
Nested stages overlap, so their totals must not be added together.

## Balancing with measured durations

The report writes `class-durations.json`, mapping fully qualified class names to
cumulative milliseconds from successful TRX results. Classes
with failed or skipped cases are omitted from duration weights.

Combine artifacts from complete runs on one operating system, keeping shard
directories separate, then run `report` on the combined directory. Pass the
resulting duration file as the fifth argument to `plan`:

```powershell
dotnet run --project build/TestPlan -- plan gflat.Tests/bin/Debug/net10.0/gflat.Tests.dll artifacts/test-plan 4 artifacts/combined/class-durations.json
```

CI uses `build/test-durations/Windows.json` or `Linux.json` when present. New
classes use a median duration estimate until measurements are available.
