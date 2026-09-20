using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace gflat.VisualStudio;

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class GflatProjectConfiguration : IVsCfgProvider2, IVsProjectCfgProvider, IVsProjectCfg2, IVsBuildableProjectCfg
{
    private readonly GflatProject project;
    private readonly Dictionary<uint, IVsBuildStatusCallback> callbacks = new();
    private readonly ErrorListProvider errors;
    private uint nextCookie = 1;
    private Process? process;
    private bool building, canceled, disposed;
    public GflatProjectConfiguration(GflatProject project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.project = project;
        errors = new ErrorListProvider(project.Package) { ProviderName = "gflat build", ProviderGuid = new Guid("B4E6560C-E935-4A03-944D-41B2C2B97F8D") };
    }
    public void Dispose() { ThreadHelper.ThrowIfNotOnUIThread(); disposed = true; Stop(0); errors.Dispose(); }
    public int GetCfgs(uint count, IVsCfg[] configs, uint[] actual, uint[] flags)
    { if (actual != null && actual.Length > 0) actual[0] = 1; if (count > 0 && configs != null) configs[0] = this; return VSConstants.S_OK; }
    public int GetCfgNames(uint count, string[] names, uint[] actual) { ThreadHelper.ThrowIfNotOnUIThread(); return Names(count, names, actual, "Debug"); }
    public int GetPlatformNames(uint count, string[] names, uint[] actual) { ThreadHelper.ThrowIfNotOnUIThread(); return Names(count, names, actual, "Any CPU"); }
    public int GetSupportedPlatformNames(uint count, string[] names, uint[] actual) { ThreadHelper.ThrowIfNotOnUIThread(); return GetPlatformNames(count, names, actual); }
    private static int Names(uint count, string[] names, uint[] actual, string value)
    { if (actual != null && actual.Length > 0) actual[0] = 1; if (count > 0 && names != null) names[0] = value; return VSConstants.S_OK; }
    public int GetCfgOfName(string name, string platform, out IVsCfg cfg) { ThreadHelper.ThrowIfNotOnUIThread(); cfg = this; return VSConstants.S_OK; }
    public int OpenProjectCfg(string name, out IVsProjectCfg cfg) { ThreadHelper.ThrowIfNotOnUIThread(); cfg = this; return VSConstants.S_OK; }
    public int get_UsesIndependentConfigurations(out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 0; return VSConstants.S_OK; }
    public int AddCfgsOfCfgName(string name, string clone, int value) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int DeleteCfgsOfCfgName(string name) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int RenameCfgsOfCfgName(string oldName, string newName) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int AddCfgsOfPlatformName(string name, string clone) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int DeleteCfgsOfPlatformName(string name) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int GetCfgProviderProperty(int property, out object value)
    {
        value = false;
        return property >= (int)__VSCFGPROPID.VSCFGPROPID_SupportsPrivateCfgs &&
            property <= (int)__VSCFGPROPID.VSCFGPROPID_SupportsCfgAdd ||
            property == (int)__VSCFGPROPID2.VSCFGPROPID_HideConfigurations
            ? VSConstants.S_OK : VSConstants.DISP_E_MEMBERNOTFOUND;
    }
    public int AdviseCfgProviderEvents(IVsCfgProviderEvents events, out uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); cookie = 0; return VSConstants.S_OK; }
    public int UnadviseCfgProviderEvents(uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.S_OK; }
    public int get_DisplayName(out string name) { ThreadHelper.ThrowIfNotOnUIThread(); name = "Debug|Any CPU"; return VSConstants.S_OK; }
    public int get_CanonicalName(out string name) { ThreadHelper.ThrowIfNotOnUIThread(); return get_DisplayName(out name); }
    public int get_IsDebugOnly(out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 0; return VSConstants.S_OK; }
    public int get_IsReleaseOnly(out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 0; return VSConstants.S_OK; }
    public int EnumOutputs(out IVsEnumOutputs value) { ThreadHelper.ThrowIfNotOnUIThread(); value = null!; return VSConstants.E_NOTIMPL; }
    public int OpenOutput(string name, out IVsOutput value) { ThreadHelper.ThrowIfNotOnUIThread(); value = null!; return VSConstants.E_NOTIMPL; }
    public int get_ProjectCfgProvider(out IVsProjectCfgProvider value) { ThreadHelper.ThrowIfNotOnUIThread(); value = this; return VSConstants.S_OK; }
    public int get_BuildableProjectCfg(out IVsBuildableProjectCfg value) { ThreadHelper.ThrowIfNotOnUIThread(); value = this; return VSConstants.S_OK; }
    public int get_ProjectCfg(out IVsProjectCfg value) { ThreadHelper.ThrowIfNotOnUIThread(); value = this; return VSConstants.S_OK; }
    public int get_CfgType(ref Guid iid, out IntPtr value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        IntPtr unknown = Marshal.GetIUnknownForObject(this);
        try { return Marshal.QueryInterface(unknown, ref iid, out value); }
        finally { Marshal.Release(unknown); }
    }
    public int get_OutputGroups(uint count, IVsOutputGroup[] groups, uint[] actual)
    { if (actual != null && actual.Length > 0) actual[0] = 0; return VSConstants.S_OK; }
    public int OpenOutputGroup(string name, out IVsOutputGroup group) { group = null!; return VSConstants.E_NOTIMPL; }
    public int OutputsRequireAppRoot(out int value) { value = 0; return VSConstants.S_OK; }
    public int get_VirtualRoot(out string value) { value = ""; return VSConstants.E_NOTIMPL; }
    public int get_IsPrivate(out int value) { value = 0; return VSConstants.S_OK; }
    public int get_Platform(out Guid value) { ThreadHelper.ThrowIfNotOnUIThread(); value = Guid.Empty; return VSConstants.S_OK; }
    public int get_IsPackaged(out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 0; return VSConstants.S_OK; }
    public int get_IsSpecifyingOutputSupported(out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 0; return VSConstants.S_OK; }
    public int get_TargetCodePage(out uint value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 65001; return VSConstants.S_OK; }
    public int get_UpdateSequenceNumber(ULARGE_INTEGER[] value) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int get_RootURL(out string value) { ThreadHelper.ThrowIfNotOnUIThread(); value = new Uri(project.Model.DirectoryPath + Path.DirectorySeparatorChar).AbsoluteUri; return VSConstants.S_OK; }
    public int AdviseBuildStatusCallback(IVsBuildStatusCallback callback, out uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); cookie = nextCookie++; callbacks[cookie] = callback; return VSConstants.S_OK; }
    public int UnadviseBuildStatusCallback(uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); callbacks.Remove(cookie); return VSConstants.S_OK; }
    public int QueryStatus(out int done) { ThreadHelper.ThrowIfNotOnUIThread(); done = building ? 0 : 1; return VSConstants.S_OK; }
    public int QueryStartBuild(uint flags, int[] supported, int[] ready) { ThreadHelper.ThrowIfNotOnUIThread(); return Ready(supported, ready, true); }
    public int QueryStartClean(uint flags, int[] supported, int[] ready) { ThreadHelper.ThrowIfNotOnUIThread(); return Ready(supported, ready, true); }
    public int QueryStartUpToDateCheck(uint flags, int[] supported, int[] ready) { ThreadHelper.ThrowIfNotOnUIThread(); return Ready(supported, ready, false); }
    private int Ready(int[] supported, int[] ready, bool support)
    { if (supported != null && supported.Length > 0) supported[0] = support ? 1 : 0; if (ready != null && ready.Length > 0) ready[0] = building ? 0 : 1; return VSConstants.S_OK; }
    public int StartUpToDateCheck(IVsOutputWindowPane pane, uint flags) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int Wait(uint milliseconds, int tick) { ThreadHelper.ThrowIfNotOnUIThread(); return building ? VSConstants.E_PENDING : VSConstants.S_OK; }
    public int Stop(int sync)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        canceled = true;
        try { if (process != null && !process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        return VSConstants.S_OK;
    }
    public int StartBuild(IVsOutputWindowPane pane, uint flags) { ThreadHelper.ThrowIfNotOnUIThread(); return Start(pane, "build"); }
    public int StartClean(IVsOutputWindowPane pane, uint flags) { ThreadHelper.ThrowIfNotOnUIThread(); return Start(pane, "clean"); }
    private int Start(IVsOutputWindowPane? pane, string command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposed) return VSConstants.E_UNEXPECTED;
        if (building) return VSConstants.E_PENDING;
        var documents = (IVsRunningDocumentTable)((System.IServiceProvider)project.Package).GetService(typeof(SVsRunningDocumentTable)) ?? throw new InvalidOperationException("Visual Studio service unavailable");
        // Referenced source libraries may have unsaved documents in other projects.
        int saved = documents.SaveDocuments((uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty, null!, VSConstants.VSITEMID_NIL, 0);
        if (ErrorHandler.Failed(saved)) return saved;
        if (pane == null)
        {
            var output = (IVsOutputWindow)((System.IServiceProvider)project.Package).GetService(typeof(SVsOutputWindow)) ?? throw new InvalidOperationException("Visual Studio service unavailable");
            Guid id = new Guid("FD45A8CD-2329-4D64-A9B1-7D537B0F9B2D");
            output.CreatePane(ref id, "gflat build", 1, 0);
            output.GetPane(ref id, out pane);
        }
        pane.Activate();
        errors.Tasks.Clear();
        building = true; canceled = false;
        foreach (var callback in callbacks.Values)
        {
            int proceed = 1; callback.BuildBegin(ref proceed);
            if (proceed == 0) canceled = true;
        }
        var outputPane = pane;
        // The build manager receives BuildEnd from finally; Stop/Close owns cancellation.
#pragma warning disable VSSDK007
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            int success = 0;
            try
            {
                string executable = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Compiler", "gflat.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("The bundled gflat compiler is missing. Rebuild/reinstall the VSIX.");
                if (canceled) return;
                var child = process = new Process { StartInfo = new ProcessStartInfo(executable)
                {
                    Arguments = command + " \"" + project.Model.FilePath + "\"", WorkingDirectory = project.Model.DirectoryPath,
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                } };
                outputPane.OutputStringThreadSafe("gflat " + child.StartInfo.Arguments + Environment.NewLine);
                child.Start();
                Task<string> stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync();
                await Task.Run(() => child.WaitForExit());
                string text = await stdout + await stderr;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (disposed) return;
                outputPane.OutputStringThreadSafe(text);
                foreach (string line in text.Split('\n'))
                {
                    var match = Regex.Match(line, @"^(.*)\((\d+),(\d+)\): (error|warning) ([^:]+): (.*)$");
                    if (!match.Success) continue;
                    var task = new ErrorTask
                    {
                        Category = TaskCategory.BuildCompile,
                        ErrorCategory = match.Groups[4].Value == "error" ? TaskErrorCategory.Error : TaskErrorCategory.Warning,
                        Document = match.Groups[1].Value, Line = int.Parse(match.Groups[2].Value) - 1,
                        Column = int.Parse(match.Groups[3].Value) - 1, Text = match.Groups[5].Value + ": " + match.Groups[6].Value
                    };
                    task.Navigate += (_, _) => errors.Navigate(task, VSConstants.LOGVIEWID_Code);
                    errors.Tasks.Add(task);
                }
                success = !canceled && child.ExitCode == 0 ? 1 : 0;
                outputPane.OutputStringThreadSafe(success == 1 ? "gflat build succeeded.\n" : "gflat build failed or canceled.\n");
            }
            catch (Exception error)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                outputPane.OutputStringThreadSafe(error.Message + Environment.NewLine);
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                process?.Dispose(); process = null; building = false;
                foreach (var callback in new List<IVsBuildStatusCallback>(callbacks.Values)) callback.BuildEnd(success);
            }
        }).FileAndForget("gflat/project-build");
#pragma warning restore VSSDK007
        return VSConstants.S_OK;
    }
}
