# Third-party components

The command-line compiler and language server distribute **Microsoft.Build.Locator**
under the MIT license. Its copyright and license are in
[LICENSES/MSBuild.Locator.txt](LICENSES/MSBuild.Locator.txt).

The Visual Studio extension adapts Roslyn's `HACK_ThemeColorFixer.cs` to keep
tooltip syntax colors in sync with the editor theme. Roslyn's MIT copyright and
license are in [LICENSES/Roslyn.txt](LICENSES/Roslyn.txt).

The Visual Studio extension also bundles the .NET runtime. Its package includes
the runtime's `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` under `Licenses/dotnet`.
Visual Studio SDK assemblies are supplied by Visual Studio, not redistributed by
this extension. Compiler bundles require an installed .NET 10 SDK and do not
bundle the runtime.

Clang and native platform toolchains are installed separately. Test dependencies
are restored from NuGet and are not included in the downloadable compiler bundles.
