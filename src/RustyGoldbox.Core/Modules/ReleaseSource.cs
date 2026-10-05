using System.Text.RegularExpressions;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Where a module's releases are published: a GitHub repository whose
/// releases carry <c>module-index.json</c> and the containers it lists, or the
/// URL of one such index. A manifest's <c>releases</c> field and each
/// <c>requires</c> entry's name one; <c>goldbox module get</c> also takes a
/// local directory of indexes.
/// </summary>
public sealed partial record ReleaseSource
{
    public const string FormatDescription =
        "\"github:<owner>/<repo>\" (the repository's GitHub releases) or an https:// URL of a module-index.json";

    private ReleaseSource(string text, string? owner, string? repository, Uri? index, string? directory)
    {
        Text = text;
        Owner = owner;
        Repository = repository;
        Index = index;
        Directory = directory;
    }

    /// <summary>The source as written.</summary>
    public string Text { get; }

    /// <summary>The GitHub owner, for a <c>github:</c> source.</summary>
    public string? Owner { get; }

    /// <summary>The GitHub repository name, for a <c>github:</c> source.</summary>
    public string? Repository { get; }

    /// <summary>The index URL, for a URL source.</summary>
    public Uri? Index { get; }

    /// <summary>A local directory holding <c>module-index.json</c> (or subdirectories that do), for <c>module get</c>.</summary>
    public string? Directory { get; }

    /// <summary>Reads a manifest's <c>releases</c> value: a <c>github:</c> source or an https URL.</summary>
    public static bool TryParse(string text, out ReleaseSource? source)
    {
        source = null;
        if (text.StartsWith("github:", StringComparison.Ordinal))
        {
            Match match = GitHubName().Match(text["github:".Length..]);
            if (match.Success)
            {
                source = new ReleaseSource(text, match.Groups[1].Value, match.Groups[2].Value, null, null);
            }

            return source is not null;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && uri.AbsolutePath.EndsWith(".json", StringComparison.Ordinal))
        {
            source = new ReleaseSource(text, null, null, uri, null);
        }

        return source is not null;
    }

    /// <summary>Reads a source given to <c>module get</c>: anything a manifest accepts, or an existing local directory.</summary>
    public static bool TryParseArgument(string text, string workingDirectory, out ReleaseSource? source)
    {
        if (TryParse(text, out source))
        {
            return true;
        }

        string path = Path.GetFullPath(text, workingDirectory);
        source = System.IO.Directory.Exists(path) ? new ReleaseSource(text, null, null, null, path) : null;
        return source is not null;
    }

    public override string ToString() => Text;

    [GeneratedRegex("^([A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/([A-Za-z0-9._-]{1,100})$")]
    private static partial Regex GitHubName();
}
