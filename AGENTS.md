# Working in this repository

## Writing

- Describe what something does. Avoid unsolicited reassurance and comparisons to things the reader has not asked about
- Keep limits that affect a decision or correct use: supported inputs, ownership, lifetime, data loss, dependencies, and error behavior
- Put the main point first. Use plain, natural language and concrete examples
- Break unrelated details into sections or categorized bullets. Short bullet items usually need no final period
- Avoid slogans, punchy fragments, and prose semicolons. Code punctuation is unaffected
- READMEs introduce the project, show useful snippets, and explain setup. Put API contracts and implementation details in linked documentation
- Put instructions aimed at agents here. Review notes, plans, and implementation checkpoints belong under ignored `artifacts/` or `.local-notes/`
- Preserve executable documentation examples when editing prose. If an example changes, run its documentation tests

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
- VSIX package validation uses Windows PowerShell and the installed Visual Studio SDK. Installing a real extension, registering an SDK, or changing security settings is a separate action
