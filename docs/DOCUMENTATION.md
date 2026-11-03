# Declaration documentation

Write consecutive `///` lines before a declaration and its attributes.

```gflat
/// @brief Returns the supplied value.
///
/// @details Use this function when an identity operation is needed.
/// @param value The value to return
/// @return The supplied value
int Identity(int value) => value;
```

The editor presents each part where it is useful:

- Completion and hovers show the brief description
- Signature help shows the brief description and parameter documentation
- **View documentation** opens the full comment in a separate document
- **Generate documentation manual** writes a static HTML manual and a JSON sidecar

Without `@brief`, the first paragraph becomes the brief description. A blank
`///` line ends that paragraph. A physical blank line or ordinary comment between
the comment and declaration detaches the documentation.

## Commands

| Command | Meaning |
| --- | --- |
| `@brief description` | Summary for completion and hovers |
| `@details description` | Detailed description for the full view and manual |
| `@param name description` | Parameter description |
| `@tparam name description` | Generic parameter description |
| `@return description` | Return value description |
| `@throws Type description` | Exception description |
| `@ref Symbol` | Resolved symbol reference |
| `@c word` | Inline code, with keyword coloring where applicable |
| `@code` and `@endcode` | Literal code example |

Commands also accept Doxygen's backslash spelling. `@returns`, `@throw`, and
`@exception` are supported aliases. This is a subset of Doxygen commands.
Prose is plaintext. Markdown punctuation and XML tags are displayed literally.

Descriptions can continue onto following documentation lines. A new section
command starts a new description. Code examples retain their line breaks and
contents.

## References and inheritance

`@ref Item` resolves a symbol in the documented declaration's scope. Qualified
names such as `@ref Library::Item` work without an import. Specify an overload
with its parameter types, for example `@ref Read(int)`.

References support hover, Go to Definition, Find All References, and rename.
Parameter names in `@param` and generic parameter names in `@tparam` also refer
to their declarations. `@c` formats code without linking it to a symbol.

Generic specializations use their source declaration's documentation. An
undocumented override or interface implementation inherits documentation from
its base member or interface. Parameter descriptions follow parameter position
when the implementation uses different names. Aliases use their own documentation.

The editor reports documentation warnings as `GFLS0003`. These include unknown
commands, unresolved or ambiguous references, incorrect parameter names, and
unclosed code examples.

## Generated documentation

Use **Generate documentation manual** from the editor's source actions. The
command reports the output directory, containing:

- `index.html`: a standalone manual with summaries, details, parameter and return
  descriptions, exceptions, examples, and links between documented symbols
- `documentation.json`: versioned structured documentation with declaration
  identifiers and source locations

The editor command uses a temporary directory by default. LSP clients can call
`workspace/executeCommand` with command `gflat.generateDocumentation` and arguments
`[documentUri, outputDirectory]` to select a directory. Generation replaces
`index.html` and `documentation.json` in that directory.

The manual covers documented declarations in the project's selected source files
and included headers. It preserves plain prose and escapes HTML characters.
