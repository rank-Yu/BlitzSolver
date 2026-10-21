using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Monsters.Act3;

/// <summary>
/// ScrollOfBiting: MORE_TEETH applies Strength 2 to itself; its starting PaperCutsPower 2 is a root
/// power (<c>MonsterMoveEffects</c> "ScrollOfBiting", native <c>AfterAddedToRoom</c>). The initial
/// move differs per instance (<c>StarterMoveIdx</c>) and is already the compiled initial node.
/// </summary>
[FlashContent("SCROLL_OF_BITING")]
public sealed class ScrollOfBiting : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "MORE_TEETH")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
    }
}

/// <summary>
/// SlimedBerserker: VOMIT_ICHOR_MOVE adds ten Slimed to the discard pile, LEECHING_HUG_MOVE applies
/// Weak 3 to the player (with the monster as applier in the existing engine) and Strength 3 to itself
/// (<c>MonsterMoveEffects</c> "SlimedBerserker").
/// </summary>
[FlashContent("SLIMED_BERSERKER")]
public sealed class SlimedBerserker : FlashMonsterDef
{
    private const int VomitSlime = 10;

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SLIMED", 0)];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "VOMIT_ICHOR_MOVE":
                for (int index = 0; index < VomitSlime; index++)
                {
                    FlashKernel.AddGeneratedCard(ref x,
                        FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Status.Slimed>()), FlashPile.Discard, creator: FlashLimits.NoOwner);
                }
                break;
            case "LEECHING_HUG_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.WeakPower>(), 3, owner);
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
                break;
        }
    }
}

/// <summary>
/// SoulNexus: DRAIN_LIFE_MOVE applies Vulnerable 2 and Weak 2 to the player
/// (<c>MonsterMoveEffects</c> "SoulNexus"); the other moves are plain attacks.
/// </summary>
[FlashContent("SOUL_NEXUS")]
public sealed class SoulNexus : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId != "DRAIN_LIFE_MOVE")
            return;
        FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.VulnerablePower>(), 2, owner);
        FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.WeakPower>(), 2, owner);
    }
}

/// <summary>
/// TheLost: DEBILITATING_SMOG takes <c>DebilitatingSmogStrengthStealAmount</c> Strength from the
/// player and gives it to itself; EYE_LASERS is a plain attack. Its starting PossessStrengthPower 1
/// is a root power (<c>MonsterMoveEffects</c> "TheLost").
/// </summary>
[FlashContent("THE_LOST")]
public sealed class TheLost : FlashMonsterDef
{
    private const int StrengthStealStat = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DebilitatingSmogStrengthStealAmount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "DEBILITATING_SMOG")
            return;
        int stolen = rules.StaticInts[StrengthStealStat];
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            -stolen, owner);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), stolen, owner);
    }
}

/// <summary>
/// TheForgotten: MIASMA takes <c>DebilitatingSmogDexStealAmount</c> Dexterity from the player, gains
/// 8 Block and gives the Dexterity to itself; DREAD's damage is its base plus the owner's current
/// Dexterity minus its root value (<c>MonsterMoveEffects</c>, <c>AdjustMonsterMoveDamage</c>). Its
/// starting PossessSpeedPower 1 is a root power.
/// </summary>
[FlashContent("THE_FORGOTTEN")]
public sealed class TheForgotten : FlashMonsterDef
{
    private const int DexterityStealStat = 0;

    public override IReadOnlyList<string> StaticIntNames => ["DebilitatingSmogDexStealAmount"];

    public override IReadOnlyList<string> RootPowerAmountNames => ["DEXTERITY_POWER"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "MIASMA")
            return;
        int stolen = rules.StaticInts[DexterityStealStat];
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            -stolen, owner);
        FlashKernel.GainBlock(ref x, owner, 8, FlashValueProps.Move, fromCard: false);
        FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.DexterityPower>(), stolen, owner);
    }

    public override int ModifyAttackDamage(in FlashBattle battle, FlashRules rules, byte owner, int moveNode,
        int attackIndex, int damage)
    {
        FlashMonsterRules machine = rules.MonsterRule(battle.Enemy(owner).Rules);
        if (machine.Nodes[moveNode].NativeId != "DREAD")
            return damage;
        int current = FlashKernel.GetAmount(battle, owner, FlashRegistry.PowerId<Powers.DexterityPower>());
        return damage + current - machine.RootPowerAmounts[0];
    }
}
