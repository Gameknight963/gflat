using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TemplateWizard;
using Microsoft.VisualStudio.TextManager.Interop;

namespace gflat.VisualStudio;

public sealed class GflatSourceFileWizard : IWizard
{
    private GflatOptions options = null!;
    private string indentation = "    ";

    public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary,
        WizardRunKind runKind, object[] customParams)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var shell = (IVsShell)Package.GetGlobalService(typeof(SVsShell));
        var id = typeof(GflatEditorPackage).GUID;
        ErrorHandler.ThrowOnFailure(shell.LoadPackage(ref id, out var package));
        options = ((GflatEditorPackage)package).Options;
        var preferences = new[] { new LANGPREFERENCES { guidLang = typeof(GflatLanguageInfo).GUID } };
        if (Package.GetGlobalService(typeof(SVsTextManager)) is IVsTextManager textManager &&
            ErrorHandler.Succeeded(textManager.GetUserPreferences(null, null, preferences, null)))
        {
            if (preferences[0].fInsertTabs != 0) indentation = "\t";
            else if (preferences[0].uIndentSize > 0 && preferences[0].uIndentSize <= 256)
                indentation = new string(' ', (int)preferences[0].uIndentSize);
        }
    }

    public void ProjectItemFinishedGenerating(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string file = projectItem.FileNames[1];
        var project = projectItem.ContainingProject;
        string rootNamespace = Property(project, "RootNamespace") ?? Property(project, "DefaultNamespace") ?? "";
        string root = Property(project, "GflatNamespaceRoot") ?? "";
        string folder = SourceFileTemplate.RelativeFolder(project.FullName, file, root);
        string contents = SourceFileTemplate.Generate(Path.GetFileName(file), rootNamespace, folder,
            options.GenerateClassFromFileName, options.GenerateNamespace, options.BracesOnNewLine, indentation);
        File.WriteAllText(file, contents, new UTF8Encoding(false));
    }

    private static string? Property(Project project, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return project.Properties.Item(name)?.Value as string; }
        catch (ArgumentException) { return null; }
        catch (COMException) { return null; }
    }

    public bool ShouldAddProjectItem(string filePath) => true;
    public void BeforeOpeningFile(ProjectItem projectItem) { }
    public void ProjectFinishedGenerating(Project project) { }
    public void RunFinished() { }
}
