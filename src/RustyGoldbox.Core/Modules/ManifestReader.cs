using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

/// <summary>Reads and checks one module's <c>module.json</c>.</summary>
public static class ManifestReader
{
    public const string FileName = "module.json";
    public const int CurrentFormat = 1;

    /// <summary>Every manifest field and what it accepts, in file order.</summary>
    public static IReadOnlyList<(string Name, string Description)> Fields { get; } =
    [
        ("format", $"number: the manifest format, currently {CurrentFormat}"),
        ("id", $"string: {ModuleIds.FormatDescription}"),
        ("kind", $"string: one of {string.Join(", ", ModuleKinds.Names)}"),
        ("version", $"string: {ModuleVersion.FormatDescription}"),
        ("title", "string: a non-empty display title"),
        ("requires", "array of { \"id\": <module id>, \"version\": <version range> }; may be empty"),
        ("provenance", "string: where the content comes from and under which license, for example \"Original content.\""),
    ];

    private static readonly string[] RequirementFields = ["id", "version"];

    /// <summary>
    /// Reads <c>module.json</c> from <paramref name="moduleDirectory"/>. Every
    /// problem is added to <paramref name="diagnostics"/>; the manifest is
    /// returned only when there were none.
    /// </summary>
    public static ModuleManifest? Read(string moduleDirectory, List<ModuleDiagnostic> diagnostics)
    {
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(moduleDirectory));
        string path = Path.Combine(directory, FileName);
        if (!Directory.Exists(directory))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "module.not-found",
                $"There is no directory at {directory}. Pass the path of a module directory (the one containing {FileName}).",
                File: directory));
            return null;
        }

        if (!File.Exists(path))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "manifest.missing",
                $"The directory has no {FileName}. Create one with `goldbox module new <kind> <id>`.",
                File: path));
            return null;
        }

        using JsonDocument? document = JsonFiles.Parse(path, null, diagnostics);
        if (document is null)
        {
            return null;
        }

        return new Reader(directory, path, diagnostics).Read(document.RootElement);
    }

    private sealed class Reader(string directory, string path, List<ModuleDiagnostic> diagnostics)
    {
        private readonly int _errorsBefore = diagnostics.Count;
        private string? _module;

        public ModuleManifest? Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Error("manifest.type", "$", $"{FileName} must contain a JSON object, but it contains {JsonFiles.Describe(root.ValueKind)}.");
                return null;
            }

            // Read the ID first so every later diagnostic can name the module.
            string? id = ReadString(root, "id");
            if (id is not null && ModuleIds.IsValid(id))
            {
                _module = id;
            }

            CheckUnknownFields(root);
            int? format = ReadFormat(root);
            id = CheckId(id);
            ModuleKind? kind = ReadKind(root);
            ModuleVersion? version = ReadVersion(root);
            string? title = ReadNonEmpty(root, "title");
            List<ModuleRequirement> requires = ReadRequires(root, id);
            string? provenance = ReadNonEmpty(root, "provenance");

            if (diagnostics.Count > _errorsBefore)
            {
                return null;
            }

            return new ModuleManifest(directory, format!.Value, id!, kind!.Value, version!.Value, title!, requires, provenance!);
        }

        private void CheckUnknownFields(JsonElement root)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!Fields.Any(field => field.Name == property.Name))
                {
                    Error(
                        "manifest.unknown-field",
                        $"$.{property.Name}",
                        $"'{property.Name}' is not a manifest field. Fields are: {string.Join(", ", Fields.Select(field => field.Name))}.");
                }
            }
        }

        private int? ReadFormat(JsonElement root)
        {
            if (!TryGetField(root, "format", JsonValueKind.Number, out JsonElement value))
            {
                return null;
            }

            if (!value.TryGetInt32(out int format) || format != CurrentFormat)
            {
                Error("manifest.format", "$.format", $"Format {value.GetRawText()} is not supported. Use \"format\": {CurrentFormat}.");
                return null;
            }

            return format;
        }

        private string? CheckId(string? id)
        {
            if (id is null)
            {
                return null;
            }

            if (!ModuleIds.IsValid(id))
            {
                Error("manifest.id", "$.id", $"'{id}' is not a valid module ID. Use {ModuleIds.FormatDescription}.");
                return null;
            }

            return id;
        }

        private ModuleKind? ReadKind(JsonElement root)
        {
            string? text = ReadString(root, "kind");
            if (text is null)
            {
                return null;
            }

            if (!ModuleKinds.TryParse(text, out ModuleKind kind))
            {
                Error("manifest.kind", "$.kind", $"'{text}' is not a module kind. Use one of {string.Join(", ", ModuleKinds.Names)}.");
                return null;
            }

            return kind;
        }

        private ModuleVersion? ReadVersion(JsonElement root)
        {
            string? text = ReadString(root, "version");
            if (text is null)
            {
                return null;
            }

            if (!ModuleVersion.TryParse(text, out ModuleVersion version))
            {
                Error("manifest.version", "$.version", $"'{text}' is not a module version. Use {ModuleVersion.FormatDescription}.");
                return null;
            }

            return version;
        }

        private List<ModuleRequirement> ReadRequires(JsonElement root, string? ownId)
        {
            List<ModuleRequirement> requires = [];
            if (!TryGetField(root, "requires", JsonValueKind.Array, out JsonElement array))
            {
                return requires;
            }

            int index = 0;
            foreach (JsonElement entry in array.EnumerateArray())
            {
                ModuleRequirement? requirement = ReadRequirement(entry, index, ownId, requires);
                if (requirement is not null)
                {
                    requires.Add(requirement);
                }

                index++;
            }

            return requires;
        }

        private ModuleRequirement? ReadRequirement(JsonElement entry, int index, string? ownId, List<ModuleRequirement> earlier)
        {
            string at = $"$.requires[{index}]";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                Error("manifest.field-type", at, $"Each requires entry must be an object like {{ \"id\": \"osric\", \"version\": \"^0.1.0\" }}, but this is {JsonFiles.Describe(entry.ValueKind)}.");
                return null;
            }

            foreach (JsonProperty property in entry.EnumerateObject())
            {
                if (!RequirementFields.Contains(property.Name))
                {
                    Error("manifest.unknown-field", $"{at}.{property.Name}", $"'{property.Name}' is not a requires field. Fields are: id, version.");
                }
            }

            string? id = ReadEntryString(entry, "id", at);
            string? rangeText = ReadEntryString(entry, "version", at);
            if (id is not null && !ModuleIds.IsValid(id))
            {
                Error("manifest.id", $"{at}.id", $"'{id}' is not a valid module ID. Use {ModuleIds.FormatDescription}.");
                id = null;
            }

            VersionRange? range = null;
            if (rangeText is not null && !VersionRange.TryParse(rangeText, out range))
            {
                Error("requires.range", $"{at}.version", $"'{rangeText}' is not a version range. Use {VersionRange.FormatDescription}.");
            }

            if (id is null || range is null)
            {
                return null;
            }

            if (id == ownId)
            {
                Error("requires.self", $"{at}.id", $"A module can't require itself. Remove this entry.");
                return null;
            }

            ModuleRequirement? duplicate = earlier.FirstOrDefault(requirement => requirement.Id == id);
            if (duplicate is not null)
            {
                Error("requires.duplicate", $"{at}.id", $"'{id}' is already required at $.requires[{duplicate.Index}]. List each module once; combine ranges like \">=1.0.0 <2.0.0\".");
                return null;
            }

            return new ModuleRequirement(id, range, index);
        }

        private string? ReadEntryString(JsonElement entry, string name, string at)
        {
            if (!entry.TryGetProperty(name, out JsonElement value))
            {
                Error("manifest.field-required", at, $"This requires entry has no \"{name}\". Write it like {{ \"id\": \"osric\", \"version\": \"^0.1.0\" }}.");
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Error("manifest.field-type", $"{at}.{name}", $"\"{name}\" must be a string, but it is {JsonFiles.Describe(value.ValueKind)}.");
                return null;
            }

            return value.GetString();
        }

        private string? ReadNonEmpty(JsonElement root, string name)
        {
            string? text = ReadString(root, name);
            if (text is not null && text.Trim().Length == 0)
            {
                Error("manifest.field-empty", $"$.{name}", $"\"{name}\" must not be empty. Expected {Describe(name)}.");
                return null;
            }

            return text;
        }

        private string? ReadString(JsonElement root, string name)
        {
            return TryGetField(root, name, JsonValueKind.String, out JsonElement value) ? value.GetString() : null;
        }

        private bool TryGetField(JsonElement root, string name, JsonValueKind kind, out JsonElement value)
        {
            if (!root.TryGetProperty(name, out value))
            {
                Error("manifest.field-required", "$", $"Missing required field \"{name}\" ({Describe(name)}).");
                return false;
            }

            if (value.ValueKind != kind)
            {
                Error("manifest.field-type", $"$.{name}", $"\"{name}\" must be {Describe(name)}, but it is {JsonFiles.Describe(value.ValueKind)}.");
                return false;
            }

            return true;
        }

        private static string Describe(string field)
        {
            return Fields.First(entry => entry.Name == field).Description;
        }

        private void Error(string rule, string jsonPath, string message)
        {
            diagnostics.Add(new ModuleDiagnostic(rule, message, _module, path, jsonPath));
        }
    }
}
