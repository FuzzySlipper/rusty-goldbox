# Provenance: tactical-diagnostics

This extension is original test data written for Rusty Goldbox. It contains
small authored combat policies whose names describe the diagnostic situation
under test: a blocked ranged plan, a support threshold and unavailable healing
resource, a lost target during a committed plan, and an explicit fallback.
`faulty_profile.json` is intentionally behaviorally wrong but structurally
valid: its first action use has a name no creature owns. The acceptance test
repairs that value as an ordinary module-authoring edit and compares the
resulting trace.

The fixture references action and spell definitions from the Classic test
ruleset by logical ID. It copies no published rules text or tables.
