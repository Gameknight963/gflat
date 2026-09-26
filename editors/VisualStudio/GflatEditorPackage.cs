using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace gflat.VisualStudio;

// Registration lives in gflat.pkgdef. The shell needs a language identity for
// editor preferences independently of the MEF content type used by LSP/TextMate.
[Guid("25453519-83CF-4439-B0A4-6D79CD8F267C")]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
public sealed class GflatEditorPackage : AsyncPackage
{
    protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        AddService(typeof(GflatLanguageInfo), (_, __, ___) => Task.FromResult<object?>(new GflatLanguageInfo()), promote: true);
        return Task.CompletedTask;
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
