using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace gflat.VisualStudio;

// gflat.pkgdef registers the language identity independently of the MEF content
// type used by LSP/TextMate. The SDK generates the language options registration.
[Guid("25453519-83CF-4439-B0A4-6D79CD8F267C")]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideBindingPath]
[ProvideLanguageEditorOptionPage(typeof(GflatOptions), "gflat", null, "IntelliSense", "IntelliSense")]
public sealed class GflatEditorPackage : AsyncPackage
{
    private GflatSdkResolver? sdkResolver;
    internal GflatOptions Options => (GflatOptions)GetDialogPage(typeof(GflatOptions));
    protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        sdkResolver = new GflatSdkResolver(Path.Combine(Path.GetDirectoryName(typeof(GflatEditorPackage).Assembly.Location)!, "Sdk"));
        AddService(typeof(GflatLanguageInfo), (_, __, ___) => Task.FromResult<object?>(new GflatLanguageInfo()), promote: true);
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) sdkResolver?.Dispose();
        base.Dispose(disposing);
    }
}

[Guid("54830AC1-0C42-42AC-A4D8-2D4E18A319C4")]
[ComVisible(true)]
public sealed class GflatLanguageInfo : IVsLanguageInfo
{
    public int GetLanguageName(out string name) { name = "gflat"; return VSConstants.S_OK; }
    public int GetFileExtensions(out string extensions) { extensions = ".gf"; return VSConstants.S_OK; }

    // MEF/TextMate and LSP supply these features, not a legacy language service.
    public int GetColorizer(IVsTextLines buffer, out IVsColorizer colorizer)
    { colorizer = null!; return VSConstants.E_NOTIMPL; }
    public int GetCodeWindowManager(IVsCodeWindow window, out IVsCodeWindowManager manager)
    { manager = null!; return VSConstants.E_NOTIMPL; }
}
