# Third-party components

The command-line compiler and language server distribute **MSBuild** under the MIT
license. Its copyright and license are in [LICENSES/MSBuild.txt](LICENSES/MSBuild.txt).
Each tool also carries MSBuild's third-party notices under `Licenses/MSBuild`.

The Visual Studio extension adapts Roslyn's `HACK_ThemeColorFixer.cs` to keep
tooltip syntax colors in sync with the editor theme. Roslyn's MIT copyright and
license are in [LICENSES/Roslyn.txt](LICENSES/Roslyn.txt).

The compiler bundles and Visual Studio extension also bundle the .NET runtime.
Their packages include the runtime's `LICENSE.TXT` and
`THIRD-PARTY-NOTICES.TXT` under `LICENSES/dotnet` (`Licenses/dotnet` in the VSIX).
Visual Studio supplies its SDK assemblies and uses its own MSBuild for IDE projects.
The compiler and language server use their bundled engine.

Clang and native platform toolchains are installed separately. Test dependencies
are restored from NuGet and are not included in the downloadable compiler bundles.
