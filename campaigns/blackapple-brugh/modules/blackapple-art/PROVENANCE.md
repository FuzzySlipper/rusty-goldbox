# Blackapple Brugh original art provenance

This independent asset module contains original ink-and-wash adaptation art
and the authored Blackapple palette. It has no module dependencies. Its assets
are released under CC BY-SA 4.0; the full licence is retained in
`LICENSE-CC-BY-SA-4.0.txt`.

Creative generation and correction used the built-in Codex image generator
from the written campaign art bible and original approved references. No source
PDF page, map, cover, or excluded source illustration was a generation input.
Source publication attribution is retained in the campaign's separate notice.

Opaque RGB originals are packaged as 8-bit RGBA by adding an all-255 alpha
channel with FFmpeg. Decoded RGB planes are compared for exact equality;
no resize, crop, composite, or creative pixel change occurs. The Ibix cutout
was already RGBA and is copied unchanged. All selected media use linear
sampling. Original/rejected attempts, exact prompts, raw tool outputs and
hash lineage remain in the editable `art/` tree outside runtime containers.

`art/production/runtime-index.json` and each batch's
`metadata/runtime-packing.json` record source/runtime byte hashes and native
sizes. These are provenance checks, not a replacement for Engine bundle
identity. Selected consumers and reuse boundaries are specified in
`art/production/art-inventory.md`. Independent judgments and ordinary product
captures are retained in Den.

## Included runtime media

| Runtime asset | File | Original source | Runtime SHA-256 |
| --- | --- | --- | --- |
| `blackapple_village` | `pictures/blackapple_village.png` | `campaigns/blackapple-brugh/art/references/blackapple_village.png` | `4b19bea6e4707dc2d89a4c0d6882b0098de42a5d4ef7541ccf83bfbdffbc91f6` |
| `brugh_ballroom_glamour` | `pictures/brugh_ballroom_glamour.png` | `campaigns/blackapple-brugh/art/production/level2/glamour/brugh_ballroom_glamour.png` | `1d8149dca951778609cbaaa4138ee0c2c7cdcf513144a6f62008adc3ca94eae4` |
| `brugh_ballroom_truth` | `pictures/brugh_ballroom_truth.png` | `campaigns/blackapple-brugh/art/production/level2/truth/brugh_ballroom_truth.png` | `3a2deff36b534ca1f0a16447ea79cdd76d808ed0d34bdcb8ce855086355a13d4` |
| `brugh_bluehouse_glamour` | `pictures/brugh_bluehouse_glamour.png` | `campaigns/blackapple-brugh/art/production/level1/glamour/brugh_bluehouse_glamour.png` | `0f7a667c1adf913851f1b3124a4350e85f75297440fcb1e1309cea2a0a4c8d02` |
| `brugh_bluehouse_truth` | `pictures/brugh_bluehouse_truth.png` | `campaigns/blackapple-brugh/art/production/level1/truth/brugh_bluehouse_truth.png` | `c9aa864a2bea0d53b2b1b94a7a1b1b105e0fedb99df012e691f26720df099baf` |
| `brugh_coatroom_glamour` | `pictures/brugh_coatroom_glamour.png` | `campaigns/blackapple-brugh/art/production/level1/glamour/brugh_coatroom_glamour.png` | `39c6610075591f1202cde3141cc3d9a21dbdec1fd7a85bbfc03c2af4509d8335` |
| `brugh_coatroom_truth` | `pictures/brugh_coatroom_truth.png` | `campaigns/blackapple-brugh/art/production/level1/truth/brugh_coatroom_truth.png` | `707003bd515c04e89ea8f2a813485ca6d8beb26d72d519d77976b95611ebfa83` |
| `brugh_dining_glamour` | `pictures/brugh_dining_glamour.png` | `campaigns/blackapple-brugh/art/production/level2/glamour/brugh_dining_glamour.png` | `655e667139369f10b9ac74667725d8b98e2bf9819b2922d664a45afffee97f3a` |
| `brugh_dining_truth` | `pictures/brugh_dining_truth.png` | `campaigns/blackapple-brugh/art/production/level2/truth/brugh_dining_truth.png` | `b664bd8c7c73a6ced5d4a5e685ec7a910549feeb3743633441d074499d30e906` |
| `brugh_dungeon_glamour` | `pictures/brugh_dungeon_glamour.png` | `campaigns/blackapple-brugh/art/production/level3/glamour/brugh_dungeon_glamour.png` | `c2f076e71e3e9f2925b21e75ea2556bf6223d9d4c5126daf24e0da758c949aaf` |
| `brugh_dungeon_truth` | `pictures/brugh_dungeon_truth.png` | `campaigns/blackapple-brugh/art/production/level3/truth/brugh_dungeon_truth.png` | `20fe5c43556e1c79b45ca35ddc04489e91fd9b8f4ebac3801ac6ca1aa4c9dce8` |
| `brugh_frog_prince_glamour` | `pictures/brugh_frog_prince_glamour.png` | `campaigns/blackapple-brugh/art/production/level1/glamour/brugh_frog_prince_glamour.png` | `eea91435674194aebf0b2c8ff50982977f0434639be185b2fc9ca2899a23924e` |
| `brugh_frog_prince_truth` | `pictures/brugh_frog_prince_truth.png` | `campaigns/blackapple-brugh/art/production/level1/truth/brugh_frog_prince_truth.png` | `4976b7574d310ebbc87dda9d8136bd3b401e001f37db22b598db870f0b0e4e3c` |
| `brugh_materials` | `pictures/brugh_materials.png` | `campaigns/blackapple-brugh/art/production/candidates/brugh_materials-candidate.png` | `f77f06e6a27456e13f36f9ac40ad0192ffecfb1d48808cf9d71c4ba7b45434ac` |
| `brugh_pool_glamour` | `pictures/brugh_pool_glamour.png` | `campaigns/blackapple-brugh/art/production/level2/glamour/brugh_pool_glamour.png` | `c0e9a46b9c41be673e597e08bb5312df39750fdf1e616da0df1d55e767f5f8fc` |
| `brugh_pool_truth` | `pictures/brugh_pool_truth.png` | `campaigns/blackapple-brugh/art/production/level2/truth/brugh_pool_truth.png` | `296f49d0366b4f3dde2a9b8772e52ecf269848d9cd521068103e9b5654cf21d0` |
| `brugh_reception_glamour` | `pictures/brugh_reception_glamour.png` | `campaigns/blackapple-brugh/art/references/brugh_reception_glamour.png` | `4a921bd54931218bf98a4c75992890b35a7072cdc95eb6762ff67d3cffba0abb` |
| `brugh_reception_truth` | `pictures/brugh_reception_truth.png` | `campaigns/blackapple-brugh/art/references/brugh_reception_truth.png` | `061ba7196f26c392126ae6b25f8549718fedf4c11f31e5da4f65ba1dcdec2f92` |
| `brugh_treasure_vault_glamour` | `pictures/brugh_treasure_vault_glamour.png` | `campaigns/blackapple-brugh/art/production/level3/glamour/brugh_treasure_vault_glamour.png` | `389941ac47a0cb6f05f70b2a3431926187e5ea7f2751e0efa30dfab07d3b13a8` |
| `brugh_treasure_vault_truth` | `pictures/brugh_treasure_vault_truth.png` | `campaigns/blackapple-brugh/art/production/level3/truth/brugh_treasure_vault_truth.png` | `a475f6d803de091b03a926c0ed30a78467dd7bd9a6b135449afa47baad3a3e6a` |
| `brugh_white_lady_glamour` | `pictures/brugh_white_lady_glamour.png` | `campaigns/blackapple-brugh/art/production/level1/glamour/brugh_white_lady_glamour.png` | `139175f85b245809ab28be241ecc5e11b3e5b74c1f0380f7de4c57d4b87aaeda` |
| `brugh_white_lady_truth` | `pictures/brugh_white_lady_truth.png` | `campaigns/blackapple-brugh/art/production/level1/truth/brugh_white_lady_truth.png` | `6b4d64ab7683d30c88f07e6d8588d4f5572575cfc79de789720cfc5976db70ce` |
| `ibix_double_figure` | `figures/ibix_double_figure.png` | `campaigns/blackapple-brugh/art/references/ibix_double_figure.png` | `bac596b5639b50f7efebc3647dbcd4fa15f8d2a7818e7183edb02bd6bb4348cc` |
| `master_ned_portrait` | `pictures/master_ned_portrait.png` | `campaigns/blackapple-brugh/art/production/recruits/originals/master_ned_portrait.png` | `257770fab7f684dd92c98b05235901ada9a4fc1e9391b569d14463593fe97cfc` |
| `tenpenny_forest` | `pictures/tenpenny_forest.png` | `campaigns/blackapple-brugh/art/production/candidates/tenpenny_forest-candidate.png` | `03e84eb751b0ef3aed963181c722d500f5436cb3de740f222c5783f6a8d2ebb0` |
| `wylda_portrait` | `pictures/wylda_portrait.png` | `campaigns/blackapple-brugh/art/references/wylda_portrait.png` | `d6af97ece59add51cb71124f584533ff030de14f892e289951aa214cd6b1a8d8` |

Truth/glamour pairs share doors, routes, object placement and camera; they are
alternate perceptions of one shared campaign world. C26's brown horse truth
and white unicorn glamour follow the source's intentional illusion; Ned's
recruit portrait always depicts his brown horse identity. The White Lady
correction replaced unintended skull/bone shapes with petals and preserves
its original rejected attempt and lineage.

The `darkfairytale` skin uses ordinary product skin colours. No custom
renderer, font, panel frame, button bitmap, audio or video is supplied.
