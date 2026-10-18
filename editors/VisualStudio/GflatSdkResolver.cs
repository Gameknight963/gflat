using System;
using System.IO;
using System.Reflection;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using MonoMod.RuntimeDetour;

namespace gflat.VisualStudio;

// The VS host cannot discover SDKs beside an extension. Intercept only our SDK;
// every other request keeps Visual Studio's normal resolver behavior.
internal sealed class GflatSdkResolver : IDisposable
{
    private delegate SdkResult Resolve(SdkResolver self, SdkReference sdk, SdkResolverContext context, SdkResultFactory factory);
    private delegate SdkResult ResolveHook(Resolve original, SdkResolver self, SdkReference sdk, SdkResolverContext context, SdkResultFactory factory);

    private readonly string sdkDirectory;
    private readonly Hook hook;

    internal GflatSdkResolver(string sdkDirectory)
    {
        this.sdkDirectory = Path.GetFullPath(sdkDirectory);
        foreach (string file in new[] { "Sdk.props", "Sdk.targets" })
            if (!File.Exists(Path.Combine(this.sdkDirectory, file)))
                throw new FileNotFoundException("The gflat extension's project SDK is incomplete. Reinstall the extension.", Path.Combine(this.sdkDirectory, file));

        // DefaultSdkResolver is internal. Check its exact public override before
        // detouring it, so an incompatible host fails at setup rather than a call.
        Type resolver = typeof(ProjectCollection).Assembly.GetType("Microsoft.Build.BackEnd.SdkResolution.DefaultSdkResolver", throwOnError: true)!;
        MethodInfo? resolve = resolver.GetMethod("Resolve", new[] { typeof(SdkReference), typeof(SdkResolverContext), typeof(SdkResultFactory) });
        if (resolve == null || resolve.ReturnType != typeof(SdkResult) || resolve.IsStatic)
            throw new NotSupportedException("This Visual Studio MSBuild version is incompatible with the gflat SDK resolver.");
        hook = new Hook(resolve, (ResolveHook)Intercept);
    }

    private SdkResult Intercept(Resolve original, SdkResolver self, SdkReference sdk, SdkResolverContext context, SdkResultFactory factory)
    {
        if (!string.Equals(sdk.Name, "Gflat.Sdk", StringComparison.OrdinalIgnoreCase))
            return original(self, sdk, context, factory);
        // The bundled SDK has no independent version selection. Never silently
        // substitute it for an explicitly requested version.
        if (!string.IsNullOrEmpty(sdk.Version) || !string.IsNullOrEmpty(sdk.MinimumVersion))
            return factory.IndicateFailure(new[] { "The bundled Gflat.Sdk does not support explicit SDK versions." });
        return factory.IndicateSuccess(sdkDirectory, string.Empty);
    }

    public void Dispose() => hook.Dispose();
}
