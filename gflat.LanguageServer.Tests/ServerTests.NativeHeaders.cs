using System.Text.Json;
using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeHeaderSearchChangesRefreshDiagnosticsWithoutDocumentEdits(bool headerDirectoryExists)
    {
        await using var session = new Session();
        Directory.CreateDirectory(session.File(headerDirectoryExists ? "obj/native-first/headers" : "obj/native-first"));
        Directory.CreateDirectory(session.File("native-second/headers"));
        System.IO.File.WriteAllText(session.File("native-second/headers/api.h"), "int FromHeader(void);");
        System.IO.File.WriteAllText(session.File("app.gfproj"), ProjectFileFixture.Xml(content:
            "<ItemGroup><IncludeDirectory Include=\"obj/native-first\"/><IncludeDirectory Include=\"native-second\"/></ItemGroup>"));
        const string text = "#lang c\n#include <headers/api.h>\n#lang gflat\nint main() => FromHeader();";
        System.IO.File.WriteAllText(session.File("main.gf"), text);
        await session.Initialize();
        await session.Open("main.gf", text);
        Assert.Empty((await session.Diagnostics("main.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        Directory.CreateDirectory(session.File("obj/native-first/headers"));
        System.IO.File.WriteAllText(session.File("obj/native-first/headers/api.h"), "int Different(void);");
        await session.Receive(message => IsMainDiagnostics(message) && message.GetProperty("params").GetProperty("diagnostics")
            .EnumerateArray().Any(diagnostic => diagnostic.GetProperty("message").GetString()!.Contains("FromHeader")));
        System.IO.File.Delete(session.File("obj/native-first/headers/api.h"));
        await session.Receive(message => IsMainDiagnostics(message) && message.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        await session.Stop();

        bool IsMainDiagnostics(JsonElement message) => message.TryGetProperty("method", out var method) &&
            method.GetString() == "textDocument/publishDiagnostics" &&
            message.GetProperty("params").GetProperty("uri").GetString() == session.Uri("main.gf");
    }
}
