using RustyTales.Core.Expressions;

namespace RustyTales.Core.Definitions;

public static partial class DefinitionTypes
{
    public static DefinitionType Figure { get; } = new(
        "figure",
        "How a monster, a class or a kind of combat terrain looks: the sprite drawn for it in combat and in the 3D view. Rulesets carry no art, so "
        + "figures live in a campaign or extension that requires both the ruleset and an assets module. Give exactly one of "
        + "monster, class and terrain (with its combat); a module set has at most one figure for each.",
        [
            new("monster", new ReferenceKind("monster"), false, "The monster it draws."),
            new("class", new ReferenceKind("class"), false, "The class it draws (for every character of the class)."),
            new("combat", new ReferenceKind("combat"), false, "With terrain: the combat definition whose field declares it."),
            new("terrain", new TextKind(), false, "A terrain key the combat's field declares, for example \"#\": the sprite stands on every cell of it."),
            new("sprite", new ReferenceKind("asset", "figure"), true, "The sheet that draws it (a figure slot)."),
            new("icon", new ReferenceKind("asset", "picture"), false, "A small picture for lists, such as the combat roster (a picture slot)."),
        ],
        """
        { "type": "figure", "id": "skeleton", "monster": "classic:skeleton", "sprite": "placeholder-art:skeleton", "icon": "placeholder-art:skull" }
        """);

    public static DefinitionType Variable { get; } = new(
        "variable",
        "A campaign or area variable: declared with a type and an initial value, read as campaign.var.<id> or area.var.<id>. Area values are separate for every area and changed by set events in the current area. Undeclared variables are errors.",
        [
            new("value_type", new EnumKind(["number", "boolean", "text"]), true, "Its type."),
            new("initial", new ExpressionKind(null, Roots.None), true, "Its value when the campaign starts, of its type, for example \"false\" or \"0\"."),
            new("scope", new EnumKind(["campaign", "area"]), false, "Where its value lives: campaign (the default) is shared by the whole adventure; area gives each area its own value and is read as area.var.<id>."),
            new("description", new TextKind(), false, "What it records."),
        ],
        """
        { "type": "variable", "id": "gate_open", "value_type": "boolean", "initial": "false" }
        """);

    /// <summary>The asset kinds the presentation draws, each an RGBA PNG.</summary>
    public static DefinitionType Asset { get; } = new(
        "asset",
        "A logical asset ID mapped to a file in the module, and what media the file is. Other modules refer to it as module:id, never by path. "
        + "An asset says only what it is; each reference names the slot it fills (picture, figure, wall_set) and the slot decides which media fit, "
        + "so anything that shows a picture shows any visual media (see `tales schema media`). Images are 8-bit RGBA PNGs, the format the Engine renderer admits; "
        + "audio is Ogg (Vorbis or Opus), WAV or FLAC, the formats the Engine decodes.",
        [
            new("media", new EnumKind(Media.Types), true, "What the file is: \"image\" (one picture, optionally with named regions), \"sheet\" (equal frames, optionally animated) or \"audio\" (a sound or music)."),
            new("sampling", new EnumKind(Media.SamplingModes), false, "Visual scaling: \"nearest\" keeps pixel art sharp (the default), while \"linear\" smoothly scales illustrated art. Omit it for nearest. A linear sheet frame or named region cut from a larger image needs at least 2 pixels on every cropped axis; use nearest for a one-pixel crop."),
            new("file", new TextKind(), true, "Path of the file inside this module, with forward slashes: a PNG for an image or sheet; an Ogg, WAV or FLAC file for audio."),
            new("regions", new MapKind(new TextKind(), new ListKind(new IntegerKind(), 4)), false,
                "image only: named pixel rectangles [x, y, width, height] inside the image. A wall set needs wall and door, and may have floor and ceiling."),
            new("frame_size", new ListKind(new IntegerKind(), 2), false,
                "sheet only (required): [width, height] of one frame in pixels. Frames are read left to right, then top to bottom, and the image must be a whole number of frames across and down."),
            new("frame_count", new IntegerKind(), false, "sheet only: how many frames the sheet holds, when the last row isn't full. Defaults to every cell."),
            new("animations", new MapKind(new TextKind(), new ObjectKind(
            [
                new("frames", new ListKind(new IntegerKind()), true, "Frame numbers in play order, counting from 0."),
                new("fps", new NumberKind(), true, "Frames per second."),
                new("loop", new BooleanKind(), false, "Repeat (true, the default) or play once and hold the last frame."),
            ])), false, "sheet only: named animations such as idle, walk, attack, hit or die. A picture plays the first; a figure picks by name. Without any, it shows frame 0."),
            new("faces", new EnumKind(["left", "right"]), false, "sheet only: which way the art faces; the renderer flips it to face the other way. A figure needs it."),
            new("anchor", new ListKind(new IntegerKind(), 2), false, "sheet only: the pixel [x, y] in a frame that stands on the floor. Defaults to the bottom centre."),
            new("height", new NumberKind(), false, "sheet only: how tall a frame stands, in cells (a cell is 1 x 1 x 1); the width follows the frame's shape. A figure needs it."),
            new("tags", new ListKind(new TextKind()), false, "Words that say what the art is meant for, so pickers can offer it: \"portrait\" puts it in the portrait chooser. They don't limit where it can be used."),
        ],
        """
        {
          "type": "asset",
          "id": "skeleton",
          "media": "sheet",
          "file": "sprites/skeleton.png",
          "frame_size": [32, 48],
          "faces": "right",
          "height": 0.8,
          "animations": { "idle": { "frames": [0, 1, 2, 1], "fps": 4 }, "attack": { "frames": [3, 4, 5], "fps": 8, "loop": false } }
        }
        """);

    public static DefinitionType Area { get; } = new(
        "area",
        "A map: a grid of cells with walls, doors and openings on cell edges, plus cell features and named entry points.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("map", new ListKind(new TextKind()), true, "The grid as " + Campaigns.AreaMap.FormatDescription),
            new("wall_set", new ReferenceKind("asset", "wall_set"), false, "The image the first-person view draws the area with (a wall_set slot: wall and door regions)."),
            new("cells", new ListKind(new ObjectKind(
            [
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell; x west to east and y north to south, from 0."),
                new("zone", new TextKind(), false, "A zone tag for the cell."),
                new("backdrop", new ReferenceKind("asset", "picture"), false, "The picture shown over the view in the cell (a picture slot)."),
                new("prop", new ObjectKind(
                [
                    new("sprite", new ReferenceKind("asset", "figure"), true, "The sheet standing in the cell (a figure slot): a chest, a pillar, bones, a guard."),
                    new("hidden", new ExpressionKind(ExprType.Boolean, Roots.Campaign | Roots.Area), false,
                        "While this is true the prop isn't there, for example \"campaign.var.chest_opened\" or \"area.var.chest_opened\"; it may read campaign.var or area.var."),
                ]), false, "Something standing in the middle of the cell in the first-person view. It doesn't block movement."),
                new("event", new ReferenceKind("event"), false, "The event that runs when the party enters the cell."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), false, "Run the event only when the party enters facing this way."),
                new("once", new BooleanKind(), false, "If true, the event runs only the first time."),
            ])), false, "Cells with features."),
            new("search", new ReferenceKind("check"), false, "The ruleset check used by the search command to discover secret doors around the party; when omitted, a check named search is used if the ruleset provides one."),
            new("doors", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Door name used by open events and commands."),
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell whose side is the door."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), true, "The cell side containing the door."),
                new("locked", new BooleanKind(), false, "Whether the door starts locked; true is the default when this field is omitted."),
                new("key", new ReferenceKind("item"), false, "An item that opens the door without being consumed."),
                new("pick", new ReferenceKind("check"), false, "A check any party member may succeed at to pick the lock."),
                new("force", new ReferenceKind("check"), false, "A check any party member may succeed at to force the door."),
                new("event", new ReferenceKind("event"), false, "An event chain that may unlock the door; an open event names this door."),
            ])), false, "Locked door declarations. Each one names a DD map edge and at least one key, pick, force or event mechanism when locked."),
            new("entries", new MapKind(new TextKind(), new ObjectKind(
            [
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), true, "Which way the party faces on arrival."),
            ])), true, "Named places the party can arrive at (by starting or by teleport); arriving doesn't run the cell's event."),
        ],
        """
        {
          "type": "area",
          "id": "hall",
          "name": "Hall",
          "map": [
            "+--+--+",
            "|     |",
            "+  +DD+",
            "|  |  |",
            "+--+--+"
          ],
          "doors": [ { "id": "gate", "at": [1, 0], "facing": "south", "key": "classic:key" } ],
          "cells": [ { "at": [1, 1], "event": "gate", "once": true } ],
          "entries": { "start": { "at": [0, 0], "facing": "east" } }
        }
        """);

    public static DefinitionType Campaign { get; } = new(
        "campaign",
        "Where a campaign starts and who may play it. A campaign module has exactly one.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("start", new ObjectKind(
            [
                new("area", new ReferenceKind("area"), true, "The starting area."),
                new("entry", new TextKind(), true, "The entry point there."),
            ]), true, "Where the party begins."),
            new("party", new ObjectKind(
            [
                new("min", new IntegerKind(), true, "Fewest characters."),
                new("max", new IntegerKind(), true, "Most characters."),
            ]), true, "Party size."),
            new("intro", new ReferenceKind("event"), false, "An event that runs before the first command."),
            new("skin", new ReferenceKind("skin"), false, "How the Game's panels look while this campaign plays, unless the player picks another skin."),
            new("starting_party", new ListKind(new ReferenceKind("npc")), false, "Pre-authored NPC characters who start the campaign as the party. When present, single-player play in the Game skips party creation and `tales play` needs no `--party` files."),
            new("combat_presentation", new EnumKind(["tactical", "first_person", "side_view"]), false, "Default combat framing for this campaign (\"tactical\", \"first_person\" or \"side_view\"), overriding the combat definition's presentation when set."),
        ],
        """
        { "type": "campaign", "id": "crypt", "name": "The Crypt", "start": { "area": "hall", "entry": "start" }, "party": { "min": 1, "max": 6 } }
        """);

    public static DefinitionType Event { get; } = new(
        "event",
        "A step in an event chain. Its kind (" + string.Join(", ", EventTypes.All.Select(kind => kind.Name)) + ") decides its other fields; see `tales schema events`.",
        [new("kind", new EnumKind(EventTypes.All.Select(kind => kind.Name).ToList()), true, "What the event does.")],
        EventTypes.Text.Example);

    /// <summary>The theme colours a skin may set.</summary>
    public static IReadOnlyList<string> SkinColors { get; } =
        ["background", "text", "muted", "accent", "border", "inset", "button", "button_text", "page", "page_text", "page_accent", "page_link"];

    private static readonly ObjectKind NineSlice = new(
    [
        new("picture", new ReferenceKind("asset", "border"), true, "The image, cut into nine: corners stay as drawn, edges and middle stretch."),
        new("slice", new IntegerKind(), true, "How many pixels in from each edge the cuts are; under half the image's width and height."),
    ]);

    public static DefinitionType Skin { get; } = new(
        "skin",
        "How the Game's panels look: theme colours and pictures, in an assets or campaign module. A campaign names its skin; "
        + "the player may pick any installed one instead. Skins restyle the panels and may set their proportions; they don't change what the panels do.",
        [
            new("name", new TextKind(), true, "Display name in the skin chooser."),
            new("colors", new MapKind(new TextKind(), new TextKind()), false,
                "Theme colours as #rgb, #rrggbb or #rrggbbaa, by name: " + string.Join(", ", SkinColors) + ". Any left out keep the Game's own."),
            new("panel", new ReferenceKind("asset", "picture"), false, "A picture tiled behind the panels."),
            new("frame", NineSlice, false, "A border drawn around the panels."),
            new("button", NineSlice, false, "The face of every button."),
            new("title", new ReferenceKind("asset", "picture"), false, "Art shown at the top of the panels in place of the product's name."),
            new("layout", new ObjectKind(SkinLayout.Parts.Select(part => new Field(
                part.Name, new NumberKind(), false, $"{part.Description}; {part.Minimum} to {part.Maximum}, {part.Default} when left out.")).ToList()), false,
                "The panels' proportions for this skin; any left out keep the Game's own. The player's own layout overrides them."),
        ],
        """
        {
          "type": "skin",
          "id": "stone",
          "name": "Crypt stone",
          "colors": { "background": "#141318e8", "text": "#e6e0d4", "accent": "#e8b04a" },
          "panel": "crypt-art:stone_tile",
          "frame": { "picture": "crypt-art:stone_frame", "slice": 8 },
          "button": { "picture": "crypt-art:stone_button", "slice": 4 },
          "layout": { "log_share": 0.42, "text_scale": 1.1 }
        }
        """);
}
