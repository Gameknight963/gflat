using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using gflat.Projects;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace gflat.VisualStudio;

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class GflatProject : IVsUIHierarchy, IVsProject3, IPersistFileFormat,
    IOleCommandTarget, IVsGetCfgProvider, IVsHierarchyDeleteHandler, IVsPersistHierarchyItem2
{
    internal readonly GflatProjectPackage Package;
    internal ProjectModel Model { get; private set; }
    private readonly GflatProjectConfiguration configuration;
    private readonly Dictionary<uint, Node> nodes = new();
    private readonly Dictionary<string, uint> identities = new(ProjectModel.Paths);
    private readonly Dictionary<uint, IVsHierarchyEvents> listeners = new();
    private readonly ErrorListProvider errors;
    private readonly GflatAutomationProject automation;
    private OleServiceProvider? site;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? reload;
    private uint nextId = 1, nextCookie = 1;
    private bool closed;
    private object? parentHierarchy;
    private object parentId = VSConstants.VSITEMID_NIL;
    private Guid projectId = Guid.NewGuid();
    private const uint Root = VSConstants.VSITEMID_ROOT, Nil = VSConstants.VSITEMID_NIL;
    private sealed class Node
    {
        public uint Id, Parent;
        public string Path = "";
        public bool Folder;
        public List<uint> Children = new();
    }

    public GflatProject(GflatProjectPackage package, string filename)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Package = package;
        Model = ProjectModel.Load(filename);
        automation = new GflatAutomationProject(this);
        errors = new ErrorListProvider(package) { ProviderName = "gflat projects", ProviderGuid = new Guid(GflatProjectPackage.FactoryGuid) };
        configuration = new GflatProjectConfiguration(this);
        RefreshTree(Model.SourceFiles());
        watcher = new FileSystemWatcher(Model.DirectoryPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        watcher.Changed += (_, e) => { if (ProjectModel.Paths.Equals(e.FullPath, Model.FilePath)) ScheduleReload(e.FullPath); };
        watcher.Created += (_, e) => ScheduleReload(e.FullPath);
        watcher.Deleted += (_, e) => ScheduleReload(e.FullPath);
        watcher.Renamed += (_, e) => ScheduleReload(e.FullPath);
        watcher.Error += (_, _) => ScheduleReload(Model.FilePath);
    }

    private void ScheduleReload(string path)
    {
        string root = Model.DirectoryPath + Path.DirectorySeparatorChar;
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            path.Substring(root.Length).Split(Path.DirectorySeparatorChar).Any(ProjectModel.IsGeneratedDirectory)) return;
        // Saving a source changes diagnostics, but not the tree. Coalesce file system
        // events and do XML parsing/source discovery off the UI thread.
        // Lifecycle is owned by the project: Close cancels the token; all failures
        // are handled below and FileAndForget observes unexpected task failures.
#pragma warning disable VSSDK007
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (closed) return;
            reload?.Cancel(); reload?.Dispose();
            var current = reload = new CancellationTokenSource();
            try
            {
                await Task.Delay(200, current.Token);
                var result = await Task.Run(() =>
                {
                    var model = ProjectModel.Load(Model.FilePath);
                    return (Model: model, Sources: model.SourceFiles());
                }, current.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(current.Token);
                if (closed) return;
                Model = result.Model;
                errors.Tasks.Clear();
                RefreshTree(result.Sources);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!closed && !current.IsCancellationRequested) ReportProjectError(error);
            }
        }).FileAndForget("gflat/project-reload");
#pragma warning restore VSSDK007
    }

    private void ReportProjectError(Exception error)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        errors.Tasks.Clear();
        var projectError = error as ProjectException;
        var task = new ErrorTask
        {
            Category = TaskCategory.BuildCompile, ErrorCategory = TaskErrorCategory.Error,
            Document = projectError?.ProjectPath ?? Model.FilePath,
            Line = Math.Max(0, (projectError?.Line ?? 1) - 1),
            Column = Math.Max(0, (projectError?.Column ?? 1) - 1),
            Text = error.Message + " (keeping the last valid project configuration)"
        };
        task.Navigate += (_, _) => VsShellUtilities.OpenDocument(Package, task.Document);
        errors.Tasks.Add(task);
    }

    private uint Identity(string path)
    {
        if (!identities.TryGetValue(path, out uint id)) identities[path] = id = nextId++;
        return id;
    }
    private void RefreshTree(IReadOnlyList<string> sources)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var old = nodes.Keys.ToArray();
        nodes.Clear();
        nodes[Root] = new Node { Id = Root, Parent = Nil, Path = Model.DirectoryPath, Folder = true };
        uint Add(string path, bool folder)
        {
            if (ProjectModel.Paths.Equals(path, Model.DirectoryPath)) return Root;
            uint id = Identity(path);
            if (nodes.ContainsKey(id)) return id;
            uint parent = Add(Path.GetDirectoryName(path)!, true);
            nodes[id] = new Node { Id = id, Parent = parent, Path = path, Folder = folder };
            nodes[parent].Children.Add(id);
            return id;
        }
        Add(Model.FilePath, false);
        foreach (string source in sources) Add(source, false);
        // Preserve empty folders created from Solution Explorer.
        foreach (string folder in SourceDirectories(Model.DirectoryPath))
            if (!ProjectModel.IsGeneratedDirectory(Path.GetFileName(folder)) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0)
                Add(folder, true);
        foreach (var node in nodes.Values)
            node.Children.Sort((a, b) => nodes[a].Folder != nodes[b].Folder ? (nodes[a].Folder ? -1 : 1) :
                StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(nodes[a].Path), Path.GetFileName(nodes[b].Path)));
        foreach (var listener in listeners.Values.ToArray())
        {
            foreach (uint removed in old.Where(id => !nodes.ContainsKey(id))) listener.OnItemDeleted(removed);
            listener.OnInvalidateItems(Root);
            listener.OnPropertyChanged(Root, (int)__VSHPROPID.VSHPROPID_Caption, 0);
        }
    }

    private static IEnumerable<string> SourceDirectories(string root)
    {
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            if (ProjectModel.IsGeneratedDirectory(Path.GetFileName(directory)) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                Directory.EnumerateFiles(directory, "*.gfproj").Any()) continue;
            yield return directory;
            foreach (string child in SourceDirectories(directory)) yield return child;
        }
    }

    public int SetSite(OleServiceProvider value) { ThreadHelper.ThrowIfNotOnUIThread(); site = value; return VSConstants.S_OK; }
    public int GetSite(out OleServiceProvider value) { ThreadHelper.ThrowIfNotOnUIThread(); value = site!; return site == null ? VSConstants.E_NOINTERFACE : VSConstants.S_OK; }
    public int QueryClose(out int canClose) { ThreadHelper.ThrowIfNotOnUIThread(); canClose = 1; return VSConstants.S_OK; }
    public int Close()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        closed = true; watcher?.Dispose(); reload?.Cancel(); reload?.Dispose(); configuration.Dispose(); errors.Dispose(); listeners.Clear();
        return VSConstants.S_OK;
    }
    public int GetGuidProperty(uint item, int property, out Guid value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        value = Guid.Empty;
        switch ((__VSHPROPID)property)
        {
            case __VSHPROPID.VSHPROPID_TypeGuid:
                value = item == Root ? new Guid(GflatProjectPackage.FactoryGuid) : nodes.TryGetValue(item, out var node) && node.Folder ? VSConstants.GUID_ItemType_PhysicalFolder : VSConstants.GUID_ItemType_PhysicalFile; break;
            case __VSHPROPID.VSHPROPID_ProjectIDGuid: value = projectId; break;
            case __VSHPROPID.VSHPROPID_CmdUIGuid: value = new Guid(GflatProjectPackage.FactoryGuid); break;
            default: return VSConstants.DISP_E_MEMBERNOTFOUND;
        }
        return VSConstants.S_OK;
    }
    public int SetGuidProperty(uint item, int property, ref Guid value)
    { if (property == (int)__VSHPROPID.VSHPROPID_ProjectIDGuid) { projectId = value; return VSConstants.S_OK; } return VSConstants.DISP_E_MEMBERNOTFOUND; }
    public int GetProperty(uint item, int property, out object value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        value = null!;
        if (!nodes.TryGetValue(item, out var node)) return VSConstants.E_INVALIDARG;
        if (property == (int)__VSHPROPID5.VSHPROPID_ProjectCapabilities) { value = "Gflat"; return VSConstants.S_OK; }
        if (property == (int)__VSHPROPID5.VSHPROPID_RequiresReloadForExternalFileChange) { value = false; return VSConstants.S_OK; }
        switch ((__VSHPROPID)property)
        {
            case __VSHPROPID.VSHPROPID_Parent: value = node.Parent; break;
            case __VSHPROPID.VSHPROPID_FirstChild:
            case __VSHPROPID.VSHPROPID_FirstVisibleChild: value = node.Children.Count > 0 ? node.Children[0] : Nil; break;
            case __VSHPROPID.VSHPROPID_NextSibling:
            case __VSHPROPID.VSHPROPID_NextVisibleSibling:
                var siblings = node.Parent == Nil ? new List<uint>() : nodes[node.Parent].Children;
                int index = siblings.IndexOf(item) + 1;
                value = index > 0 && index < siblings.Count ? siblings[index] : Nil; break;
            case __VSHPROPID.VSHPROPID_Caption:
            case __VSHPROPID.VSHPROPID_Name:
            case __VSHPROPID.VSHPROPID_EditLabel: value = item == Root ? Model.Name : Path.GetFileName(node.Path); break;
            case __VSHPROPID.VSHPROPID_SaveName: value = item == Root ? Model.FilePath : node.Path; break;
            case __VSHPROPID.VSHPROPID_ProjectDir: value = Model.DirectoryPath + Path.DirectorySeparatorChar; break;
            case __VSHPROPID.VSHPROPID_TypeName: value = "gflat"; break;
            case __VSHPROPID.VSHPROPID_ExtObject:
            case __VSHPROPID.VSHPROPID_BrowseObject: value = item == Root ? automation : null!; break;
            case __VSHPROPID.VSHPROPID_Expandable: value = node.Folder; break;
            case __VSHPROPID.VSHPROPID_ExpandByDefault: value = item == Root; break;
            case __VSHPROPID.VSHPROPID_IconHandle: value = node.Folder ? System.Drawing.SystemIcons.Application.Handle : System.Drawing.SystemIcons.Information.Handle; break;
            case __VSHPROPID.VSHPROPID_ConfigurationProvider: value = configuration; break;
            case __VSHPROPID.VSHPROPID_ParentHierarchy: value = parentHierarchy!; break;
            case __VSHPROPID.VSHPROPID_ParentHierarchyItemid: value = parentId; break;
            case __VSHPROPID.VSHPROPID_HandlesOwnReload:
            case __VSHPROPID.VSHPROPID_DefaultEnableBuildProjectCfg: value = true; break;
            default: return VSConstants.DISP_E_MEMBERNOTFOUND;
        }
        return VSConstants.S_OK;
    }
    public int SetProperty(uint item, int property, object value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (property == (int)__VSHPROPID.VSHPROPID_ParentHierarchy) { parentHierarchy = value; return VSConstants.S_OK; }
        if (property == (int)__VSHPROPID.VSHPROPID_ParentHierarchyItemid) { parentId = value; return VSConstants.S_OK; }
        if (property == (int)__VSHPROPID.VSHPROPID_Expanded) return VSConstants.S_OK;
        if (property == (int)__VSHPROPID.VSHPROPID_EditLabel && item != Root && nodes.TryGetValue(item, out var node))
            return RunAction(() => Rename(node, (string)value));
        return VSConstants.DISP_E_MEMBERNOTFOUND;
    }
    private int RunAction(Action action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { action(); return VSConstants.S_OK; }
        catch (Exception error) { VsShellUtilities.ShowMessageBox(Package, error.Message, "gflat project", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST); return VSConstants.E_FAIL; }
    }
    private void Rename(Node node, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ProjectModel.Paths.Equals(node.Path, Model.FilePath)) throw new InvalidOperationException("Project-file renaming is not supported by this prototype.");
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Enter a file name, not a path.");
        string target = Path.Combine(Path.GetDirectoryName(node.Path)!, name);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The destination already exists.");
        if (node.Folder && Directory.EnumerateFileSystemEntries(node.Path).Any())
            throw new InvalidOperationException("Only empty folders can be renamed in this prototype.");
        // Explicit source patterns may intentionally exclude the new name. Avoid silently
        // removing a renamed file from compilation.
        if (!node.Folder && !Model.IncludesFile(target)) throw new InvalidOperationException("The new name is excluded by the project's Sources rules. Edit .gfproj first.");
        var table = (IVsRunningDocumentTable)((System.IServiceProvider)Package).GetService(typeof(SVsRunningDocumentTable)) ?? throw new InvalidOperationException("Visual Studio service unavailable");
        if (node.Folder) Directory.Move(node.Path, target); else File.Move(node.Path, target);
        IntPtr hierarchy = Marshal.GetIUnknownForObject(this);
        try
        {
            if (!node.Folder) ErrorHandler.ThrowOnFailure(table.RenameDocument(node.Path, target, hierarchy, node.Id));
        }
        catch
        {
            if (!node.Folder) File.Move(target, node.Path);
            throw;
        }
        finally { Marshal.Release(hierarchy); }
        identities[target] = node.Id;
        RefreshTree(Model.SourceFiles());
    }
    public int GetNestedHierarchy(uint item, ref Guid iid, out IntPtr hierarchy, out uint nested) { ThreadHelper.ThrowIfNotOnUIThread(); hierarchy = IntPtr.Zero; nested = Nil; return VSConstants.E_NOINTERFACE; }
    public int GetCanonicalName(uint item, out string name) { ThreadHelper.ThrowIfNotOnUIThread(); name = item == Root ? Model.FilePath : nodes.TryGetValue(item, out var node) ? node.Path : ""; return name.Length == 0 ? VSConstants.E_INVALIDARG : VSConstants.S_OK; }
    public int ParseCanonicalName(string name, out uint item) { ThreadHelper.ThrowIfNotOnUIThread(); item = ProjectModel.Paths.Equals(name, Model.FilePath) ? Root : nodes.Values.FirstOrDefault(n => ProjectModel.Paths.Equals(n.Path, name))?.Id ?? Nil; return item == Nil ? VSConstants.E_FAIL : VSConstants.S_OK; }
    public int AdviseHierarchyEvents(IVsHierarchyEvents events, out uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); cookie = nextCookie++; listeners[cookie] = events; return VSConstants.S_OK; }
    public int UnadviseHierarchyEvents(uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); listeners.Remove(cookie); return VSConstants.S_OK; }
    public int Unused0() { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int Unused1() { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int Unused2() { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int Unused3() { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int Unused4() { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }

    public int IsDocumentInProject(string path, out int found, VSDOCUMENTPRIORITY[] priority, out uint item)
    { ThreadHelper.ThrowIfNotOnUIThread(); ParseCanonicalName(path, out item); found = item == Nil ? 0 : 1; if (priority != null && priority.Length > 0) priority[0] = VSDOCUMENTPRIORITY.DP_Standard; return VSConstants.S_OK; }
    public int GetMkDocument(uint item, out string name) { ThreadHelper.ThrowIfNotOnUIThread(); return GetCanonicalName(item, out name); }
    public int GetItemContext(uint item, out OleServiceProvider provider) { ThreadHelper.ThrowIfNotOnUIThread(); return GetSite(out provider); }
    public int OpenItem(uint item, ref Guid view, IntPtr existing, out IVsWindowFrame frame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        frame = null!;
        GetCanonicalName(item, out string path);
        if (!File.Exists(path)) return VSConstants.E_INVALIDARG;
        var service = (IVsUIShellOpenDocument)((System.IServiceProvider)Package).GetService(typeof(SVsUIShellOpenDocument)) ?? throw new InvalidOperationException("Visual Studio service unavailable");
        int result = service.OpenStandardEditor(0, path, ref view, Path.GetFileName(path), this, item, existing, site!, out frame);
        if (ErrorHandler.Succeeded(result)) frame.Show();
        return result;
    }
    public int ReopenItem(uint item, ref Guid editor, string physical, ref Guid view, IntPtr existing, out IVsWindowFrame frame) { ThreadHelper.ThrowIfNotOnUIThread(); return OpenItem(item, ref view, existing, out frame); }
    public int OpenItemWithSpecific(uint item, uint flags, ref Guid editor, string physical, ref Guid view, IntPtr existing, out IVsWindowFrame frame) { ThreadHelper.ThrowIfNotOnUIThread(); return OpenItem(item, ref view, existing, out frame); }
    public int GenerateUniqueItemName(uint item, string extension, string suggested, out string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string directory = Destination(item);
        name = suggested + extension;
        for (int i = 1; File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name)); i++) name = suggested + i + extension;
        return VSConstants.S_OK;
    }
    private string Destination(uint item) => nodes.TryGetValue(item, out var node) && node.Folder ? node.Path : Model.DirectoryPath;
    public int AddItem(uint item, VSADDITEMOPERATION operation, string name, uint count, string[] files, IntPtr owner, VSADDRESULT[] result)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        int hr = RunAction(() =>
        {
            foreach (string file in files.Take((int)count))
            {
                string target = Path.Combine(Destination(item), Path.GetFileName(file));
                if (!Model.IncludesFile(target)) throw new InvalidOperationException("The file is excluded by the project's Sources rules.");
                if (!ProjectModel.Paths.Equals(Path.GetFullPath(file), target)) File.Copy(file, target, false);
            }
            RefreshTree(Model.SourceFiles());
        });
        if (result.Length > 0) result[0] = hr == 0 ? VSADDRESULT.ADDRESULT_Success : VSADDRESULT.ADDRESULT_Failure;
        return hr;
    }
    public int AddItemWithSpecific(uint item, VSADDITEMOPERATION operation, string name, uint count, string[] files, IntPtr owner, uint flags, ref Guid editor, string physical, ref Guid view, VSADDRESULT[] result) { ThreadHelper.ThrowIfNotOnUIThread(); return AddItem(item, operation, name, count, files, owner, result); }
    public int RemoveItem(uint reserved, uint item, out int result) { ThreadHelper.ThrowIfNotOnUIThread(); result = 0; return VSConstants.E_NOTIMPL; }
    public int TransferItem(string oldName, string newName, IVsWindowFrame frame) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }

    public int GetClassID(out Guid value) { ThreadHelper.ThrowIfNotOnUIThread(); value = new Guid(GflatProjectPackage.FactoryGuid); return VSConstants.S_OK; }
    public int IsDirty(out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 0; return VSConstants.S_OK; }
    public int InitNew(uint format) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.E_NOTIMPL; }
    public int Load(string file, uint mode, int readOnly) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.S_OK; }
    public int Save(string file, int remember, uint format) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.S_OK; } // XML is edited/saved by its document editor.
    public int SaveCompleted(string file) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.S_OK; }
    public int GetCurFile(out string file, out uint format) { ThreadHelper.ThrowIfNotOnUIThread(); file = Model.FilePath; format = 0; return VSConstants.S_OK; }
    public int GetFormatList(out string formats) { ThreadHelper.ThrowIfNotOnUIThread(); formats = "gflat project (*.gfproj)\n*.gfproj\n"; return VSConstants.S_OK; }
    public int GetCfgProvider(out IVsCfgProvider provider) { ThreadHelper.ThrowIfNotOnUIThread(); provider = configuration; return VSConstants.S_OK; }

    public int IsItemDirty(uint item, IntPtr data, out int dirty)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        dirty = 0;
        return data != IntPtr.Zero && Marshal.GetObjectForIUnknown(data) is IVsPersistDocData document
            ? document.IsDocDataDirty(out dirty) : VSConstants.S_OK;
    }
    public int SaveItem(VSSAVEFLAGS flags, string name, uint item, IntPtr data, out int canceled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        canceled = 0;
        return data != IntPtr.Zero && Marshal.GetObjectForIUnknown(data) is IVsPersistDocData document
            ? document.SaveDocData(flags, out _, out canceled) : VSConstants.E_INVALIDARG;
    }
    public int IsItemReloadable(uint item, out int value) { ThreadHelper.ThrowIfNotOnUIThread(); value = 1; return VSConstants.S_OK; }
    public int ReloadItem(uint item, uint reserved) { ThreadHelper.ThrowIfNotOnUIThread(); ScheduleReload(Model.FilePath); return VSConstants.S_OK; }
    public int IgnoreItemFileChanges(uint item, int ignore) { ThreadHelper.ThrowIfNotOnUIThread(); return VSConstants.S_OK; }

    public int QueryStatus(ref Guid group, uint count, OLECMD[] commands, IntPtr text) { ThreadHelper.ThrowIfNotOnUIThread(); return QueryStatusCommand(Root, ref group, count, commands, text); }
    public int Exec(ref Guid group, uint command, uint options, IntPtr input, IntPtr output) { ThreadHelper.ThrowIfNotOnUIThread(); return ExecCommand(Root, ref group, command, options, input, output); }
    public int QueryStatusCommand(uint item, ref Guid group, uint count, OLECMD[] commands, IntPtr text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (group == VSConstants.GUID_VsUIHierarchyWindowCmds) return VSConstants.S_OK;
        if (group != VSConstants.GUID_VSStandardCommandSet97) return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_UNKNOWNGROUP;
        for (int i = 0; i < count; i++)
        {
            var cmd = (VSConstants.VSStd97CmdID)commands[i].cmdID;
            if (cmd is VSConstants.VSStd97CmdID.Open or VSConstants.VSStd97CmdID.ViewCode or
                VSConstants.VSStd97CmdID.AddNewItem or VSConstants.VSStd97CmdID.AddExistingItem or
                VSConstants.VSStd97CmdID.NewFolder or VSConstants.VSStd97CmdID.BuildCtx or VSConstants.VSStd97CmdID.RebuildCtx or
                VSConstants.VSStd97CmdID.CleanCtx || item != Root && cmd is VSConstants.VSStd97CmdID.Rename or VSConstants.VSStd97CmdID.Delete)
                commands[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
        }
        return VSConstants.S_OK;
    }
    public int ExecCommand(uint item, ref Guid group, uint command, uint options, IntPtr input, IntPtr output)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (group == VSConstants.GUID_VsUIHierarchyWindowCmds)
        {
            if (command == (uint)VSConstants.VsUIHierarchyWindowCmdIds.UIHWCMDID_DoubleClick || command == (uint)VSConstants.VsUIHierarchyWindowCmdIds.UIHWCMDID_EnterKey)
            { Guid view = VSConstants.LOGVIEWID_Primary; return OpenItem(item, ref view, IntPtr.Zero, out _); }
            if (command == (uint)VSConstants.VsUIHierarchyWindowCmdIds.UIHWCMDID_RightClick)
            {
                ShowMenu(item); return VSConstants.S_OK;
            }
        }
        if (group != VSConstants.GUID_VSStandardCommandSet97) return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        switch ((VSConstants.VSStd97CmdID)command)
        {
            case VSConstants.VSStd97CmdID.Open:
            case VSConstants.VSStd97CmdID.ViewCode: Guid view = VSConstants.LOGVIEWID_Primary; return OpenItem(item, ref view, IntPtr.Zero, out _);
            case VSConstants.VSStd97CmdID.AddNewItem: return RunAction(() => NewItem(item, false));
            case VSConstants.VSStd97CmdID.NewFolder: return RunAction(() => NewItem(item, true));
            case VSConstants.VSStd97CmdID.AddExistingItem: return RunAction(() => AddExisting(item));
            case VSConstants.VSStd97CmdID.Delete: return DeleteItem(0, item);
            case VSConstants.VSStd97CmdID.Rename:
                return RunAction(() =>
                {
                    if (item == Root || !nodes.TryGetValue(item, out var node)) return;
                    string? name = Prompt("Rename", Path.GetFileName(node.Path));
                    if (name != null) Rename(node, name);
                });
            case VSConstants.VSStd97CmdID.BuildCtx:
            case VSConstants.VSStd97CmdID.RebuildCtx: return configuration.StartBuild(null!, 0);
            case VSConstants.VSStd97CmdID.CleanCtx: return configuration.StartClean(null!, 0);
            default: return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }
    }
    private void ShowMenu(uint item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); Guid view = VSConstants.LOGVIEWID_Primary; OpenItem(item, ref view, IntPtr.Zero, out _); });
        menu.Items.Add("Edit project file", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); Guid view = VSConstants.LOGVIEWID_Primary; OpenItem(Root, ref view, IntPtr.Zero, out _); });
        menu.Items.Add("New .gf file...", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); RunAction(() => NewItem(item, false)); });
        menu.Items.Add("New folder...", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); RunAction(() => NewItem(item, true)); });
        menu.Items.Add("Add existing file...", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); RunAction(() => AddExisting(item)); });
        if (item != Root && nodes.TryGetValue(item, out var node) && !ProjectModel.Paths.Equals(node.Path, Model.FilePath))
        {
            menu.Items.Add("Rename...", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); string? name = Prompt("Rename", Path.GetFileName(node.Path)); if (name != null) RunAction(() => Rename(node, name)); });
            menu.Items.Add("Delete", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); DeleteItem(0, item); });
        }
        menu.Items.Add("Build", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); configuration.StartBuild(null!, 0); });
        menu.Items.Add("Clean", null, (_, _) => { ThreadHelper.ThrowIfNotOnUIThread(); configuration.StartClean(null!, 0); });
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(Cursor.Position);
    }
    private static string? Prompt(string title, string initial)
    {
        using var form = new Form { Text = title, Width = 380, Height = 135, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var text = new TextBox { Text = initial, Left = 12, Top = 12, Width = 340 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 190, Top = 45 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 275, Top = 45 };
        form.Controls.AddRange(new Control[] { text, ok, cancel }); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? text.Text : null;
    }
    private void NewItem(uint item, bool folder)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string? name = Prompt(folder ? "New folder" : "New gflat file", folder ? "NewFolder" : "NewFile.gf");
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Enter a name, not a path.");
        string target = Path.Combine(Destination(item), name);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("That name already exists.");
        if (folder) Directory.CreateDirectory(target);
        else
        {
            if (!Model.IncludesFile(target)) throw new InvalidOperationException("The file is excluded by the project's Sources rules.");
            using (File.Create(target)) { }
        }
        RefreshTree(Model.SourceFiles());
        if (!folder) { Guid view = VSConstants.LOGVIEWID_Primary; OpenItem(Identity(target), ref view, IntPtr.Zero, out _); }
    }
    private void AddExisting(uint item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        using var dialog = new OpenFileDialog { Filter = "gflat source (*.gf)|*.gf", Multiselect = true };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var result = new VSADDRESULT[1];
        AddItem(item, VSADDITEMOPERATION.VSADDITEMOP_CLONEFILE, "", (uint)dialog.FileNames.Length, dialog.FileNames, IntPtr.Zero, result);
    }
    public int QueryDeleteItem(uint operation, uint item, out int canDelete)
    { canDelete = item != Root && nodes.TryGetValue(item, out var node) && !ProjectModel.Paths.Equals(node.Path, Model.FilePath) ? 1 : 0; return VSConstants.S_OK; }
    public int DeleteItem(uint operation, uint item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        QueryDeleteItem(operation, item, out int allowed);
        if (allowed == 0) return VSConstants.E_INVALIDARG;
        var node = nodes[item];
        if (MessageBox.Show("Delete " + node.Path + " from disk?", "gflat project", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return VSConstants.S_OK;
        return RunAction(() =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (node.Folder) Directory.Delete(node.Path, false); // Empty folders only; never recursively delete user files.
            else
            {
                if (VsShellUtilities.IsDocumentOpen(Package, node.Path, VSConstants.LOGVIEWID_Primary, out _, out _, out IVsWindowFrame frame))
                    ErrorHandler.ThrowOnFailure(frame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_PromptSave));
                File.Delete(node.Path);
            }
            RefreshTree(Model.SourceFiles());
        });
    }
}
