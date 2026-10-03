using System.Text.Json;

namespace RustyGoldbox.Core.Definitions;

/// <summary>
/// What an asset is (its media) and where it may be used (a slot). An asset
/// declares only its media; a reference names the slot it fills, and the
/// slot decides which media fit. A new media type is added here once and
/// every slot that accepts it takes it.
/// </summary>
public static class Media
{
    /// <summary>A place a definition uses an asset, and what it accepts there.</summary>
    /// <param name="Accepts">The media that fit, with any fields the use needs.</param>
    public sealed record Slot(string Name, string Description, string Accepts);

    /// <summary>The media an asset can be.</summary>
    public static IReadOnlyList<string> Types { get; } = ["image", "sheet", "audio"];

    /// <summary>The audio formats the Engine decodes, by the bytes a file starts with.</summary>
    public static IReadOnlyList<string> AudioFormats { get; } = ["Ogg (Vorbis or Opus)", "WAV", "FLAC"];

    /// <summary>The fields only a sheet has.</summary>
    public static IReadOnlyList<string> SheetFields { get; } = ["frame_size", "frame_count", "animations", "faces", "anchor", "height"];

    /// <summary>The regions a wall set draws an area with; the first two are required.</summary>
    public static IReadOnlyList<string> WallRegions { get; } = ["wall", "door", "floor", "ceiling"];

    public static IReadOnlyList<Slot> Slots { get; } =
    [
        new("picture", "A picture shown whole: a cell's backdrop, a character's portrait, an icon in a list.",
            "any visual media: an image, or a sheet (which plays its first animation, or shows its first frame)"),
        new("figure", "Something standing in the 3D view or in combat: a monster's or class's figure, a cell's prop, a terrain feature.",
            "a sheet with faces and height, so it can stand on the floor and be flipped to face either way"),
        new("wall_set", "The textures the first-person view builds an area from.",
            "an image with wall and door regions (and optionally floor and ceiling)"),
        new("border", "A border or button face cut into nine by a skin's slice.", "an image, drawn whole (no frames, no animation)"),
        new("sound", "A sound played once: an event's sound effect.", "audio"),
        new("music", "Music that loops until something else changes it: an event's music.", "audio"),
    ];

    public static Slot? FindSlot(string name) => Slots.FirstOrDefault(slot => slot.Name == name);

    public static string MediaOf(Definition asset) => asset.Json.GetProperty("media").GetString()!;

    /// <summary>Why <paramref name="asset"/> can't fill <paramref name="slot"/>, or null when it can.</summary>
    public static string? Problem(string slot, Definition asset)
    {
        JsonElement json = asset.Json;
        string media = MediaOf(asset);
        if (slot is "sound" or "music")
        {
            return media == "audio" ? null : $"{asset.QualifiedId} is a picture ({media}), but a {slot} is audio.";
        }

        if (media == "audio")
        {
            return $"{asset.QualifiedId} is audio, but {(slot == "wall_set" ? "a wall set" : $"a {slot}")} is something seen.";
        }

        switch (slot)
        {
            case "picture":
                return null;
            case "border":
                return media == "image" ? null : $"{asset.QualifiedId} is a {media}, but a border is an image cut into nine.";
            case "figure":
                if (media != "sheet")
                {
                    return $"{asset.QualifiedId} is an {media}, but a figure is a sheet that stands in the world. Make it a sheet (one frame is fine) with frame_size, faces and height.";
                }

                string[] missing = new[] { "faces", "height" }.Where(field => !json.TryGetProperty(field, out _)).ToArray();
                return missing.Length == 0
                    ? null
                    : $"{asset.QualifiedId} is used as a figure, so it needs {string.Join(" and ", missing.Select(field => $"\"{field}\""))}: which way the art faces and how tall it stands in cells.";
            case "wall_set":
                if (media != "image")
                {
                    return $"{asset.QualifiedId} is a {media}, but a wall set is an image with wall and door regions.";
                }

                string[] regions = WallRegions.Take(2)
                    .Where(region => !json.TryGetProperty("regions", out JsonElement named) || !named.TryGetProperty(region, out _))
                    .ToArray();
                return regions.Length == 0
                    ? null
                    : $"{asset.QualifiedId} is used as a wall set, so it needs {string.Join(" and ", regions.Select(region => $"\"{region}\""))} in its \"regions\" (pixel rectangles in the image).";
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Not a media slot.");
        }
    }
}
