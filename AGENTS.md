# Working in this repository

## Writing

- Describe what something does. Avoid unsolicited reassurance and comparisons to things the reader has not asked about
- Keep limits that affect a decision or correct use: supported inputs, ownership, lifetime, data loss, dependencies, and error behavior
- Put the main point first. Use plain, natural language and concrete examples
- Organize API details around the reader's task. Use parameter, result, and error descriptions where they help, rather than stacking unrelated contract sentences
- Omit routine guarantees that follow from the signature or ordinary pointer/count semantics. Explain units, unusual empty-input behavior, result sentinels, ownership decisions, and observable failure effects where they matter
- Break unrelated details into sections or categorized bullets. Short bullet items usually need no final period
- Avoid slogans, punchy fragments, and prose semicolons. Code punctuation is unaffected
- READMEs introduce the project, show useful snippets, and explain setup. Put API contracts and implementation details in linked documentation
- Put instructions aimed at agents here. Review notes, plans, and implementation checkpoints belong under ignored `artifacts/` or `.local-notes/`
- Preserve executable documentation examples when editing prose. If an example changes, run its documentation tests
- Put each `using` directive on its own line in source and examples. Leave a blank line after the last directive before declarations, namespaces, or comments
- In examples, use names made visible by imports. Keep full qualification when it resolves ambiguity or demonstrates qualified lookup
- Use `readonly(T)*` for a pointer to readonly data, including `readonly(char)*` for C strings. `readonly T*` protects the pointer itself
- Use ordinary `//` comments in Standard, shipped libraries, and examples. The plaintext `///` mechanism is deferred for a structured redesign. Compiler/editor tests can still exercise the existing mechanism

## Files and namespaces

- Directories follow public namespaces within each source root. `Standard/core` and `Standard/libc` describe dependencies and can contribute to the same namespace
- Type files use PascalCase and match their main type, such as `HashSet.gf`
- Freestanding function and extension files use snake_case names describing their subject, such as `integer_parsing.gf` or `enumerable_select.gf`
- Keep nested types with their owner. Small supporting declarations can share the owner's file
- Group tests by subject. Avoid catch-all `Helpers` and `Utilities` files
- Preserve public namespaces during housekeeping unless the task calls for an API change

## Scope and verification

- Keep unrelated changes in separate commits. Check whether a commit reached the remote before amending it
- Run checks appropriate to the change. Documentation examples are covered by `DocumentationAndDriverTests` and `StandardLibraryGuideTests`
- Read build and test results before claiming success. Distinguish focused checks from a full suite
- Avoid running concurrent build or native-test processes against the same outputs
- Focused compiler and Standard tests select their required library sources explicitly. Reserve full Standard directory loading for integration and documentation coverage
- VSIX package validation uses Windows PowerShell and the installed Visual Studio SDK. Installing a real extension, registering an SDK, or changing security settings is a separate action
