# Provenance: tactical-bestiaire

Every creature, name, number, behavior profile, and figure mapping in this
extension is original content written for Rusty Goldbox. The module contains
no copied setting text, monster description, published table, or Product
Identity. Its profiles demonstrate the data contract with short authored
plans:

- `behaviors/skirmisher.json` keeps a ranged creature at distance and gives a
  second creature a different target preference while both use the same
  missile action;
- `behaviors/warder.json` supports the most injured ally before attacking;
- `behaviors/phase_watcher.json` changes between ranged pressure and close
  pressure when its authored hit-point guard changes;
- `actions/withdraw.json` and `actions/approach_ally.json` are original
  compositions of the existing move operation, so destination steps can
  withdraw or reach a wounded ally without a product-specific primitive;
- `features/warder_training.json` gives the original Ivy companion the
  authored approach action; her healing spell and weapon use remain the
  ordinary Classic class, spell and item definitions;
- the `monsters/` definitions supply original tracks, stats, equipment-like
  action parameters and profile attachments; and
- `figures/` maps each logical creature to a figure or icon in the required
  `placeholder-art` assets module.

The extension relies on the Classic ruleset's open-licensed mechanics and
action definitions by direct reference. Classic's `LICENSE-OGL.txt` and
`PROVENANCE.md` remain in that dependency; this extension does not reproduce
that license text or its notices. Its figure media are original placeholder
assets from the `placeholder-art` dependency and are referenced by logical ID,
never by file path.
