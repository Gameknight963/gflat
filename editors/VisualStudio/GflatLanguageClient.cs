using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace gflat.VisualStudio;

[Export(typeof(ILanguageClient))]
[ContentType("gflat")]
public sealed class GflatLanguageClient : ILanguageClient, IDisposable
{
    private Process? server;
    public string Name => "gflat";
    public IEnumerable<string> ConfigurationSections => Array.Empty<string>();
    public object? InitializationOptions => null;
    public IEnumerable<string> FilesToWatch => new[] { "gflat-workspace.json", "**/*.gf", "**/*.gfproj", "**/*.props", "**/*.targets" };
    public bool ShowNotificationOnInitializeFailed => true;
    public event AsyncEventHandler<EventArgs>? StartAsync;
#pragma warning disable CS0067 // Required by ILanguageClient; VS owns normal shutdown.
    public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await TaskScheduler.Default;
        token.ThrowIfCancellationRequested();
        Dispose();
        string executable = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Server", "gflat.LanguageServer.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The bundled gflat language server is missing. Reinstall the extension.", executable);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        process.ErrorDataReceived += (_, args) => { if (args.Data != null) Debug.WriteLine("gflat: " + args.Data); };
        if (!process.Start()) { process.Dispose(); throw new InvalidOperationException("Could not start the gflat language server"); }
        server = process;
        process.BeginErrorReadLine();
        return new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
    }

    public async Task OnLoadedAsync()
    {
        if (StartAsync != null) await StartAsync.InvokeAsync(this, EventArgs.Empty);
    }
    public Task OnServerInitializedAsync() => Task.CompletedTask;
    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
        => Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext { FailureMessage = "gflat language server initialization failed. Try reinstalling the extension." });

    public void Dispose()
    {
        var process = server;
        server = null;
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}

public static class GflatContentType
{
    [Export, Name("gflat"), BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    public static ContentTypeDefinition Definition = null!;

    [Export, FileExtension(".gf"), ContentType("gflat")]
    public static FileExtensionToContentTypeDefinition Extension = null!;
}
