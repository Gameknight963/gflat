# Contributing to gflat

gflat is experimental. Contributions may require changes as the language evolves.

## AI policy

AI-assisted contributions are welcome. Review and test the code you submit.

Mentioning AI usage is optional and can help explain the context of a contribution.

Be ready to explain how your change works and why you chose it.

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

Windows CI puts generated test programs under `$RUNNER_TEMP/gflat-tests` and
excludes only that directory from Defender scanning on the disposable runner.
Test failures still fail the job. This does not change local Defender settings.
For the same setup locally, run `./build/setup-local-tests.ps1` once and accept
the Windows elevation prompt. It excludes only `%LOCALAPPDATA%/gflat/TestOutputs`
and saves `GFLAT_TEST_OUTPUT_ROOT` for your user. Compiler tests use that setting
automatically. Reopen existing shells before running package smoke tests.
You can also set `GFLAT_TEST_OUTPUT_ROOT` explicitly for a particular process.

## Pull requests

- Submit unrelated changes separately
- Follow the surrounding code style and [writing guidelines](AGENTS.md#writing)
- Include a regression test for compiler or editor bugs where practical
- Update documentation when changing language behavior
- Explain what changed, why, and how you tested it

You don't need both Windows and Linux to contribute. Mention what you tested locally, since CI checks both platforms.

## Reporting bugs

Include a small reproducing example, expected and actual behavior, your operating system, and the compiler commit or BUILD-INFO.json.

## License

All contributions are provided under the project's MIT license.
