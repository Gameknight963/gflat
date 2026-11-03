using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using StreamJsonRpc;

namespace gflat.VisualStudio;

public sealed class GflatEditorMessages
{
    [JsonRpcMethod("gflat/showDocument")]
    public async Task ShowDocumentAsync(string uri)
    {
        var document = new Uri(uri);
        if (!document.IsFile) throw new ArgumentException("An editor document must use a file URI.");
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, document.LocalPath);
    }
}
