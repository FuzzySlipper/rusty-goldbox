using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>
/// The distances and sight lines that combat expressions see through
/// <c>combat.*</c>. The resolver and authored tactics both read them from here,
/// so a plan and the fight it plans for measure the field the same way.
/// </summary>
internal static class CombatGeometry
{
    /// <summary>Cells apart on the field; without a field or positions, everyone is 1 apart (within reach).</summary>
    public static decimal Distance(CombatField? field, Creature from, Creature to)
    {
        return field is not null && from.Position is Cell a && to.Position is Cell b ? field.Distance(a, b) : 1;
    }

    /// <summary>Whether nothing on the field blocks the line of sight between two creatures; without a field or positions, always.</summary>
    public static bool CanSee(CombatField? field, Creature from, Creature to)
    {
        return field is null || from.Position is not Cell a || to.Position is not Cell b || field.CanSee(a, b);
    }

    /// <summary>How far <paramref name="self"/> is from its nearest enemy still on the field (0 with none).</summary>
    public static decimal Nearest(CombatField? field, Combatant? self, Creature creature, IEnumerable<Combatant> everyone)
    {
        return everyone
            .Where(member => self is not null && member.Side != self.Side && !member.Defeated && !member.Escaped)
            .Select(member => Distance(field, creature, member.Creature))
            .DefaultIfEmpty(0)
            .Min();
    }

    /// <summary>How many of <paramref name="self"/>'s allies still on the field, other than itself and the target, stand within 1 cell of the target.</summary>
    public static decimal AlliesNear(CombatField? field, Combatant? self, Creature target, IEnumerable<Combatant> everyone)
    {
        return everyone.Count(member => self is not null
            && member != self
            && member.Side == self.Side
            && !member.Defeated
            && !member.Escaped
            && member.Creature != target
            && Distance(field, member.Creature, target) <= 1);
    }
}
