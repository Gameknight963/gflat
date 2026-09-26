# Contributing to gflat

gflat itself, the standard library, and compiler are still evolving, and constant breaking changes are expected.

## AI policy

AI-Assisted contributions are welcome, However, you are responsible for the code you submit (reviewing, testing, etc.)

You don't have to disclose AI usage, but if you do, it will help me better understand the context of the contribution. 

Also, if I have any questions about the change, I don't wanna hear "idk, cause claude said [this thing]."

## Discussing changes

For new language features or substantial design changes, open an issue before implementing them so we can discuss the approach.

Small bug fixes, documentation improvements, and regression tests are welcome as pull requests.

## Building and testing

See the README for prerequisites and build instructions.

Run the relevant test projects:

    dotnet test gflat.Tests/gflat.Tests.csproj
    dotnet test gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj

For Visual Studio extension changes, also build and check the package:

    ./editors/VisualStudio/build.ps1
    ./editors/VisualStudio/test-package.ps1

## Pull requests

- Keep changes focused; submit unrelated changes separately.
- Follow the surrounding code style.
- Include a regression test for compiler or editor bugs where practical.
- Update documentation when changing language behavior.
- Explain what changed, why, and how you tested it.

You don't need both Windows and Linux to contribute. Mention what you tested locally, since CI checks both platforms.

## Reporting bugs

Include a small reproducing example, expected and actual behavior, your operating system, and the compiler commit or BUILD-INFO.json.

## License

All contributions are provided under the project's MIT license.