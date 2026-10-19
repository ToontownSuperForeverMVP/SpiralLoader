using SpiralLoader.Api.Versioning;

namespace SpiralLoader.Tests;

public sealed class VersioningTests {

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null, null)]
    [InlineData("0.0.0", 0, 0, 0, null, null)]
    [InlineData("1.0.0-alpha.1", 1, 0, 0, "alpha.1", null)]
    [InlineData("1.0.0+build.5", 1, 0, 0, null, "build.5")]
    [InlineData("10.20.30-rc.1+sha.abc", 10, 20, 30, "rc.1", "sha.abc")]
    public void ParsesValidVersions(string text, int major, int minor, int patch, string? prerelease, string? build) {
        var version = SemanticVersion.Parse(text);
        Assert.Equal((major, minor, patch, prerelease, build), (version.Major, version.Minor, version.Patch, version.Prerelease, version.Build));
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3+")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.-3")]
    public void RejectsInvalidVersions(string text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void OrdersPerSemVerSpecification() {
        // Example ordering from semver.org section 11.
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
                            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"];
        var parsed = ordered.Select(SemanticVersion.Parse).ToList();
        var shuffled = parsed.OrderBy(v => v.GetHashCode() ^ 0x5bd1e995).ToList();
        shuffled.Sort();
        Assert.Equal(ordered, shuffled.Select(v => v.ToString()));
    }

    [Fact]
    public void BuildMetadataDoesNotAffectEquality()
        => Assert.Equal(SemanticVersion.Parse("1.0.0+a"), SemanticVersion.Parse("1.0.0+b"));

    [Theory]
    [InlineData("*", "0.0.1", true)]
    [InlineData("", "5.0.0", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData(">=1.2.0", "1.2.0", true)]
    [InlineData(">=1.2.0", "1.1.9", false)]
    [InlineData(">1.2.0", "1.2.0", false)]
    [InlineData(">1.2", "1.2.9", false)]
    [InlineData(">1.2", "1.3.0", true)]
    [InlineData("<2.0.0", "1.9.9", true)]
    [InlineData("<2", "2.0.0-beta", false)]
    [InlineData("<=1.2", "1.2.9", true)]
    [InlineData("<=1.2", "1.3.0", false)]
    [InlineData("^1.2.3", "1.9.0", true)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("^1.2.3", "2.0.0-alpha", false)]
    [InlineData("^1.2.3", "1.2.2", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("~1.2.3", "1.2.9", true)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData("~1", "1.9.9", true)]
    [InlineData("1.2.x", "1.2.7", true)]
    [InlineData("1.2.x", "1.3.0", false)]
    [InlineData("1.x", "1.99.0", true)]
    [InlineData("1", "2.0.0", false)]
    [InlineData(">=0.1.0 <0.2.0", "0.1.5", true)]
    [InlineData(">=0.1.0 <0.2.0", "0.2.0", false)]
    [InlineData("1.0.0 || >=3.0.0", "3.1.0", true)]
    [InlineData("1.0.0 || >=3.0.0", "2.0.0", false)]
    public void EvaluatesRanges(string range, string version, bool expected)
        => Assert.Equal(expected, VersionRange.Parse(range).IsSatisfiedBy(SemanticVersion.Parse(version)));

    [Theory]
    [InlineData(">=")]
    [InlineData("^abc")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.0.0 ||")]
    [InlineData(">*")]
    public void RejectsInvalidRanges(string range) => Assert.False(VersionRange.TryParse(range, out _));

}
