namespace RustyGoldbox.Core.Definitions;

/// <summary>One proportion of the Game's panel layout a skin or the player may set.</summary>
public sealed record SkinLayoutPart(string Name, string Label, double Minimum, double Maximum, double Default, string Description);

/// <summary>
/// The proportions of the Game's panels: how much room the log, the side
/// column, text, controls and portraits get, and at which window shapes the
/// arrangement changes. A skin may give its own; the player may override them.
/// These are presentation settings, not game rules.
/// </summary>
public static class SkinLayout
{
    public static IReadOnlyList<SkinLayoutPart> Parts { get; } =
    [
        new("log_share", "Log share", 0.15, 0.7, 0.36, "How much of the main column the text log under the view takes"),
        new("side_width", "Side column", 18, 50, 32, "Width of the portraits, map and controls column, in percent of the window"),
        new("text_scale", "Text scale", 0.6, 2, 1, "How large text is, relative to the window"),
        new("control_scale", "Controls scale", 0.6, 2.5, 1, "How large the movement pad and command buttons are"),
        new("portrait_scale", "Portrait size", 0.5, 2.5, 1, "How large portrait cards are"),
        new("ultrawide_from", "Ultrawide from", 1.5, 4, 2.1, "The window aspect ratio (width / height) from which portraits get their own column"),
        new("tall_below", "Tall below", 0.5, 1.5, 1.1, "The window aspect ratio below which the panels stack in one column"),
    ];

    /// <summary>The defaults with the given values laid over them.</summary>
    public static Dictionary<string, double> Over(IReadOnlyDictionary<string, double>? values)
    {
        Dictionary<string, double> layout = Parts.ToDictionary(part => part.Name, part => part.Default);
        foreach ((string name, double value) in values ?? new Dictionary<string, double>())
        {
            layout[name] = value;
        }

        return layout;
    }

    /// <summary>What is wrong with the given values, by part name: unknown parts, values out of range, and arrangements that overlap.</summary>
    public static List<(string Part, string Problem)> Problems(IReadOnlyDictionary<string, double> values)
    {
        List<(string, string)> problems = [];
        foreach ((string name, double value) in values)
        {
            if (Parts.FirstOrDefault(part => part.Name == name) is not SkinLayoutPart part)
            {
                problems.Add((name, $"'{name}' is not a layout part; parts are {string.Join(", ", Parts.Select(entry => entry.Name))}."));
            }
            else if (double.IsNaN(value) || value < part.Minimum || value > part.Maximum)
            {
                problems.Add((name, $"{name} is {value}; it must be from {part.Minimum} to {part.Maximum}."));
            }
        }

        Dictionary<string, double> layout = Over(values);
        if (layout["tall_below"] >= layout["ultrawide_from"])
        {
            string at = values.ContainsKey("tall_below") ? "tall_below" : "ultrawide_from";
            problems.Add((at, $"tall_below ({layout["tall_below"]}) must be under ultrawide_from ({layout["ultrawide_from"]}), or no window would use the standard arrangement."));
        }

        return problems;
    }
}
