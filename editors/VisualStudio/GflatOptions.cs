using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace gflat.VisualStudio;

[Guid("A48BDF16-DAF5-4E05-92BC-F9F89C923A91")]
public sealed class GflatOptions : DialogPage
{
    [Category("Quick Info"), DisplayName("Show known exceptions"), DefaultValue(false)]
    [Description("Show exceptions identified by analysis in method tooltips. The list is not exhaustive.")]
    public bool ShowKnownExceptions { get; set; }

    [Category("Quick Info"), DisplayName("Show type size and alignment"), DefaultValue(true)]
    [Description("Show size and alignment when hovering over a type.")]
    public bool ShowTypeLayout { get; set; } = true;

    internal event Action? Changed;
    internal object Snapshot() => new { gflat = new { showKnownExceptions = ShowKnownExceptions, showTypeLayout = ShowTypeLayout } };

    public override void SaveSettingsToStorage()
    {
        base.SaveSettingsToStorage();
        Changed?.Invoke();
    }
}
