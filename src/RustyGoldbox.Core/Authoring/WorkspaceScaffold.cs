namespace RustyGoldbox.Core.Authoring;

using RustyGoldbox.Core.Modules;

/// <summary>Creates the small, editable workspace layout used by agents.</summary>
public static class WorkspaceScaffold
{
    private const string Manifest = """
        {
          "modules": ["modules"],
          "authoring": {
            "modules": [],
            "staging": ".goldbox/staged",
            "exports": "exports"
          }
        }

        """;

    /// <summary>
    /// Creates <paramref name="directory"/> and its conventional editable and
    /// generated directories. Existing files are preserved; an existing
    /// <c>goldbox.json</c> is an error.
    /// </summary>
    public static Workspace? Create(string directory, List<ModuleDiagnostic> diagnostics)
    {
        string root;
        try
        {
            root = Path.GetFullPath(directory);
            if (File.Exists(root))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.create",
                    $"{root} is a file. Choose a directory for the workspace.",
                    File: root));
                return null;
            }

            Directory.CreateDirectory(root);
            string manifest = Path.Combine(root, Workspace.FileName);
            if (File.Exists(manifest))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.exists",
                    $"{manifest} already exists. Inspect it or choose another directory; workspace new never overwrites a manifest.",
                    File: manifest,
                    JsonPath: "$"));
                return null;
            }

            foreach (string relative in Workspace.EditableDirectoryNames.Append("modules").Append(".goldbox/staged").Append("exports"))
            {
                Directory.CreateDirectory(Path.Combine(root, relative));
            }

            File.WriteAllText(manifest, Manifest);
            return Workspace.Read(manifest, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.create",
                $"Can't create the workspace: {exception.Message} Check that the target directory is writable, or choose another path.",
                File: directory));
            return null;
        }
    }
}
