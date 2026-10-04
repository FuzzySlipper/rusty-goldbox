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
| `blackapple_agatha_shop` | `pictures/blackapple_agatha_shop.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_agatha_shop.png` | `d0d6919b4026316c7883382e271da3e2763a714de05f1763477bfd203bbc3b4d` |
| `blackapple_cemetery` | `pictures/blackapple_cemetery.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_cemetery.png` | `aab190f212aa1f99376392f3f24f8f207076aa42394837cc53c11069d6dcf8e0` |
| `blackapple_figwort_manor` | `pictures/blackapple_figwort_manor.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_figwort_manor.png` | `9bd0b09676203b177a737b342737834386170ac6e190e7a30f749a564c3c2c7e` |
| `blackapple_goodall_shop` | `pictures/blackapple_goodall_shop.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_goodall_shop.png` | `ca21a3215ceca4349feef6a83a1fa46058a589710db1b25ca7d6869249d07726` |
| `blackapple_hazard_shop` | `pictures/blackapple_hazard_shop.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_hazard_shop.png` | `183b4aac78a1e3505eb378707166e99c5256c516efdf1bb4b1e9fc159bf2a967` |
| `blackapple_jolly_fox` | `pictures/blackapple_jolly_fox.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_jolly_fox.png` | `9858a1c84d85ed72717b3bc08f0f5378ac5391098688fc766874139607738bac` |
| `blackapple_priory` | `pictures/blackapple_priory.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_priory.png` | `a75daae5d19b0a2cd2e4570f6677693fcee16e4b0df6ccf39de4b7c4ca3f2093` |
| `blackapple_title` | `pictures/blackapple_title.png` | `campaigns/blackapple-brugh/art/production/materials-title/originals/blackapple_title.png` | `f2bd27cde2ac9a0d4c6713be550274a41afb6a4b845a78da7834f58d5bd85271` |
| `blackapple_tobler_shop` | `pictures/blackapple_tobler_shop.png` | `campaigns/blackapple-brugh/art/production/village-service/originals/blackapple_tobler_shop.png` | `8607b54f217a99377b2b0906f55172cee40a4a2c4285cd8296352600f1a39d8a` |
| `blackapple_village` | `pictures/blackapple_village.png` | `campaigns/blackapple-brugh/art/references/blackapple_village.png` | `4b19bea6e4707dc2d89a4c0d6882b0098de42a5d4ef7541ccf83bfbdffbc91f6` |
| `blackapple_village_materials` | `pictures/blackapple_village_materials.png` | `campaigns/blackapple-brugh/art/production/materials-title/originals/blackapple_village_materials.png` | `f62170af4f29c450cd3512893f81949109b96cd3a5251b10e9a9627fbfdb1a4d` |
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
| `environs_faehill` | `pictures/environs_faehill.png` | `campaigns/blackapple-brugh/art/production/environs-service/originals/environs_faehill.png` | `810a8ae78f60e45cdae70d5b4690d4dc77ac87b818259d071d1f9a6a01f692a4` |
| `environs_fairy_ruins` | `pictures/environs_fairy_ruins.png` | `campaigns/blackapple-brugh/art/production/environs-service/originals/environs_fairy_ruins.png` | `1c488eb87ccbf160043b73d890aa2f8c84248173407cb2f97ad9c17178049b37` |
| `environs_forest_materials` | `pictures/environs_forest_materials.png` | `campaigns/blackapple-brugh/art/production/materials-title/originals/environs_forest_materials.png` | `bce7eda8966f9a919518cc8a566fbd39f42fb2421536bf50a6420891c0509fc1` |
| `environs_hen_teeth` | `pictures/environs_hen_teeth.png` | `campaigns/blackapple-brugh/art/production/environs-service/originals/environs_hen_teeth.png` | `4bdee2060015a42a551f0c889c3265d592fdeff7222852eaa8d0f2cfaede7722` |
| `environs_sanitarium` | `pictures/environs_sanitarium.png` | `campaigns/blackapple-brugh/art/production/environs-service/originals/environs_sanitarium.png` | `f4a508a46d0c255bab0f73db0a9bcc35848cc4533fd606a22bc225762207e52d` |
| `environs_shrine_confession` | `pictures/environs_shrine_confession.png` | `campaigns/blackapple-brugh/art/production/environs-service/originals/environs_shrine_confession.png` | `63ee544d8b5bedfb90703b10a8638294d9356f4d8641be157b9f316703fa5f41` |
| `environs_wild_dog_lair` | `pictures/environs_wild_dog_lair.png` | `campaigns/blackapple-brugh/art/production/environs-service/originals/environs_wild_dog_lair.png` | `2b4e6cfd6d6418259e4c8b8105c343980f63c9e08d43f06a2388bf405d2a8bc4` |
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
