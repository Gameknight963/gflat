using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace gflat.VisualStudio;

[Export(typeof(IWpfTextViewCreationListener))]
[ContentType("gflat")]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class GflatLibraryProtection : IWpfTextViewCreationListener
{
    [Import] internal ITextDocumentFactoryService Documents = null!;

    public void TextViewCreated(IWpfTextView textView)
    {
        ITextBuffer buffer = textView.TextBuffer;
        if (!Documents.TryGetTextDocument(buffer, out var document) ||
            !PackagedLibraryPaths.Contains(Path.GetDirectoryName(typeof(GflatLibraryProtection).Assembly.Location)!, document.FilePath)) return;
        buffer.Properties.GetOrCreateSingletonProperty(typeof(GflatLibraryProtection), () => Protect(buffer));
    }

    private static IReadOnlyRegion Protect(ITextBuffer buffer)
    {
        using (var edit = buffer.CreateReadOnlyRegionEdit())
        {
            var region = edit.CreateReadOnlyRegion(new Span(0, buffer.CurrentSnapshot.Length),
                SpanTrackingMode.EdgeInclusive, EdgeInsertionMode.Deny);
            edit.Apply();
            return region;
        }
    }
}
