namespace RustyTales.Core.Authoring;

using RustyTales.Core.Modules;

/// <summary>Creates the small, editable workspace layout used by agents.</summary>
public static class WorkspaceScaffold
{
    private const string Manifest = """
        {
          "modules": ["modules"],
          "authoring": {
            "modules": [],
            "staging": ".tales/staged",
            "exports": "exports"
          }
        }

        """;

    /// <summary>
    /// Creates <paramref name="directory"/> and its conventional editable and
    /// generated directories. When <paramref name="kitId"/> is given, also
    /// scaffolds that genre starter kit's ruleset, starter art, campaign,
    /// smoke script and kit guide. Existing files are preserved; an existing
    /// <c>tales.json</c> is an error.
    /// </summary>
    public static Workspace? Create(
        string directory,
        List<ModuleDiagnostic> diagnostics,
        string? kitId = null,
        string? campaignId = null,
        string? title = null)
    {
        GenreKit? kit = null;
        if (kitId is not null)
        {
            kit = GenreKits.Find(kitId);
            if (kit is null)
            {
                string known = string.Join(", ", GenreKits.All.Select(entry => entry.Id));
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.kit",
                    $"'{kitId}' is not a genre starter kit. Kits: {known}. Run `tales schema kits` for details."));
                return null;
            }
        }

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

            string? resolvedCampaignId = null;
            string? resolvedTitle = null;
            if (kit is not null)
            {
                resolvedCampaignId = campaignId ?? GenreKits.DefaultCampaignId(root, kit);
                resolvedTitle = string.IsNullOrWhiteSpace(title) ? GenreKits.DefaultTitle(resolvedCampaignId, kit) : title.Trim();
                if (!ModuleIds.IsValid(resolvedCampaignId)
                    || !ModuleIds.IsValid($"{resolvedCampaignId}-rules")
                    || !ModuleIds.IsValid($"{resolvedCampaignId}-art"))
                {
                    diagnostics.Add(new ModuleDiagnostic(
                        "workspace.kit.id",
                        $"'{resolvedCampaignId}' is not a valid module ID. Use {ModuleIds.FormatDescription}."));
                    return null;
                }
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

            foreach (string relative in Workspace.EditableDirectoryNames.Append("modules").Append(".tales/staged").Append("exports"))
            {
                Directory.CreateDirectory(Path.Combine(root, relative));
            }

            string manifestText = kit is null
                ? Manifest
                : $$"""
                    {
                      "modules": ["modules"],
                      "authoring": {
                        "modules": [
                          "modules/{{resolvedCampaignId}}-rules",
                          "modules/{{resolvedCampaignId}}-art",
                          "modules/{{resolvedCampaignId}}"
                        ],
                        "staging": ".tales/staged",
                        "exports": "exports"
                      }
                    }

                    """;
            File.WriteAllText(manifest, manifestText);

            if (kit is not null)
            {
                GenreKits.Scaffold(kit, root, resolvedCampaignId!, resolvedTitle!, diagnostics);
                if (diagnostics.Count > 0)
                {
                    return null;
                }
            }

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
