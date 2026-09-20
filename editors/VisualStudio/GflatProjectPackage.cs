using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace gflat.VisualStudio;

[ComVisible(true), Guid(PackageGuid), PackageRegistration(UseManagedResourcesOnly = true)]
public sealed class GflatProjectPackage : Package
{
    public const string PackageGuid = "315FB02C-BD6D-4B6A-9A89-4A5F9D5E9162";
    public const string FactoryGuid = "6423F711-946A-41AB-B95B-0F9B5911F803";
    protected override void Initialize()
    {
        base.Initialize();
        RegisterProjectFactory(new GflatProjectFactory(this));
    }
}

[Guid(GflatProjectPackage.FactoryGuid), ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class GflatProjectFactory : IVsProjectFactory
{
    private readonly GflatProjectPackage package;
    public GflatProjectFactory(GflatProjectPackage package) { this.package = package; }
    public int SetSite(OleServiceProvider site) => VSConstants.S_OK;
    public int Close() => VSConstants.S_OK;
    public int CanCreateProject(string filename, uint flags, out int canCreate)
    { canCreate = filename.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase) ? 1 : 0; return VSConstants.S_OK; }
    public int CreateProject(string filename, string location, string name, uint flags, ref Guid iid, out IntPtr result, out int canceled)
    {
        Guid requested = iid;
        IntPtr created = IntPtr.Zero;
        int hr = ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return CreateOnUIThread(filename, requested, out created);
        });
        result = created; canceled = 0;
        return hr;
    }
    private int CreateOnUIThread(string filename, Guid iid, out IntPtr result)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        result = IntPtr.Zero;
        try
        {
            if (!File.Exists(filename)) throw new FileNotFoundException("Create a .gfproj file, then use Add > Existing Project.", filename);
            var project = new GflatProject(package, filename);
            IntPtr unknown = Marshal.GetIUnknownForObject(project);
            try
            {
                return Marshal.QueryInterface(unknown, ref iid, out result);
            }
            finally { Marshal.Release(unknown); }
        }
        catch (Exception error)
        {
            ActivityLog.LogError("gflat", error.ToString());
            VsShellUtilities.ShowMessageBox(package, error.Message, "Cannot load gflat project",
                OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return VSConstants.E_FAIL;
        }
    }
}
