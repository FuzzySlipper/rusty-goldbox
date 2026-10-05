using System.Text.Json;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

public sealed class CommandFeedbackTests
{
    [Fact]
    public void UnknownAuthorBuildNamesWorkspaceBuildInText()
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(["author", "build", "campaign"], output, Rules.RepositoryRoot);

        Assert.Equal(GoldboxCli.Usage, code);
        Assert.Contains("Unknown command 'author build'", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("goldbox workspace build <path>", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAuthorBuildNamesWorkspaceBuildInJson()
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(["author", "build", "campaign", "--json"], output, Rules.RepositoryRoot);

        Assert.Equal(GoldboxCli.Usage, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("usage", diagnostic.GetProperty("rule").GetString());
        Assert.Contains("goldbox workspace build <path>", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
