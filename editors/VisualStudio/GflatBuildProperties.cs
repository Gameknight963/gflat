using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using gflat.Projects;

namespace gflat.VisualStudio;

// CPS owns the hierarchy, commands, persistence and builds. This is our only
// project-system customization: locate the compiler and SDK shipped in this VSIX.
// Default export participates in evaluation, design-time builds and full builds.
[Export(typeof(IProjectGlobalPropertiesProvider))]
[AppliesTo("Gflat")]
internal sealed class GflatBuildProperties : StaticGlobalPropertiesProviderBase
{
    [ImportingConstructor]
    public GflatBuildProperties(IProjectCommonServices services) : base(services) { }

    public override Task<IImmutableDictionary<string, string>> GetGlobalPropertiesAsync(CancellationToken cancellationToken)
    {
        string extension = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string compiler = Path.Combine(extension, "Compiler", "gflat.exe");
        string sdk = ProjectSdk.FindRoot(extension, Environment.GetEnvironmentVariable("GflatSdkRoot")) ?? throw new FileNotFoundException("The gflat SDK is missing. Reinstall the gflat Visual Studio extension.");
        return Task.FromResult<IImmutableDictionary<string, string>>(
            Empty.PropertiesMap.SetItem("GflatBundledCompiler", ProjectSdk.EscapePropertyValue(compiler))
                .SetItem("GflatCompilerPath", ProjectSdk.EscapePropertyValue(compiler))
                .SetItem("GflatSdkRoot", ProjectSdk.EscapePropertyValue(sdk)));
    }
}
