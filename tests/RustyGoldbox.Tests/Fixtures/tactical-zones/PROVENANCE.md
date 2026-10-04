# Provenance: tactical-zones test fixture

This is an original test fixture written for Rusty Goldbox. Its names, stats,
action choices, and numbers are made up for tests and copy no published rules
text or tables. It requires the original `ascend` fixture only to reuse that
fixture's ascending check and action definitions.

The `zones` combat uses a three-action budget and a shared-zone field. It is the
second ruleset shape paired with the first-party Classic one-action grid in
`modules/tactical-bestiaire`; its purpose is to prove that authored profiles
refer to action legality and budget IDs rather than to a hardcoded edition or
grid assumption. Its original `advance` action spends one `standard` budget
unit and is attached as a common combat action so movement remains reachable
through the same legal action surface as the three authored strikes.

The fixture also adds an original optional combat feat, `brace_guard`. Its
`brace_on_hit` reaction spends the fixture combat's one-per-turn `reaction`
budget and uses the self-targeting `brace` action to reduce one pending hit by
2. `zone_striker` and its fixed four-point `training_tap` action provide a
deterministic hit for the acceptance test. These names, effects and numbers
are original and copy no published rule text or table.
