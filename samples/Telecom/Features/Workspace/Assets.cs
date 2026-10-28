using MinApiLib.Endpoints;

namespace Telecom.Features.Workspace;

public sealed record WorkspacePageEndpoint() : Get("/")
{
    public static IResult HandleAsync() => WorkspaceAssets.Page();
}

public sealed record WorkspaceAssetEndpoint() : Get("/workspace/{name}")
{
    public static IResult HandleAsync(string name) => WorkspaceAssets.Asset(name);
}

internal static class WorkspaceAssets
{
    private static readonly byte[] Html = Read("index.html");
    private static readonly byte[] App = Read("app.js");
    private static readonly byte[] Rendering = Read("render.js");
    private static readonly byte[] Api = Read("api.js");
    private static readonly byte[] Css = Read("styles.css");

    public static IResult Page() => Results.Bytes(Html, "text/html; charset=utf-8");

    public static IResult Asset(string name) => name switch
    {
        "app.js" => Results.Bytes(App, "text/javascript; charset=utf-8"),
        "render.js" => Results.Bytes(Rendering, "text/javascript; charset=utf-8"),
        "api.js" => Results.Bytes(Api, "text/javascript; charset=utf-8"),
        "styles.css" => Results.Bytes(Css, "text/css; charset=utf-8"),
        _ => Results.NotFound(),
    };

    private static byte[] Read(string name)
    {
        using var stream = typeof(WorkspaceAssets).Assembly.GetManifestResourceStream($"Telecom.Features.Workspace.Ui.{name}")
            ?? throw new InvalidOperationException($"Missing workspace asset: {name}.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
