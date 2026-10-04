using System.Reflection;
using System.Text;

namespace RustyGoldbox.Cli;

internal sealed record AuthoringResource(
    string Id,
    string Title,
    string Description,
    string FileName,
    string Status);

/// <summary>Reads the copyable campaign-authoring resources embedded in goldbox.</summary>
internal static class AuthoringKit
{
    private const string ResourcePrefix = "RustyGoldbox.Cli.AuthoringKit.Resources.";

    private static readonly IReadOnlyList<AuthoringResource> ResourceList =
    [
        new("kit", "Campaign authoring kit", "Index and staged workflow for the draft kit.", "README.md", "draft"),
        new("brief", "Author brief", "Brief intake, visible acceptance facts, and repair path.", "brief.md", "draft"),
        new("canon", "Canon contract", "Shared canon, IDs, variables, ownership, and rights.", "canon.md", "draft"),
        new("chapter", "Chapter contract", "Area, scene, entry/exit, state, and merge contract.", "chapter.md", "draft"),
        new("encounter", "Encounter contract", "Ruleset-aware tuning, seeded replay, and repair.", "encounter.md", "draft"),
        new("art", "Art direction", "Style, provenance, accepted/rejected art, and drift review.", "art.md", "draft"),
        new("handoff", "Authoring handoff", "Files-first worker packet and contradiction handling.", "handoff.md", "draft"),
        new("workflow", "Authoring workflow", "Single-agent and multi-agent coordinator sequence.", "workflow.md", "draft"),
        new("revision", "Revision record", "Repair, deliberate canon changes, and evidence layers.", "revision.md", "draft"),
    ];

    public static IReadOnlyList<AuthoringResource> Resources => ResourceList;

    public static bool TryGet(string id, out AuthoringResource? resource)
    {
        resource = ResourceList.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return resource is not null;
    }

    public static bool TryRead(AuthoringResource resource, out string content)
    {
        Assembly assembly = typeof(AuthoringKit).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + resource.FileName);
        if (stream is null)
        {
            content = "";
            return false;
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        content = reader.ReadToEnd();
        return true;
    }
}
