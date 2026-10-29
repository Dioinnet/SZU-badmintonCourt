namespace SzuBadminton.Services;

public static class CourtPreference
{
    public const string AllCourts = "全部场地";

    public static string Normalize(string? preference)
    {
        var value = preference?.Trim() ?? string.Empty;
        return value.Length == 0 || value == AllCourts ? string.Empty : value;
    }

    public static IReadOnlyList<string> GetOptions(string campus, IEnumerable<string>? liveCourtNames = null)
    {
        var options = new List<string> { AllCourts };
        if (campus.Contains("丽湖", StringComparison.Ordinal))
        {
            options.Add("至畅体育馆");
            options.Add("至快体育馆");
        }
        else
        {
            options.Add("运动广场东馆羽毛球场");
        }

        if (liveCourtNames is not null)
        {
            foreach (var name in liveCourtNames)
            {
                var value = name.Trim();
                if (value.Length > 0 && !options.Contains(value, StringComparer.Ordinal))
                {
                    options.Add(value);
                }
            }
        }

        return options;
    }
}
