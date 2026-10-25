using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act3;

/// <summary>
/// ScrollOfBiting: MORE_TEETH applies Strength 2 to itself; its starting PaperCutsPower 2 is a root
/// power (<c>MonsterMoveEffects</c> "ScrollOfBiting", native <c>AfterAddedToRoom</c>). The initial
/// move differs per instance (<c>StarterMoveIdx</c>) and is already the compiled initial node.
/// </summary>
[BlitzContent("SCROLL_OF_BITING")]
public sealed class ScrollOfBiting : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "MORE_TEETH")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
    }
}

/// <summary>
/// SlimedBerserker: VOMIT_ICHOR_MOVE adds ten Slimed to the discard pile, LEECHING_HUG_MOVE applies
/// Weak 3 to the player (with the monster as applier in the existing engine) and Strength 3 to itself
/// (<c>MonsterMoveEffects</c> "SlimedBerserker").
/// </summary>
[BlitzContent("SLIMED_BERSERKER")]
public sealed class SlimedBerserker : BlitzMonsterDef
{
    private const int VomitSlime = 10;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SLIMED", 0)];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "VOMIT_ICHOR_MOVE":
                for (int index = 0; index < VomitSlime; index++)
                {
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Slimed>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
                }
                break;
            case "LEECHING_HUG_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.WeakPower>(), 3, owner);
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
                break;
        }
    }
}

/// <summary>
/// SoulNexus: DRAIN_LIFE_MOVE applies Vulnerable 2 and Weak 2 to the player
/// (<c>MonsterMoveEffects</c> "SoulNexus"); the other moves are plain attacks.
/// </summary>
[BlitzContent("SOUL_NEXUS")]
public sealed class SoulNexus : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "DRAIN_LIFE_MOVE")
            return;
        BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.VulnerablePower>(), 2, owner);
        BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.WeakPower>(), 2, owner);
    }
}

/// <summary>
/// TheLost: DEBILITATING_SMOG takes <c>DebilitatingSmogStrengthStealAmount</c> Strength from the
/// player and gives it to itself; EYE_LASERS is a plain attack. Its starting PossessStrengthPower 1
/// is a root power (<c>MonsterMoveEffects</c> "TheLost").
/// </summary>
[BlitzContent("THE_LOST")]
public sealed class TheLost : BlitzMonsterDef
{
    private const int StrengthStealStat = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DebilitatingSmogStrengthStealAmount"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "DEBILITATING_SMOG")
            return;
        int stolen = rules.StaticInts[StrengthStealStat];
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            -stolen, owner);
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), stolen, owner);
    }
}

/// <summary>
/// TheForgotten: MIASMA takes <c>DebilitatingSmogDexStealAmount</c> Dexterity from the player, gains
/// 8 Block and gives the Dexterity to itself; DREAD's damage is its base plus the owner's current
/// Dexterity minus its root value (<c>MonsterMoveEffects</c>, <c>AdjustMonsterMoveDamage</c>). Its
/// starting PossessSpeedPower 1 is a root power.
/// </summary>
[BlitzContent("THE_FORGOTTEN")]
public sealed class TheForgotten : BlitzMonsterDef
{
    private const int DexterityStealStat = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DebilitatingSmogDexStealAmount"];

    public override IReadOnlyList<string> RootPowerAmountNames => ["DEXTERITY_POWER"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "MIASMA")
            return;
        int stolen = rules.StaticInts[DexterityStealStat];
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DexterityPower>(),
            -stolen, owner);
        BlitzKernel.GainBlock(ref x, owner, 8, BlitzValueProps.Move, fromCard: false);
        BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.DexterityPower>(), stolen, owner);
    }

    public override int ModifyAttackDamage(in BlitzBattle battle, BlitzRules rules, byte owner, int moveNode,
        int attackIndex, int damage)
    {
        BlitzMonsterRules machine = rules.MonsterRule(battle.Enemy(owner).Rules);
        if (machine.Nodes[moveNode].NativeId != "DREAD")
            return damage;
        int current = BlitzKernel.GetAmount(battle, owner, BlitzRegistry.PowerId<Powers.DexterityPower>());
        return damage + current - machine.RootPowerAmounts[0];
    }
}
