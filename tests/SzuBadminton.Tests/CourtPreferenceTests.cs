using SzuBadminton.Services;
using Xunit;

namespace SzuBadminton.Tests;

public sealed class CourtPreferenceTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("全部场地", "")]
    [InlineData(" 至畅5号场 ", "至畅5号场")]
    public void Preference_NormalizesAllCourtsAndCustomCourt(string input, string expected)
    {
        Assert.Equal(expected, CourtPreference.Normalize(input));
    }

    [Fact]
    public void Options_ProvideKnownVenueGroupsForEachCampus()
    {
        Assert.Contains("运动广场东馆羽毛球场", CourtPreference.GetOptions("粤海校区"));
        Assert.Contains("至畅体育馆", CourtPreference.GetOptions("丽湖校区"));
        Assert.Contains("至快体育馆", CourtPreference.GetOptions("丽湖校区"));
    }

    [Fact]
    public void Options_AppendLiveCourtNamesWithoutDuplicates()
    {
        var options = CourtPreference.GetOptions(
            "丽湖校区",
            ["至畅5号场", "至快10号场", "至畅5号场"]);

        Assert.Contains("至畅5号场", options);
        Assert.Contains("至快10号场", options);
        Assert.Equal(options.Count, options.Distinct().Count());
    }
}
