# Authoring workspaces

An authoring workspace keeps editable material beside the runtime module
directories that will be played and exported. Create one with:

```bash
goldbox workspace new my-campaign
```

The generated `goldbox.json` has two path lists:

```json
{
  "modules": ["modules"],
  "authoring": {
    "modules": ["modules/my-campaign", "modules/my-art"],
    "staging": ".goldbox/staged",
    "exports": "exports"
  }
}
```

The top-level `modules` entries are the dependency search directories used by
all existing module commands. `authoring.modules` is the explicit list of
runtime module source directories owned by this workspace. A source directory
contains `module.json`, runtime definitions and approved runtime media, along
with any required licence and provenance files. Keep canon, prompt trials,
reference images, rejected images, scripts and other scratch material in the
editable roots outside those module directories.

`goldbox workspace build <path>` validates every explicit authored module with
the normal Core module loader, resolves its requirements from the workspace
search paths, and replaces `authoring.staging` with one directory per authored
module. Its JSON result lists every included runtime file and reports
unresolved requirements with the owning module, manifest path and JSON path.
The generated staging and export paths must not overlap module sources, module
search directories or editable roots; this is checked before either generated
tree is cleaned.

`goldbox workspace export <path>` runs the build first, cleans the generated
export directory, and independently packs each staged module as
`<id>-<version>.rpak` through the pinned Engine `rusty pack-content` command.
The output lists each generated container. A failed build leaves source and
editable files untouched and does not pack a new export.

`goldbox workspace install <path>` builds the same way and packs each staged
module into the Game's module library instead (`$GOLDBOX_MODULE_LIBRARY`,
else the XDG data directory), replacing the same versions installed before.
Press Refresh on the Game's title screen to see the campaign without a
restart.

To play a workspace while editing it, without installing, start the Game with
`GOLDBOX_WORKSPACES` naming the workspace directory (several separated like
`PATH`). Each title listing builds it, repacks only modules whose content
identity changed into `.goldbox/game/`, and offers those in place of installed
copies with the same IDs.

Use `goldbox workspace inspect` to see the resolved paths and authored module
manifests, and `goldbox schema workspace --json` to discover the contract and
the build/export commands from the tool itself.
