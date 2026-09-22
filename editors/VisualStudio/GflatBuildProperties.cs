using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;

namespace gflat.VisualStudio;

// CPS owns the hierarchy, commands, persistence and builds. This is our only
// project-system customization: locate the compiler shipped in this VSIX.
[ExportBuildGlobalPropertiesProvider(designTimeBuildProperties: false)]
[AppliesTo("Gflat")]
internal sealed class GflatBuildProperties : StaticGlobalPropertiesProviderBase
{
    [ImportingConstructor]
    // The FullBuild export belongs to the configured-project MEF scope.
    public GflatBuildProperties(ConfiguredProject project) : base(project.Services) { }

    public override Task<IImmutableDictionary<string, string>> GetGlobalPropertiesAsync(CancellationToken cancellationToken)
    {
        string compiler = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Compiler", "gflat.exe");
        return Task.FromResult<IImmutableDictionary<string, string>>(
            Empty.PropertiesMap.SetItem("GflatBundledCompiler", compiler));
    }
}
