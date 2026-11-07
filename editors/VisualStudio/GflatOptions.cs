using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace gflat.VisualStudio;

[Guid("A48BDF16-DAF5-4E05-92BC-F9F89C923A91")]
public sealed class GflatOptions : DialogPage
{
    [Category("New files"), DisplayName("Generate a class for PascalCase filenames"), DefaultValue(true)]
    [Description("Create a class matching a PascalCase source filename. Snake_case filenames create no type declaration.")]
    public bool GenerateClassFromFileName { get; set; } = true;

    [Category("New files"), DisplayName("Generate a namespace"), DefaultValue(true)]
    [Description("Use the project's default namespace and the new file's folders relative to its namespace root.")]
    public bool GenerateNamespace { get; set; } = true;

    [Category("Quick Info"), DisplayName("Show known exceptions"), DefaultValue(false)]
    [Description("Show exceptions identified by analysis in method tooltips. The list is not exhaustive.")]
    public bool ShowKnownExceptions { get; set; }

    [Category("Quick Info"), DisplayName("Show type size and alignment"), DefaultValue(true)]
    [Description("Show size and alignment when hovering over a type.")]
    public bool ShowTypeLayout { get; set; } = true;

    [Category("Quick Info"), DisplayName("Show language construct explanations"), DefaultValue(false)]
    [Description("Show brief explanations when hovering over language keywords and preprocessor directives.")]
    public bool ShowLanguageConstructs { get; set; }

    [Category("Formatting"), DisplayName("Opening braces on a new line"), DefaultValue(true)]
    public bool BracesOnNewLine { get; set; } = true;

    internal event Action? Changed;
    internal object Snapshot() => new { gflat = new { showKnownExceptions = ShowKnownExceptions, showTypeLayout = ShowTypeLayout, showLanguageConstructs = ShowLanguageConstructs, bracesOnNewLine = BracesOnNewLine } };

    public override void SaveSettingsToStorage()
    {
        base.SaveSettingsToStorage();
        Changed?.Invoke();
    }
}
