using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace gflat.VisualStudio;

[Export(typeof(IAsyncQuickInfoSourceProvider)), Name("gflat navigable hover"), ContentType("gflat")]
public sealed class GflatQuickInfoProvider : IAsyncQuickInfoSourceProvider
{
    private readonly ITextDocumentFactoryService documents;
    private readonly HoverConnection connection;
    [ImportingConstructor]
    public GflatQuickInfoProvider(ITextDocumentFactoryService documents, HoverConnection connection)
    { this.documents = documents; this.connection = connection; }

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer buffer) => new Source(buffer, documents, connection);

    private sealed class Source : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer buffer;
        private readonly ITextDocumentFactoryService documents;
        private readonly HoverConnection connection;
        private volatile bool disposed;
        public Source(ITextBuffer buffer, ITextDocumentFactoryService documents, HoverConnection connection)
        { this.buffer = buffer; this.documents = documents; this.connection = connection; }
        public void Dispose() => disposed = true;

        public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var rpc = connection.Rpc;
            var snapshot = buffer.CurrentSnapshot;
            var trigger = session.GetTriggerPoint(snapshot);
            if (disposed || !connection.Ready || rpc == null || trigger == null || !documents.TryGetTextDocument(buffer, out var document)) return null;
            var line = trigger.Value.GetContainingLine();
            try
            {
                var hover = await rpc.InvokeWithParameterObjectAsync<JToken>("textDocument/hover", new
                {
                    textDocument = new { uri = new Uri(document.FilePath).AbsoluteUri },
                    position = new { line = line.LineNumber, character = trigger.Value.Position - line.Start.Position }
                }, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (disposed || buffer.CurrentSnapshot != snapshot || hover is not JObject || hover["_vs_rawContent"] is not JObject content) return null;
                int start = Offset(hover["range"]!["start"]!, snapshot), end = Offset(hover["range"]!["end"]!, snapshot);
                if (start < 0 || end < start) return null;
                return new QuickInfoItem(snapshot.CreateTrackingSpan(start, end - start, SpanTrackingMode.EdgeInclusive), HoverContent.Create(content, Navigate));
            }
            catch (OperationCanceledException) { return null; }
            catch (RemoteInvocationException error) { Debug.WriteLine("gflat hover: " + error.Message); return null; }
            catch (ConnectionLostException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }

        private static int Offset(JToken position, ITextSnapshot snapshot)
        {
            int line = (int)position["line"]!, column = (int)position["character"]!;
            if (line < 0 || line >= snapshot.LineCount || column < 0) return -1;
            var textLine = snapshot.GetLineFromLineNumber(line);
            return column <= textLine.Length ? textLine.Start.Position + column : -1;
        }

        private static void Navigate(string path, int line, int column)
        {
            // Native tooltip hyperlink clicks run on the UI thread.
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path, VSConstants.LOGVIEWID_Code,
                    out var hierarchy, out var item, out var frame, out var view);
                if (view != null)
                {
                    ErrorHandler.ThrowOnFailure(view.SetCaretPos(line, column));
                    ErrorHandler.ThrowOnFailure(view.CenterLines(line, 1));
                }
            }
            catch (Exception error) { Debug.WriteLine("gflat navigation: " + error.Message); }
        }
    }
}
