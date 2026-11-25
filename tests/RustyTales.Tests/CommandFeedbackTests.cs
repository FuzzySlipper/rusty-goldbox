using System.Text.Json;
using RustyTales.Cli;

namespace RustyTales.Tests;

public sealed class CommandFeedbackTests
{
    [Fact]
    public void UnknownAuthorBuildNamesWorkspaceBuildInText()
    {
        using StringWriter output = new();
        int code = TalesCli.Run(["author", "build", "campaign"], output, Rules.RepositoryRoot);

        Assert.Equal(TalesCli.Usage, code);
        Assert.Contains("Unknown command 'author build'", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("tales workspace build <path>", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAuthorBuildNamesWorkspaceBuildInJson()
    {
        using StringWriter output = new();
        int code = TalesCli.Run(["author", "build", "campaign", "--json"], output, Rules.RepositoryRoot);

        Assert.Equal(TalesCli.Usage, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("usage", diagnostic.GetProperty("rule").GetString());
        Assert.Contains("tales workspace build <path>", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
