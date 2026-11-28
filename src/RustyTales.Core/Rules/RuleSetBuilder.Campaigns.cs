using System.Text.Json;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Expressions;
using RustyTales.Core.Modules;

namespace RustyTales.Core.Rules;

public sealed partial class RuleSetBuilder
{
    private void CheckCampaigns()
    {
        foreach (Definition variable in _rules.Variables.Values.Concat(_rules.AreaVariables.Values))
        {
            CheckValueType(variable, "$.initial", variable);
        }

        foreach (Definition definition in _rules.Definitions)
        {
            if (definition.Type == DefinitionTypes.Item)
            {
                CheckItem(definition);
            }
            else if (definition.Type == DefinitionTypes.Asset)
            {
                string file = definition.Json.GetProperty("file").GetString()!;
                ModuleSource source = _manifests[definition.Module].Source;
                if (file.Split('/').Any(part => part is "" or "." or "..") || file.Contains('\\', StringComparison.Ordinal) || file.Contains(':', StringComparison.Ordinal))
                {
                    Error(definition, "asset.file", "$.file", $"'{file}' must be a relative path inside the module, with forward slashes.");
                }
                else if (!source.Contains(file))
                {
                    Error(definition, "asset.file", "$.file", $"There is no file '{file}' in module '{definition.Module}' ({source.PathOf(file)}).");
                }
                else if (Media.MediaOf(definition) == "audio")
                {
                    CheckAudio(definition, source, file);
                }
                else
                {
                    CheckImage(definition, source, file);
                }
            }
            else if (definition.Type == DefinitionTypes.Figure)
            {
                CheckFigure(definition);
            }
            else if (definition.Type == DefinitionTypes.Campaign)
            {
                CheckEntry(definition, "$.start.area", definition.Json.GetProperty("start").GetProperty("entry").GetString()!, "$.start.entry");
                JsonElement party = definition.Json.GetProperty("party");
                int min = party.GetProperty("min").GetInt32();
                int max = party.GetProperty("max").GetInt32();
                if (min < 1 || max < min)
                {
                    Error(definition, "campaign.party", "$.party", $"Party size needs 1 <= min <= max, but min is {min} and max is {max}.");
                }

                if (definition.Json.TryGetProperty("starting_party", out JsonElement startingParty))
                {
                    int count = startingParty.GetArrayLength();
                    if (count < min || count > max)
                    {
                        Error(definition, "campaign.starting-party", "$.starting_party", $"starting_party has {count} member(s), outside the campaign's party size {min}..{max}.");
                    }

                    HashSet<Definition> seen = [];
                    for (int index = 0; index < count; index++)
                    {
                        string path = $"$.starting_party[{index}]";
                        if (_rules.References.TryGetValue((definition, path), out Definition? npc) && !seen.Add(npc))
                        {
                            Error(definition, "campaign.starting-party", path, $"Duplicate starting_party NPC '{npc.QualifiedId}'. Each starting member must be distinct.");
                        }
                    }
                }
            }
        }

        // After the loop above, so every asset's image size is known.
        foreach (Definition skin in _rules.OfType(DefinitionTypes.Skin))
        {
            CheckSkin(skin);
        }

        CheckTilesetsAndTilemaps();

        foreach (Definition evt in _rules.OfType(DefinitionTypes.Event))
        {
            CheckEvent(evt);
        }

        foreach (ModuleManifest module in _manifests.Values)
        {
            List<Definition> campaigns = _rules.OfType(DefinitionTypes.Campaign).Where(definition => definition.Module == module.Id).ToList();
            if (module.Kind == ModuleKind.Campaign && campaigns.Count != 1)
            {
                _diagnostics.Add(new ModuleDiagnostic("campaign.definition", campaigns.Count == 0
                    ? "A campaign module needs one campaign definition (see `tales schema campaign`)."
                    : $"A campaign module has exactly one campaign definition, but this one has {campaigns.Count}.", module.Id, module.ManifestPath));
            }
            else if (module.Kind != ModuleKind.Campaign)
            {
                foreach (Definition campaign in campaigns)
                {
                    Error(campaign, "campaign.definition", "$", $"Only campaign modules can define a campaign; '{module.Id}' is a {ModuleKinds.Name(module.Kind)}.");
                }
            }
        }
    }

    /// <summary>A skin is art: it lives in an assets or campaign module, its colours parse, and its slices fit their images.</summary>
    private void CheckSkin(Definition skin)
    {
        ModuleKind kind = _manifests[skin.Module].Kind;
        if (kind is not (ModuleKind.Assets or ModuleKind.Campaign))
        {
            Error(skin, "skin.module", "$", $"Skins are art, so they live in assets or campaign modules; '{skin.Module}' is a {ModuleKinds.Name(kind)}.");
        }

        if (skin.Json.TryGetProperty("colors", out JsonElement colors))
        {
            foreach (JsonProperty color in colors.EnumerateObject())
            {
                string at = $"$.colors.{color.Name}";
                if (!DefinitionTypes.SkinColors.Contains(color.Name))
                {
                    Error(skin, "skin.color", at, $"'{color.Name}' is not a skin colour; colours are {string.Join(", ", DefinitionTypes.SkinColors)}.");
                }
                else if (!IsColor(color.Value.GetString()!))
                {
                    Error(skin, "skin.color", at, $"'{color.Value.GetString()}' is not a colour; write #rgb, #rrggbb or #rrggbbaa.");
                }
            }
        }

        if (skin.Json.TryGetProperty("layout", out JsonElement layout))
        {
            Dictionary<string, double> values = layout.EnumerateObject().ToDictionary(part => part.Name, part => part.Value.GetDouble());
            foreach ((string part, string problem) in SkinLayout.Problems(values))
            {
                Error(skin, "skin.layout", $"$.layout.{part}", problem);
            }
        }

        foreach (string part in new[] { "frame", "button" })
        {
            string path = $"$.{part}.picture";
            if (!skin.Json.TryGetProperty(part, out JsonElement nine)
                || !_rules.References.TryGetValue((skin, path), out Definition? picture)
                || !_rules.ImageSizes.TryGetValue(picture, out (int Width, int Height) size))
            {
                continue;
            }

            int slice = nine.GetProperty("slice").GetInt32();
            if (slice < 1 || slice * 2 >= size.Width || slice * 2 >= size.Height)
            {
                Error(skin, "skin.slice", $"$.{part}.slice", $"A slice of {slice} doesn't cut the {size.Width} x {size.Height} image into nine; it must be at least 1 and under half of each side.");
            }
        }
    }

    private static bool IsColor(string text)
    {
        return text.Length is 4 or 7 or 9 && text[0] == '#' && text[1..].All(Uri.IsHexDigit);
    }

    /// <summary>Validates reusable 2D tilesets and compiles area/encounter tilemaps against their sheets and dimensions.</summary>
    private void CheckTilesetsAndTilemaps()
    {
        foreach (Definition tileset in _rules.OfType(DefinitionTypes.Tileset))
        {
            CheckTileset(tileset);
        }

        foreach (Definition area in _rules.OfType(DefinitionTypes.Area))
        {
            if (!area.Json.TryGetProperty("tilemap", out JsonElement tilemapEl))
            {
                continue;
            }

            int? expectedW = null;
            int? expectedH = null;
            if (area.Json.TryGetProperty("map", out JsonElement mapEl))
            {
                Campaigns.AreaMap? parsed = Campaigns.AreaMap.Parse(mapEl.EnumerateArray().Select(r => r.GetString()!).ToList(), []);
                if (parsed is not null)
                {
                    expectedW = parsed.Width;
                    expectedH = parsed.Height;
                }
            }

            if (CheckTilemap(area, tilemapEl, "$.tilemap", expectedW, expectedH) is Campaigns.ResolvedTilemap resolved)
            {
                _rules.AreaTilemaps[area] = resolved;
                if (!area.Json.TryGetProperty("map", out _))
                {
                    foreach (JsonProperty entry in area.Json.GetProperty("entries").EnumerateObject())
                    {
                        JsonElement at = entry.Value.GetProperty("at");
                        int ex = at[0].GetInt32();
                        int ey = at[1].GetInt32();
                        if (!resolved.IsPassable(ex, ey))
                        {
                            Error(area, "area.entry", $"$.entries.{entry.Name}.at",
                                $"Entry '{entry.Name}' at [{ex}, {ey}] lands on an impassable tile. Place entries on passable tiles.");
                        }
                    }

                    if (area.Json.TryGetProperty("doors", out JsonElement doors))
                    {
                        Campaigns.AreaMap synthesized = resolved.SynthesizeAreaMap();
                        int doorIdx = 0;
                        foreach (JsonElement door in doors.EnumerateArray())
                        {
                            int dx = door.GetProperty("at")[0].GetInt32();
                            int dy = door.GetProperty("at")[1].GetInt32();
                            Campaigns.Facing facing = Enum.Parse<Campaigns.Facing>(door.GetProperty("facing").GetString()!, ignoreCase: true);
                            Campaigns.AreaEdge edge = new Campaigns.AreaEdge(dx, dy, facing).Canonical;
                            if (!synthesized.ContainsEdge(edge.X, edge.Y, edge.Facing) || synthesized.EdgeOf(edge.X, edge.Y, edge.Facing) != Campaigns.Edge.Door)
                            {
                                Error(area, "area.door", $"$.doors[{doorIdx}].at",
                                    $"Door at [{dx}, {dy}] {Campaigns.Facings.Name(facing)} must border a tile with \"door\": true in the tilemap.");
                            }

                            doorIdx++;
                        }
                    }
                }
            }
        }

        List<Definition> fieldedCombats = _rules.OfType(DefinitionTypes.Combat)
            .Where(combat => combat.Json.TryGetProperty("field", out _))
            .ToList();
        foreach (Definition encounter in _rules.OfType(DefinitionTypes.Encounter))
        {
            if (!encounter.Json.TryGetProperty("tilemap", out JsonElement tilemapEl))
            {
                continue;
            }

            if (fieldedCombats.Count == 0)
            {
                Error(encounter, "encounter.tilemap", "$.tilemap", "An encounter tilemap needs a combat definition with a \"field\".");
                continue;
            }

            int? expectedW = null;
            int? expectedH = null;
            if (fieldedCombats.Count == 1)
            {
                JsonElement field = fieldedCombats[0].Json.GetProperty("field");
                expectedW = field.GetProperty("width").GetInt32();
                expectedH = field.GetProperty("height").GetInt32();
            }

            if (CheckTilemap(encounter, tilemapEl, "$.tilemap", expectedW, expectedH) is Campaigns.ResolvedTilemap resolved)
            {
                foreach (Definition combat in fieldedCombats)
                {
                    JsonElement field = combat.Json.GetProperty("field");
                    int cw = field.GetProperty("width").GetInt32();
                    int ch = field.GetProperty("height").GetInt32();
                    if (resolved.Width != cw || resolved.Height != ch)
                    {
                        Error(encounter, "encounter.tilemap", "$.tilemap.layers",
                            $"Combat '{combat.Id}' has a {cw} by {ch} field, but this encounter tilemap is {resolved.Width} by {resolved.Height}.");
                    }
                }

                _rules.EncounterTilemaps[encounter] = resolved;
            }
        }
    }

    private void CheckTileset(Definition tileset)
    {
        ModuleKind kind = _manifests[tileset.Module].Kind;
        if (kind is not (ModuleKind.Assets or ModuleKind.Extension or ModuleKind.Campaign))
        {
            Error(tileset, "tileset.module", "$", $"Tilesets live in assets, extension or campaign modules; '{tileset.Module}' is a {ModuleKinds.Name(kind)}.");
        }

        if (!_rules.References.TryGetValue((tileset, "$.sheet"), out Definition? sheet)
            || !_rules.ImageSizes.TryGetValue(sheet, out (int Width, int Height) imageSize)
            || !sheet.Json.TryGetProperty("frame_size", out JsonElement frameSize))
        {
            return;
        }

        int fw = frameSize[0].GetInt32();
        int fh = frameSize[1].GetInt32();
        if (fw < 1 || fh < 1 || imageSize.Width % fw != 0 || imageSize.Height % fh != 0)
        {
            return;
        }

        int cols = imageSize.Width / fw;
        int rows = imageSize.Height / fh;
        int frameCount = sheet.Json.TryGetProperty("frame_count", out JsonElement countEl)
            ? countEl.GetInt32()
            : cols * rows;
        if (frameCount < 1)
        {
            return;
        }

        JsonElement tilesEl = tileset.Json.GetProperty("tiles");
        if (!tilesEl.EnumerateObject().Any())
        {
            Error(tileset, "tileset.tiles", "$.tiles", "A tileset must declare at least one tile glyph.");
            return;
        }

        Dictionary<char, Campaigns.TileGlyph> glyphs = [];
        foreach (JsonProperty entry in tilesEl.EnumerateObject())
        {
            string at = $"$.tiles.{entry.Name}";
            if (entry.Name.Length != 1 || entry.Name[0] == ' ')
            {
                Error(tileset, "tileset.glyph", at, $"Tile glyph '{entry.Name}' must be a single non-space character (space ' ' is reserved for empty overlay cells).");
                continue;
            }

            char ch = entry.Name[0];
            JsonElement obj = entry.Value;
            string name = obj.GetProperty("name").GetString()!;
            int? tile = obj.TryGetProperty("tile", out JsonElement tileEl) ? tileEl.GetInt32() : null;
            if (tile is int t && (t < 0 || t >= frameCount))
            {
                Error(tileset, "tileset.frame", $"{at}.tile", $"Frame {t} is outside sheet '{sheet.QualifiedId}' (0..{frameCount - 1}).");
            }

            List<int> variants = [];
            if (obj.TryGetProperty("variants", out JsonElement varEl))
            {
                int vi = 0;
                foreach (JsonElement item in varEl.EnumerateArray())
                {
                    int v = item.GetInt32();
                    if (v < 0 || v >= frameCount)
                    {
                        Error(tileset, "tileset.frame", $"{at}.variants[{vi}]", $"Variant frame {v} is outside sheet '{sheet.QualifiedId}' (0..{frameCount - 1}).");
                    }

                    variants.Add(v);
                    vi++;
                }

                if (variants.Count == 0)
                {
                    Error(tileset, "tileset.variants", $"{at}.variants", "variants must contain at least one frame index, or omit it.");
                }
            }

            Campaigns.TileAutotileRule? autotile = null;
            if (obj.TryGetProperty("autotile", out JsonElement autoEl))
            {
                string mode = autoEl.GetProperty("mode").GetString()!;
                int? baseFrame = autoEl.TryGetProperty("base", out JsonElement baseEl) ? baseEl.GetInt32() : null;
                Dictionary<string, int> explicitFrames = new(StringComparer.Ordinal);
                if (autoEl.TryGetProperty("frames", out JsonElement framesEl))
                {
                    foreach (JsonProperty fp in framesEl.EnumerateObject())
                    {
                        int f = fp.Value.GetInt32();
                        if (f < 0 || f >= frameCount)
                        {
                            Error(tileset, "tileset.frame", $"{at}.autotile.frames.{fp.Name}", $"Frame {f} is outside sheet '{sheet.QualifiedId}' (0..{frameCount - 1}).");
                        }

                        explicitFrames[fp.Name] = f;
                    }
                }

                if (baseFrame is int bf)
                {
                    int needed = mode switch
                    {
                        "4bit" => 16,
                        "wall_top" => 2,
                        "blob47" => 47,
                        _ => 1,
                    };
                    if (bf < 0 || bf + needed > frameCount)
                    {
                        Error(tileset, "tileset.frame", $"{at}.autotile.base",
                            $"Autotile mode '{mode}' from base {bf} needs frames {bf}..{bf + needed - 1}, outside sheet '{sheet.QualifiedId}' (0..{frameCount - 1}).");
                    }
                }
                else if (explicitFrames.Count == 0)
                {
                    Error(tileset, "tileset.autotile", $"{at}.autotile", "Autotile needs \"base\" and/or \"frames\" specifying sheet frame indices.");
                }

                HashSet<char> connectsTo = [];
                if (autoEl.TryGetProperty("connects_to", out JsonElement connEl))
                {
                    int ci = 0;
                    foreach (JsonElement item in connEl.EnumerateArray())
                    {
                        string text = item.GetString()!;
                        if (text.Length != 1 || text[0] == ' ')
                        {
                            Error(tileset, "tileset.autotile", $"{at}.autotile.connects_to[{ci}]", $"connects_to entries must be single non-space glyphs, got '{text}'.");
                        }
                        else
                        {
                            connectsTo.Add(text[0]);
                        }

                        ci++;
                    }
                }

                bool oobSame = !autoEl.TryGetProperty("out_of_bounds", out JsonElement oobEl) || oobEl.GetString() != "empty";
                autotile = new Campaigns.TileAutotileRule(mode, baseFrame, explicitFrames, connectsTo, oobSame);
            }

            if (tile is null && variants.Count == 0 && autotile is null)
            {
                Error(tileset, "tileset.frame", at, $"Tile '{ch}' needs at least one of \"tile\", \"variants\", or \"autotile\".");
            }

            string? color = obj.TryGetProperty("color", out JsonElement colorEl) ? colorEl.GetString() : null;
            if (color is not null && !IsColor(color))
            {
                Error(tileset, "tileset.color", $"{at}.color", $"'{color}' is not a colour; write #rgb, #rrggbb or #rrggbbaa.");
            }

            bool passable = !obj.TryGetProperty("passable", out JsonElement passEl) || passEl.GetBoolean();
            bool door = obj.TryGetProperty("door", out JsonElement doorEl) && doorEl.GetBoolean();
            int cost = obj.TryGetProperty("cost", out JsonElement costEl) ? costEl.GetInt32() : 1;
            if (cost < 1)
            {
                Error(tileset, "tileset.cost", $"{at}.cost", "A tile's movement cost must be at least 1.");
                cost = 1;
            }

            bool blocksSight = obj.TryGetProperty("blocks_sight", out JsonElement sightEl) && sightEl.GetBoolean();
            glyphs[ch] = new Campaigns.TileGlyph(ch, name, tile, variants, autotile, passable, door, color, cost, blocksSight);
        }

        _rules.Tilesets[tileset] = new Campaigns.TilesetDefinition(
            tileset,
            sheet,
            imageSize.Width,
            imageSize.Height,
            fw,
            fh,
            cols,
            rows,
            frameCount,
            glyphs);
    }

    private Campaigns.ResolvedTilemap? CheckTilemap(Definition owner, JsonElement tilemapEl, string path, int? expectedWidth, int? expectedHeight)
    {
        if (!_rules.References.TryGetValue((owner, $"{path}.tileset"), out Definition? tilesetDef)
            || !_rules.Tilesets.TryGetValue(tilesetDef, out Campaigns.TilesetDefinition? tileset))
        {
            return null;
        }

        JsonElement layersEl = tilemapEl.GetProperty("layers");
        if (layersEl.GetArrayLength() == 0)
        {
            Error(owner, "tilemap.layers", $"{path}.layers", "A tilemap needs at least one layer.");
            return null;
        }

        List<IReadOnlyList<string>> layers = [];
        int height = -1;
        int width = -1;
        bool valid = true;

        for (int li = 0; li < layersEl.GetArrayLength(); li++)
        {
            List<string> rows = layersEl[li].EnumerateArray().Select(r => r.GetString()!).ToList();
            if (rows.Count == 0 || rows[0].Length == 0)
            {
                Error(owner, "tilemap.layers", $"{path}.layers[{li}]", "Tilemap layers must be non-empty.");
                valid = false;
                continue;
            }

            if (height < 0)
            {
                height = rows.Count;
                width = rows[0].Length;
            }
            else if (rows.Count != height)
            {
                Error(owner, "tilemap.layers", $"{path}.layers[{li}]", $"Layer {li} has {rows.Count} rows, expected {height}.");
                valid = false;
                continue;
            }

            for (int y = 0; y < rows.Count; y++)
            {
                string row = rows[y];
                if (row.Length != width)
                {
                    Error(owner, "tilemap.layers", $"{path}.layers[{li}][{y}]", $"Row {y} has length {row.Length}, expected {width}.");
                    valid = false;
                    continue;
                }

                for (int x = 0; x < row.Length; x++)
                {
                    char glyph = row[x];
                    if (glyph == ' ')
                    {
                        if (li == 0)
                        {
                            Error(owner, "tilemap.glyph", $"{path}.layers[0][{y}]", $"Base layer cannot use empty space ' ' at [{x}, {y}]. Use a ground glyph from tileset '{tilesetDef.QualifiedId}'.");
                            valid = false;
                        }

                        continue;
                    }

                    if (!tileset.Tiles.ContainsKey(glyph))
                    {
                        Error(owner, "tilemap.glyph", $"{path}.layers[{li}][{y}]",
                            $"Unknown tile glyph '{glyph}' at [{x}, {y}]. Tileset '{tilesetDef.QualifiedId}' declares: {string.Join(", ", tileset.Tiles.Keys.Order().Select(k => $"'{k}'"))}.");
                        valid = false;
                    }
                }
            }

            layers.Add(rows);
        }

        if (valid && expectedWidth is int ew && expectedHeight is int eh && (width != ew || height != eh))
        {
            Error(owner, "tilemap.size", $"{path}.layers", $"Tilemap is {width} by {height}, but must match the {ew} by {eh} map/field.");
            valid = false;
        }

        double? zoom = null;
        if (tilemapEl.TryGetProperty("zoom", out JsonElement zoomEl))
        {
            double z = zoomEl.GetDouble();
            if (z < 2 || z > 100)
            {
                Error(owner, "tilemap.zoom", $"{path}.zoom", "zoom must be between 2 and 100 tiles.");
                valid = false;
            }
            else
            {
                zoom = z;
            }
        }

        return valid ? Campaigns.AutoTiler.Resolve(tileset, layers, zoom) : null;
    }

    /// <summary>An audio asset's file is one the Engine decodes, and it has no picture fields.</summary>
    private void CheckAudio(Definition asset, ModuleSource source, string file)
    {
        byte[] bytes;
        try
        {
            bytes = source.Read(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(asset, "asset.file", "$.file", $"Can't read '{file}': {exception.Message}");
            return;
        }

        bool ogg = Starts(bytes, "OggS");
        bool wav = Starts(bytes, "RIFF") && bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE";
        bool flac = Starts(bytes, "fLaC");
        if (!ogg && !wav && !flac)
        {
            Error(asset, "asset.audio", "$.file", $"'{file}' is not audio the Engine decodes; use {string.Join(", ", Media.AudioFormats)}.");
        }

        foreach (string field in Media.SheetFields.Append("regions").Append("sampling").Where(field => asset.Json.TryGetProperty(field, out _)))
        {
            Error(asset, "asset.audio", $"$.{field}", $"\"{field}\" is for pictures; this asset is audio.");
        }
    }

    private static bool Starts(byte[] bytes, string magic)
    {
        return bytes.Length >= magic.Length && System.Text.Encoding.ASCII.GetString(bytes, 0, magic.Length) == magic;
    }

    /// <summary>The asset's file is a PNG the renderer admits, and a wall set's frames lie inside it.</summary>
    private void CheckImage(Definition asset, ModuleSource source, string file)
    {
        byte[] bytes;
        try
        {
            bytes = source.Read(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(asset, "asset.file", "$.file", $"Can't read '{file}': {exception.Message}");
            return;
        }

        if (PngImage.Read(bytes, out int width, out int height) is string problem)
        {
            Error(asset, "asset.image", "$.file", $"'{file}' {problem} Save it as an 8-bit RGBA PNG.");
            return;
        }

        _rules.ImageSizes[asset] = (width, height);
        bool sheet = Media.MediaOf(asset) == "sheet";
        if (asset.Json.TryGetProperty("regions", out JsonElement regions))
        {
            if (sheet)
            {
                Error(asset, "asset.regions", "$.regions", "Only an image has regions; a sheet is cut into frames by its frame_size.");
            }
            else
            {
                CheckRegions(asset, regions, width, height);
            }
        }

        if (sheet)
        {
            CheckSheet(asset, width, height);
        }
        else
        {
            foreach (string field in Media.SheetFields.Where(field => asset.Json.TryGetProperty(field, out _)))
            {
                Error(asset, "asset.sheet", $"$.{field}", $"\"{field}\" is a sheet field; this asset is an image. Make it \"media\": \"sheet\" to cut it into frames.");
            }
        }
    }

    private void CheckRegions(Definition asset, JsonElement regions, int width, int height)
    {
        foreach (JsonProperty region in regions.EnumerateObject())
        {
            int[] rect = region.Value.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            if (rect[0] < 0 || rect[1] < 0 || rect[2] < 1 || rect[3] < 1 || (long)rect[0] + rect[2] > width || (long)rect[1] + rect[3] > height)
            {
                Error(asset, "asset.regions", $"$.regions.{region.Name}", $"[{string.Join(", ", rect)}] must be [x, y, width, height] with a positive size inside the {width} x {height} image.");
                continue;
            }

            CheckLinearCrop(asset, $"$.regions.{region.Name}", rect[0], rect[1], rect[2], rect[3], width, height, "region");
        }
    }

    /// <summary>
    /// The pinned Engine rejects a degenerate sprite-atlas UV rectangle. A
    /// half-pixel inset keeps linear filtering inside a crop, but a one-pixel
    /// cropped axis would collapse when both edges move inward. Reject that
    /// one concrete case at the module boundary so the DOM and Engine retain
    /// the same authored sampling policy.
    /// </summary>
    private void CheckLinearCrop(Definition asset, string path, int x, int y, int cropWidth, int cropHeight, int imageWidth, int imageHeight, string kind)
    {
        if (!asset.Json.TryGetProperty("sampling", out JsonElement sampling)
            || sampling.ValueKind != JsonValueKind.String
            || sampling.GetString() != "linear")
        {
            return;
        }

        string? axis = x > 0 || cropWidth < imageWidth
            ? cropWidth == 1 ? "width" : null
            : null;
        axis ??= y > 0 || cropHeight < imageHeight
            ? cropHeight == 1 ? "height" : null
            : null;
        if (axis is not null)
        {
            Error(asset, "asset.sampling", path, $"linear sampling cannot safely filter a one-pixel cropped {kind} on its {axis} axis: the pinned Engine rejects a degenerate sprite-atlas UV rectangle (CSHARP_SPRITE_ATLAS_FRAME: DegenerateRect). Use \"nearest\" for this crop or make its {axis} dimension at least 2 pixels.");
        }
    }

    /// <summary>A figure draws exactly one monster, class or terrain key, and nothing has two figures.</summary>
    private void CheckFigure(Definition figure)
    {
        bool monster = figure.Json.TryGetProperty("monster", out _);
        bool characterClass = figure.Json.TryGetProperty("class", out _);
        bool terrain = figure.Json.TryGetProperty("terrain", out _);
        if ((monster ? 1 : 0) + (characterClass ? 1 : 0) + (terrain ? 1 : 0) != 1)
        {
            Error(figure, "figure.subject", "$", "A figure draws exactly one thing: give \"monster\", \"class\" or \"terrain\" (with \"combat\"), not several or none.");
            return;
        }

        if (terrain)
        {
            CheckTerrainFigure(figure);
            return;
        }

        string path = monster ? "$.monster" : "$.class";
        if (!_rules.References.TryGetValue((figure, path), out Definition? subject) || !_rules.References.TryGetValue((figure, "$.sprite"), out Definition? sprite))
        {
            return;
        }

        Definition? earlier = _rules.OfType(DefinitionTypes.Figure)
            .TakeWhile(other => other != figure)
            .FirstOrDefault(other => _rules.References.TryGetValue((other, path), out Definition? drawn) && drawn == subject);
        if (earlier is not null)
        {
            Error(figure, "figure.duplicate", path, $"{subject.QualifiedId} already has a figure ({earlier.QualifiedId}); a module set has one figure for each monster or class.");
            return;
        }

        _rules.Figures[subject] = sprite;
        if (_rules.References.TryGetValue((figure, "$.icon"), out Definition? icon))
        {
            _rules.Icons[subject] = icon;
        }
    }

    /// <summary>A terrain figure names a key its combat's field declares, once in the module set.</summary>
    private void CheckTerrainFigure(Definition figure)
    {
        if (!figure.Json.TryGetProperty("combat", out _))
        {
            Error(figure, "figure.subject", "$.combat", "A terrain figure needs the \"combat\" whose field declares the terrain.");
            return;
        }

        if (!_rules.References.TryGetValue((figure, "$.combat"), out Definition? combat) || !_rules.References.TryGetValue((figure, "$.sprite"), out Definition? sprite))
        {
            return;
        }

        string key = figure.Json.GetProperty("terrain").GetString()!;
        Dictionary<char, Terrain> kinds = combat.Json.TryGetProperty("field", out JsonElement field) ? CombatField.Kinds(field) : [];
        if (key.Length != 1 || !kinds.ContainsKey(key[0]))
        {
            string known = kinds.Count == 0 ? $"Combat '{combat.Id}' declares no terrain." : $"Its terrain: {string.Join(", ", kinds.Keys.Select(k => $"'{k}'"))}.";
            Error(figure, "figure.subject", "$.terrain", $"'{key}' is not terrain the field of combat '{combat.Id}' declares. {known}");
            return;
        }

        if (!_rules.TerrainFigures.TryAdd((combat, key[0]), sprite))
        {
            Error(figure, "figure.duplicate", "$.terrain", $"Terrain '{key}' of {combat.QualifiedId} already has a figure; a module set has one figure for each.");
        }
    }

    private static bool Inside(int value, int size) => value >= 0 && value < size;

    /// <summary>A sprite sheet is a whole grid of equal frames; its animations play frames it has.</summary>
    private void CheckSheet(Definition asset, int width, int height)
    {
        JsonElement json = asset.Json;
        if (!json.TryGetProperty("frame_size", out _))
        {
            Error(asset, "asset.sheet", "$", "A sheet needs \"frame_size\", the [width, height] of one frame (see `tales schema asset`).");
        }

        if (json.TryGetProperty("height", out JsonElement standing) && standing.GetDouble() <= 0)
        {
            Error(asset, "asset.sheet", "$.height", "height must be more than 0 cells.");
        }

        if (!json.TryGetProperty("frame_size", out JsonElement size))
        {
            return;
        }

        int frameWidth = size[0].GetInt32();
        int frameHeight = size[1].GetInt32();
        if (frameWidth < 1 || frameHeight < 1 || width % frameWidth != 0 || height % frameHeight != 0)
        {
            Error(asset, "asset.sheet", "$.frame_size", $"[{frameWidth}, {frameHeight}] must divide the {width} x {height} image into whole frames.");
            return;
        }

        CheckLinearCrop(asset, "$.frame_size", 0, 0, frameWidth, frameHeight, width, height, "sheet frame");

        int cells = width / frameWidth * (height / frameHeight);
        int count = cells;
        if (json.TryGetProperty("frame_count", out JsonElement declared))
        {
            count = declared.GetInt32();
            if (count < 1 || count > cells)
            {
                Error(asset, "asset.sheet", "$.frame_count", $"frame_count must be from 1 to the {cells} frames the image holds.");
                return;
            }
        }

        if (json.TryGetProperty("anchor", out JsonElement anchor)
            && (!Inside(anchor[0].GetInt32(), frameWidth) || !Inside(anchor[1].GetInt32(), frameHeight)))
        {
            Error(asset, "asset.sheet", "$.anchor", $"The anchor must be a pixel inside the {frameWidth} x {frameHeight} frame.");
        }

        if (!json.TryGetProperty("animations", out JsonElement animations))
        {
            return;
        }

        foreach (JsonProperty animation in animations.EnumerateObject())
        {
            string at = $"$.animations.{animation.Name}";
            JsonElement played = animation.Value.GetProperty("frames");
            if (played.GetArrayLength() == 0)
            {
                Error(asset, "asset.sheet", $"{at}.frames", "An animation plays at least one frame.");
            }

            int index = 0;
            foreach (JsonElement frame in played.EnumerateArray())
            {
                if (frame.GetInt32() < 0 || frame.GetInt32() >= count)
                {
                    Error(asset, "asset.sheet", $"{at}.frames[{index}]", $"Frame {frame.GetInt32()} isn't in the sheet; frames are 0 to {count - 1}.");
                }

                index++;
            }

            if (animation.Value.GetProperty("fps").GetDouble() <= 0)
            {
                Error(asset, "asset.sheet", $"{at}.fps", "fps must be more than 0.");
            }
        }
    }

    private void CheckResting()
    {
        foreach (Definition resting in _rules.OfType(DefinitionTypes.Resting))
        {
            bool rounds = resting.Json.GetProperty("unit").GetString() == "rounds";
            bool combat = resting.Json.TryGetProperty("combat", out _);
            if (rounds && !combat)
            {
                Error(resting, "resting.duration", "$.combat", "A rounds policy needs a combat reference for the ruleset's round_seconds.");
            }
            else if (!rounds && combat)
            {
                Error(resting, "resting.duration", "$.combat", "combat only applies to a rounds policy; omit it for hours or days.");
            }
        }
    }

    private void CheckEconomy()
    {
        List<Definition> economies = _rules.OfType(DefinitionTypes.Economy).ToList();
        _rules.Economy = economies.FirstOrDefault();
        foreach (Definition extra in economies.Skip(1))
        {
            Error(extra, "economy.duplicate", "$.id", $"A module set has at most one economy; {economies[0].QualifiedId} already defines it. Patch that one instead.");
        }

        foreach (Definition economy in economies.Where(economy => _manifests[economy.Module].Kind != ModuleKind.Ruleset))
        {
            Error(economy, "economy.module", "$", "The economy belongs in the ruleset. Extensions and campaigns can patch its sell_fraction.");
        }
    }

    private void CheckCurrencies()
    {
        foreach (Definition currency in _rules.OfType(DefinitionTypes.Currency))
        {
            if (_manifests[currency.Module].Kind != ModuleKind.Ruleset)
            {
                Error(currency, "currency.module", "$", "A currency belongs in the ruleset. Extensions and campaigns cannot introduce money types.");
            }
        }
    }

    private void CheckEvent(Definition definition)
    {
        string kind = definition.Json.GetProperty("kind").GetString()!;
        switch (kind)
        {
            case "text":
                CheckTextViews(definition);
                break;
            case "perception":
                CheckPerception(definition);
                break;
            case "check":
                CheckSceneCheck(definition);
                break;
            case "effect":
                CheckSceneEffect(definition);
                break;
            case "give" or "take":
                if (definition.Json.TryGetProperty("count", out JsonElement copies) && copies.GetInt32() <= 0)
                {
                    Error(definition, "event.items", "$.count", "An item count must be a positive whole number; omit count for one copy.");
                }

                break;
            case "treasure":
                bool hasCurrency = definition.Json.TryGetProperty("currency", out _);
                bool hasAmount = definition.Json.TryGetProperty("amount", out _);
                if (hasCurrency != hasAmount)
                {
                    Error(definition, "event.treasure", "$", "A treasure event needs both \"currency\" and \"amount\", or neither for item-only treasure.");
                }

                break;
            case "rest":
                bool timed = definition.Json.TryGetProperty("resting", out _);
                if (timed && (!definition.Json.TryGetProperty("periods", out JsonElement count) || count.GetInt32() <= 0))
                {
                    Error(definition, "event.rest", "$.periods", "Timed rest requires a positive whole number of periods.");
                }
                else if (!timed && (definition.Json.TryGetProperty("periods", out _) || definition.Json.TryGetProperty("wandering", out _)))
                {
                    Error(definition, "event.rest", "$", "periods and wandering require a resting policy; omit them for immediate rest.");
                }

                if (_rules.References.TryGetValue((definition, "$.wandering.event"), out Definition? roaming) && roaming.Json.GetProperty("kind").GetString() != "combat")
                {
                    Error(definition, "event.rest", "$.wandering.event", "The wandering event must have kind combat, with an encounter and its outcome chains.");
                }

                break;
            case "training" when !Characters.CharacterRules.RequiresTraining(_rules):
                Error(definition, "event.training", "$", "A training event needs advancement.training with cost and days expressions in its ruleset.");
                break;
            case "spell_reward" when definition.Json.TryGetProperty("member", out JsonElement member) && member.GetInt32() < 1:
                Error(definition, "event.spell-reward", "$.member", "A spell reward member number must be at least 1; omit it to reward every active party member.");
                break;
            case "shop":
                if (_rules.Economy is null)
                {
                    Error(definition, "event.shop", "$", "A shop needs an economy definition in its ruleset, for example { \"type\": \"economy\", \"id\": \"standard\", \"sell_fraction\": 0.5 }.");
                }

                if (definition.Json.TryGetProperty("buying", out JsonElement buying)
                    && buying.TryGetProperty("fraction", out JsonElement fractionValue)
                    && (!fractionValue.TryGetDecimal(out decimal fraction) || fraction is < 0 or > 1))
                {
                    Error(definition, "event.shop.buying-fraction", "$.buying.fraction", "A shop buying fraction must be from 0 to 1, for example 0.9 for ninety percent of the item's cost.");
                }

                if (definition.Json.TryGetProperty("buying", out buying)
                    && buying.TryGetProperty("max_value", out JsonElement maximum)
                    && (!maximum.TryGetDecimal(out decimal maxValue) || maxValue < 0))
                {
                    Error(definition, "event.shop.buying-max-value", "$.buying.max_value", "A shop maximum item value must be a nonnegative decimal number that fits the currency range.");
                }

                if (_rules.References.TryGetValue((definition, "$.buying.balance"), out Definition? balance)
                    && balance.Json.GetProperty("value_type").GetString() != "number")
                {
                    Error(definition, "event.shop.buying-balance", "$.buying.balance", "A shop buying balance must reference a numeric campaign or area variable.");
                }

                JsonElement items = definition.Json.GetProperty("items");
                Evaluator initialEvaluator = new(_rules, null);
                for (int index = 0; index < items.GetArrayLength(); index++)
                {
                    string stockPath = $"$.items[{index}].stock";
                    if (!_rules.References.TryGetValue((definition, stockPath), out Definition? stock))
                    {
                        continue;
                    }

                    if (stock.Json.GetProperty("value_type").GetString() != "number")
                    {
                        Error(definition, "event.shop.stock-type", stockPath, "A shop stock reference must name a numeric campaign or area variable.");
                        continue;
                    }

                    string initial = stock.Json.GetProperty("initial").GetString()!;
                    if (_rules.TryExpression(stock, "$.initial", out CompiledExpression? initialExpression)
                        && initialExpression!.Type == ExprType.Number)
                    {
                        try
                        {
                            decimal initialValue = initialEvaluator.Evaluate(initialExpression, new Scope(null, null)).Number;
                            if (initialValue < 0 || decimal.Truncate(initialValue) != initialValue)
                            {
                                Error(definition, "event.shop.stock-integral", stockPath, "A shop stock variable must start at a nonnegative whole number.");
                            }
                        }
                        catch (ExpressionException exception)
                        {
                            Error(definition, "event.shop.stock-integral", stockPath,
                                $"A shop stock variable must evaluate to a nonnegative whole number: {exception.Message}");
                        }
                    }
                    else if (double.TryParse(initial, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double oversized)
                        && (double.IsInfinity(oversized) || oversized > (double)decimal.MaxValue || oversized < (double)decimal.MinValue))
                    {
                        Error(definition, "event.shop.stock-integral", stockPath, "A shop stock variable must fit the decimal range and start at a nonnegative whole number.");
                    }
                }

                break;
            case "set":
                if (_rules.References.TryGetValue((definition, "$.variable"), out Definition? variable))
                {
                    CheckValueType(definition, "$.value", variable);
                }

                break;
            case "open" when string.IsNullOrWhiteSpace(definition.Json.GetProperty("door").GetString()):
                Error(definition, "event.open", "$.door", "An open event needs the ID of a door in the current area.");
                break;
            case "teleport":
                CheckEntry(definition, "$.area", definition.Json.GetProperty("entry").GetString()!, "$.entry");
                break;
            case "combat":
                CheckCombatEvent(definition);
                break;
        }
    }

    private void CheckSceneCheck(Definition definition)
    {
        int member = definition.Json.GetProperty("member").GetInt32();
        if (member < 1)
        {
            Error(definition, "event.check", "$.member", "A scene check member must be a positive 1-based party member number.");
        }
    }

    private void CheckSceneEffect(Definition definition)
    {
        if (definition.Json.TryGetProperty("member", out JsonElement member) && member.GetInt32() < 1)
        {
            Error(definition, "event.effect", "$.member", "An effect member must be a positive 1-based party member number when given.");
        }

        JsonElement operations = definition.Json.GetProperty("operations");
        if (operations.GetArrayLength() == 0)
        {
            Error(definition, "event.effect", "$.operations", "An effect needs at least one operation; use a text or branch event for a no-op scene step.");
            return;
        }

        for (int index = 0; index < operations.GetArrayLength(); index++)
        {
            JsonElement operation = operations[index];
            string op = operation.GetProperty("op").GetString()!;
            string path = $"$.operations[{index}]";
            if (op is ("damage" or "heal") && !operation.TryGetProperty("track", out _))
            {
                Error(definition, "event.effect", $"{path}.track", $"A campaign {op} operation needs an explicit track; there is no combat track here.");
            }

            if (op == "apply_condition")
            {
                if (operation.TryGetProperty("rounds", out _))
                {
                    Error(definition, "event.effect", $"{path}.rounds", "Timed condition rounds belong to combat turns; campaign effects use a condition's default duration instead.");
                }

                if (operation.TryGetProperty("values", out _))
                {
                    Error(definition, "event.effect", $"{path}.values", "Campaign effects do not keep transient condition values; define the condition's default values instead.");
                }

                CheckDurableCondition(definition, "event.effect", $"{path}.condition", "campaign effects");
            }
        }
    }

    private void CheckItem(Definition definition)
    {
        if (!definition.Json.TryGetProperty("use", out JsonElement use))
        {
            return;
        }

        JsonElement operations = use.GetProperty("operations");
        if (operations.GetArrayLength() == 0)
        {
            Error(definition, "item.use", "$.use.operations", "A consumable use needs at least one operation; omit use for equipment.");
            return;
        }

        bool hasDuration = use.TryGetProperty("duration_days", out _);

        bool hasCondition = false;
        for (int index = 0; index < operations.GetArrayLength(); index++)
        {
            JsonElement operation = operations[index];
            string path = $"$.use.operations[{index}]";
            string op = operation.GetProperty("op").GetString()!;
            if (op is ("damage" or "heal") && !operation.TryGetProperty("track", out _))
            {
                Error(definition, "item.use", $"{path}.track", $"A consumable {op} operation needs an explicit track; there is no combat track here.");
            }

            if (op != "apply_condition")
            {
                continue;
            }

            hasCondition = true;
            if (operation.TryGetProperty("rounds", out _))
            {
                Error(definition, "item.use", $"{path}.rounds", "Timed condition rounds belong to combat turns; consumable uses use duration_days for fictional campaign time.");
            }

            if (operation.TryGetProperty("values", out _))
            {
                Error(definition, "item.use", $"{path}.values", "Consumable uses do not keep transient condition values; define the condition's durable state instead.");
            }

            CheckDurableCondition(definition, "item.use", $"{path}.condition", "consumable uses");
        }

        if (hasDuration && !hasCondition)
        {
            Error(definition, "item.use", "$.use.duration_days", "duration_days applies to an apply_condition operation; omit it for an immediate track change.");
        }
    }

    private void CheckDurableCondition(Definition owner, string rule, string path, string context)
    {
        if (!_rules.References.TryGetValue((owner, path), out Definition? condition))
        {
            return;
        }

        if (condition.Json.TryGetProperty("instant", out JsonElement instant) && instant.GetBoolean())
        {
            Error(owner, rule, path, $"Instant conditions are combat operations; {context} need a condition that remains on the character.");
        }

        if (condition.Json.TryGetProperty("on_apply", out _))
        {
            Error(owner, rule, path, $"Conditions with on_apply need the combat operation executor; {context} need a durable condition without an apply hook.");
        }

        if (condition.Json.TryGetProperty("each_turn", out _)
            || condition.Json.TryGetProperty("end_of_turn", out _)
            || condition.Json.TryGetProperty("rounds_end", out _))
        {
            Error(owner, rule, path, $"Turn hooks and turn duration belong to combat; {context} need a durable condition without combat timing.");
        }
    }

    private void CheckTextViews(Definition definition)
    {
        if (!definition.Json.TryGetProperty("views", out JsonElement views))
        {
            return;
        }

        if (views.GetArrayLength() == 0)
        {
            Error(definition, "event.views", "$.views", "A text event's views must contain at least one authored mode.");
            return;
        }

        HashSet<string> modes = new(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement view in views.EnumerateArray())
        {
            string mode = view.GetProperty("mode").GetString()!;
            if (string.IsNullOrWhiteSpace(mode))
            {
                Error(definition, "event.views", $"$.views[{index}].mode", "A view mode must be nonempty text.");
            }
            else if (!modes.Add(mode))
            {
                Error(definition, "event.views", $"$.views[{index}].mode", $"View mode '{mode}' is repeated; each authored view needs a distinct mode.");
            }

            index++;
        }
    }

    private void CheckPerception(Definition definition)
    {
        foreach (string field in new[] { "scope", "success_mode", "failure_mode" })
        {
            if (string.IsNullOrWhiteSpace(definition.Json.GetProperty(field).GetString()))
            {
                Error(definition, "event.perception", $"$.{field}", $"{field} must be nonempty text.");
            }
        }
    }

    private void CheckCombatEvent(Definition definition)
    {
        Definition? combat = null;
        _rules.References.TryGetValue((definition, "$.encounter"), out Definition? encounter);
        if (definition.Json.TryGetProperty("combat", out _))
        {
            _rules.References.TryGetValue((definition, "$.combat"), out combat);
        }
        else
        {
            List<Definition> combats = _rules.OfType(DefinitionTypes.Combat).ToList();
            if (combats.Count != 1)
            {
                Error(definition, "event.combat", "$", combats.Count == 0
                    ? "There is no combat definition in the module set to fight with."
                    : "The module set has more than one combat definition; name one with \"combat\".");
            }
            else
            {
                combat = combats[0];
            }
        }

        if (definition.Json.TryGetProperty("surprise_rounds", out JsonElement rounds) && rounds.GetInt32() < 1)
        {
            Error(definition, "event.combat-surprise", "$.surprise_rounds", "surprise_rounds must be at least 1.");
        }

        if (!definition.Json.TryGetProperty("surprise", out _) && definition.Json.TryGetProperty("surprise_rounds", out _))
        {
            Error(definition, "event.combat-surprise", "$.surprise_rounds", "surprise_rounds needs surprise to name party or monsters.");
        }

        foreach (string path in new[] { "$.party_start", "$.monsters_start" })
        {
            if (!definition.Json.TryGetProperty(path[2..], out JsonElement start))
            {
                continue;
            }

            if (combat is null || !combat.Json.TryGetProperty("field", out JsonElement field))
            {
                Error(definition, "event.combat-placement", path, "A starting cell needs the selected combat definition to have a field.");
                continue;
            }

            int x = start[0].GetInt32();
            int y = start[1].GetInt32();
            int width = field.GetProperty("width").GetInt32();
            int height = field.GetProperty("height").GetInt32();
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                Error(definition, "event.combat-placement", path, $"Starting cell [{x}, {y}] is outside the combat field's {width} by {height} cells.");
            }
            else if (encounter is not null && !CombatField.Of(_rules, combat, encounter)!.Passable(new Cell(x, y)))
            {
                Error(definition, "event.combat-placement", path, $"Starting cell [{x}, {y}] is impassable in encounter '{encounter.Name}'. Choose an open or passable terrain cell.");
            }
        }
    }
}
