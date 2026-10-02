using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("*", "0.0.1", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("^1.2.0", "1.9.9", true)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("^1.2.0", "1.1.9", false)]
    [InlineData("^0.1.0", "0.1.7", true)]
    [InlineData("^0.1.0", "0.2.0", false)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("~1.2.0", "1.2.9", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0", true)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
    [InlineData(">0.1.0", "0.1.0", false)]
    [InlineData("<=0.1.0", "0.1.0", true)]
    public void Contains(string range, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(range, out VersionRange? parsed));
        Assert.True(ModuleVersion.TryParse(version, out ModuleVersion parsedVersion));
        Assert.Equal(expected, parsed!.Contains(parsedVersion));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.x")]
    [InlineData("^1.2")]
    [InlineData("01.2.3")]
    [InlineData(">= 1.0.0")]
    [InlineData("1.2.3-beta")]
    public void RejectsMalformedRanges(string range)
    {
        Assert.False(VersionRange.TryParse(range, out _));
    }
}
