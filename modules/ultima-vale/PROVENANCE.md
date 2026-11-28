# Ultima Vale Provenance

Original first-party Rusty Tales showcase module (no external game text copied).

- **Ruleset:** `classic` (OGL 1.0a; see `modules/classic/PROVENANCE.md`).
- **Base Art:** `placeholder-art` (original first-party pixel art for figures, portraits, dungeon wall set, and UI skin).
- **Tileset (`tiles/oakvale_tiles.png`):** Original 128×96 (8×6 frames of 16×16) 8-bit RGBA pixel-art tileset created for this showcase, demonstrating:
  - Coordinate-seeded grass variants (`0..3`)
  - 3/4 `wall_top` half-timbered cottages (`front`, `top`, `cap`)
  - 16-tile `4bit` auto-tiled cobblestone paths (`16..31`) connecting seamlessly to doors, bridges, and stairs
  - 16-tile `4bit` auto-tiled river & shoreline banks (`32..47`)
  - Transparent overlay tiles (pine trees, village sign, lanterns, wildflowers, town well)
- **Baked Puppet Sprites (`sprites/hero_sword.png`, `sprites/mage_staff.png`, `sprites/goblin_axe.png`, `puppets.json`):** Exported via `tales puppet export-sheet` from `puppet-studio` (`examples/poselock-trio`, dual-licensed MIT OR Apache-2.0), demonstrating multi-clip (`idle`, `attack`, `hit`) tight-union baked sprite sheets with projected world `(0,0)` ground anchors.
