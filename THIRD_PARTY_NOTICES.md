# Third-party components

The command-line compiler and language server distribute **MSBuild** under the MIT
license. Its copyright and license are in [LICENSES/MSBuild.txt](LICENSES/MSBuild.txt).
Each tool also carries MSBuild's third-party notices under `Licenses/MSBuild`.

The Visual Studio extension adapts Roslyn's `HACK_ThemeColorFixer.cs` to keep
tooltip syntax colors in sync with the editor theme. Roslyn's MIT copyright and
license are in [LICENSES/Roslyn.txt](LICENSES/Roslyn.txt).

The Visual Studio extension uses MonoMod.RuntimeDetour to resolve its bundled
project SDK inside Visual Studio's MSBuild. MonoMod, Mono.Cecil, and the Iced
disassembler it bundles are covered by the notices in
[LICENSES/MonoMod.txt](LICENSES/MonoMod.txt),
[LICENSES/Mono.Cecil.txt](LICENSES/Mono.Cecil.txt), and
[LICENSES/Iced.txt](LICENSES/Iced.txt), also included in the VSIX.

The compiler bundles and Visual Studio extension also bundle the .NET runtime.
Their packages include the runtime's `LICENSE.TXT` and
`THIRD-PARTY-NOTICES.TXT` under `LICENSES/dotnet` (`Licenses/dotnet` in the VSIX).
Visual Studio SDK assemblies are supplied by Visual Studio, not redistributed by
this extension. Visual Studio continues to use its own MSBuild for IDE projects;
the compiler and language server use their bundled engine.

Clang and native platform toolchains are installed separately. Test dependencies
are restored from NuGet and are not included in the downloadable compiler bundles.
