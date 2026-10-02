namespace RustyGoldbox.Tests;

/// <summary>A temporary directory of module sources for one test.</summary>
internal sealed class TempModules : IDisposable
{
    public TempModules()
    {
        Root = Path.Combine(Path.GetTempPath(), "goldbox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Writes <c>&lt;Root&gt;/&lt;directory&gt;/module.json</c> and returns the module directory.</summary>
    public string Manifest(string directory, string json)
    {
        return Write(Path.Combine(directory, "module.json"), json);
    }

    /// <summary>Writes a manifest from the usual fields.</summary>
    public string Module(string id, string kind, string version = "0.1.0", string requires = "", string? directory = null)
    {
        return Manifest(directory ?? id, $$"""
            {
              "format": 1,
              "id": "{{id}}",
              "kind": "{{kind}}",
              "version": "{{version}}",
              "title": "{{id}}",
              "requires": [{{requires}}],
              "provenance": "Test fixture."
            }
            """);
    }

    /// <summary>Writes a file relative to <see cref="Root"/>; returns its directory.</summary>
    public string Write(string relativePath, string text)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return Path.GetDirectoryName(path)!;
    }

    public static string Require(string id, string range) => $$"""{ "id": "{{id}}", "version": "{{range}}" }""";

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
