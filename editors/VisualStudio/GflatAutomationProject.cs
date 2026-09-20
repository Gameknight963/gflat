using System;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace gflat.VisualStudio;

/// <summary>Identity exposed to solution automation; file editing uses IVsProject.</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class GflatAutomationProject : Project
{
    private readonly GflatProject project;
    public GflatAutomationProject(GflatProject project) { this.project = project; }
    public string Name { get => project.Model.Name; set => throw new NotSupportedException("Project renaming is not supported yet."); }
    public string FileName => project.Model.FilePath;
    public string FullName => FileName;
    public string UniqueName => ThreadHelper.JoinableTaskFactory.Run(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var solution = (IVsSolution)((IServiceProvider)project.Package).GetService(typeof(SVsSolution));
        if (solution == null) throw new InvalidOperationException("Visual Studio solution service unavailable.");
        return solution.GetUniqueNameOfProject(project, out string name) == 0 ? name : FileName;
    });
    public string Kind => "{" + GflatProjectPackage.FactoryGuid + "}";
    public object Object => this;
    public DTE DTE => ThreadHelper.JoinableTaskFactory.Run(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return (DTE)((IServiceProvider)project.Package).GetService(typeof(DTE)) ?? throw new InvalidOperationException("Visual Studio automation unavailable.");
    });
    public EnvDTE.Projects Collection => ThreadHelper.JoinableTaskFactory.Run(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return DTE.Solution.Projects;
    });
    public bool IsDirty { get => false; set { } }
    public bool Saved { get => true; set { } }
    public ProjectItems ProjectItems => null!;
    public Properties Properties => null!;
    public ConfigurationManager ConfigurationManager => null!;
    public Globals Globals => null!;
    public ProjectItem ParentProjectItem => null!;
    public CodeModel CodeModel => null!;
    public string ExtenderCATID => "";
    public object ExtenderNames => Array.Empty<string>();
    public object get_Extender(string name) => null!;
    public void Save(string fileName = "")
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!string.IsNullOrEmpty(fileName) && !gflat.Projects.ProjectModel.Paths.Equals(fileName, FileName))
            throw new NotSupportedException("Project Save As is not supported yet.");
        var documents = (IVsRunningDocumentTable)((IServiceProvider)project.Package).GetService(typeof(SVsRunningDocumentTable));
        if (documents == null) throw new InvalidOperationException("Visual Studio document service unavailable.");
        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(documents.SaveDocuments((uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty, project, Microsoft.VisualStudio.VSConstants.VSITEMID_ROOT, 0));
    }
    public void SaveAs(string fileName) => throw new NotSupportedException("Project Save As is not supported yet.");
    public void Delete() => throw new NotSupportedException("Remove the project from Solution Explorer.");
}
