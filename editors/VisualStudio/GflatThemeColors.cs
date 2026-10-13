// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file under the MIT license (LICENSES/Roslyn.txt).
// Adapted from Roslyn's HACK_ThemeColorFixer.cs:
// https://github.com/dotnet/roslyn/blob/main/src/VisualStudio/Core/Def/HACK_ThemeColorFixer.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace gflat.VisualStudio;

// VS does not propagate theme colors to classifications that aren't registered
// as Fonts and Colors items in the tooltip category. Roslyn copies foregrounds
// from "text" to "tooltip", preserving the tooltip's font and other formatting.
[Export(typeof(IWpfTextViewConnectionListener))]
[ContentType("gflat")]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
public sealed class GflatThemeColors : IWpfTextViewConnectionListener
{
    private readonly IClassificationTypeRegistryService registry;
    private readonly IClassificationFormatMapService maps;
    private readonly IClassificationFormatMap textFormatMap;
    private bool connected;

    [ImportingConstructor]
    public GflatThemeColors(IClassificationTypeRegistryService registry, IClassificationFormatMapService maps)
    {
        this.registry = registry;
        this.maps = maps;
        textFormatMap = maps.GetClassificationFormatMap("text");
        // This MEF service lives for the lifetime of VS, like Roslyn's listener.
        textFormatMap.ClassificationFormatMappingChanged += TextFormatMapChanged;
    }

    private void TextFormatMapChanged(object sender, EventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh() => _ = VsTaskLibraryHelper.CreateAndStartTask(
        VsTaskLibraryHelper.ServiceInstance, VsTaskRunContext.UIThreadIdlePriority, RefreshThemeColors);

    public void RefreshThemeColors()
    {
        textFormatMap.ClassificationFormatMappingChanged -= TextFormatMapChanged;
        try
        {
            var source = maps.GetClassificationFormatMap("text");
            var target = maps.GetClassificationFormatMap("tooltip");
            foreach (string name in new[] {
                "comment", "excluded code", "identifier", "keyword", "keyword - control",
                "number", "string", "string - verbatim", "string - escape character",
                "operator", "punctuation", "class name", "struct name", "interface name",
                "enum name", "type parameter name", "type", "field name", "enum member name",
                "constant name", "local name", "parameter name", "method name", "property name", "namespace name"
            })
            {
                var type = registry.GetClassificationType(name);
                if (type == null) continue;
                var sourceProps = source.GetTextProperties(type);
                var targetProps = target.GetTextProperties(type);
                targetProps = targetProps.SetForegroundBrush(sourceProps.ForegroundBrush);
                target.SetTextProperties(type, targetProps);
            }
        }
        finally
        {
            textFormatMap.ClassificationFormatMappingChanged += TextFormatMapChanged;
        }
    }

    public void SubjectBuffersConnected(IWpfTextView textView, ConnectionReason reason, Collection<ITextBuffer> subjectBuffers)
    {
        if (connected) return;
        connected = true;
        // Roslyn schedules this after editor composition to avoid delaying it.
        ScheduleRefresh();
    }

    public void SubjectBuffersDisconnected(IWpfTextView textView, ConnectionReason reason, Collection<ITextBuffer> subjectBuffers) { }
}
