using System;
using System.Linq;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Text.Adornments;
using Newtonsoft.Json.Linq;

namespace gflat.VisualStudio;

// Convert the server's presentation data into VS's own tooltip objects. Actions
// must be attached here: the LSP ClassifiedTextRun converter drops callbacks.
public static class HoverContent
{
    public static object Create(JToken node, Action<string, int, int> navigate)
    {
        switch ((string?)node["_vs_type"])
        {
            case "ContainerElement":
                return new ContainerElement((ContainerElementStyle)(int)node["Style"]!,
                    node["Elements"]!.Select(n => Create(n, navigate)).ToArray());
            case "ImageElement":
                return new ImageElement(new ImageId(Guid.Parse((string)node["ImageId"]!["Guid"]!), (int)node["ImageId"]!["Id"]!), (string)node["AutomationName"]!);
            case "ClassifiedTextElement":
                return new ClassifiedTextElement(node["Runs"]!.Select(run =>
                {
                    string kind = (string)run["ClassificationTypeName"]!;
                    string text = (string)run["Text"]!;
                    var style = (ClassifiedTextRunStyle)(int)run["Style"]!;
                    var target = run["_gflat_target"];
                    if (target is JObject && Uri.TryCreate((string?)target["uri"], UriKind.Absolute, out var uri) && uri.IsFile)
                    {
                        int line = (int)target["range"]!["start"]!["line"]!;
                        int column = (int)target["range"]!["start"]!["character"]!;
                        if (line >= 0 && column >= 0)
                            return new ClassifiedTextRun(kind, text, () => navigate(uri.LocalPath, line, column), "Go to definition", style);
                    }
                    return new ClassifiedTextRun(kind, text, style);
                }).ToArray());
            default:
                throw new ArgumentException("Unknown hover content element");
        }
    }
}
