/// <summary>
/// Advancement (job promotion) classes unlocked at level 10.
/// </summary>
public enum AdvancementClass
{
    None,               // Not yet advanced

    // ── Warrior advancements (total 100%) ────────────────────────────────────
    GreatWarrior,       // 30%  | +2% ATK Physical / level > 10
    DualBlade,          // 30%  | +2% Speed / level > 10
    Spellblade,         // 30%  | +5% MaxMP, +2% ATK Magic / level > 10
    Berserker,          // 4.5% | Unlocks LifeSteal, +1% / level > 10
    SwiftBlade,         // 4.5% | Attack range x4, -2% AtkSpd / level; Toggle flight +20% speed
    SwordSaint,         // 1%   | +2.5% HP/MP, +1% all / level; Execute < 3% HP

    // ── Archer advancements (total 100%) ─────────────────────────────────────
    Longbowman,         // 30%  | Attack range +25% (one-time), +2% ATK Physical / level > 10
    Crossbowman,        // 30%  | Attack range -50% (one-time), +2% ATK Phys/DEF Phys/DEF Magic / level; triple shot
    SpellArcher,        // 30%  | +5% MaxMP, +2% ATK Magic / level; each hit +30% ATK Magic
    BurstArcher,        // 9%   | AtkSpeed x1.75 (slower), +5% ATK Physical / level; hit = 230% ATK
    GodSlayer,          // 1%   | +2.5% HP/MP, +1% all / level; Execute < 3% HP
}
