using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

// The remaining Silent cards. Existing engine paths are named on each definition:
// CardOnPlaySupport.Batch043 (KnifeTrap), CardOnPlaySupport.Batch042 (EchoingSlash), the
// CardEffectSpecRegistry/CardPowerOnPlaySupport cases (Burst, ToolsOfTheTrade, Shadowmeld, Mirage),
// and CorePowerSupport (TheHunt).

/// <summary>
/// <c>KnifeTrap</c>: auto-play every Shiv in the exhaust pile at the selected target. An upgraded
/// KnifeTrap upgrades each Shiv first. Existing engine: <c>CardOnPlaySupport.Batch043.ApplyKnifeTrap</c>
/// and <c>CardExecutionSupport.AutoPlay</c>; the exhaust pile is snapshotted in order and each
/// auto-played Shiv exhausts back to the pile, so the next search takes the next byte-equal copy.
/// </summary>
[FlashContent("KNIFE_TRAP")]
public sealed class KnifeTrap : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            throw new FlashUnsupportedException("KnifeTrap 需要目标。");
        int exhaust = x.B.ExhaustCount;
        if (exhaust == 0)
            return;
        Span<FlashCard> shivs = stackalloc FlashCard[exhaust];
        int count = 0;
        int start = FlashPiles.Start(x.B, FlashPile.Exhaust);
        for (int offset = 0; offset < exhaust; offset++)
        {
            FlashCard card = x.B.Cards[start + offset];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & FlashCardTag.Shiv) != 0)
                shivs[count++] = card;
        }
        bool upgrade = x.Card.Upgrade > 0;
        for (int item = 0; item < count; item++)
        {
            FlashCard wanted = shivs[item];
            int found = -1;
            int pileCount = x.B.ExhaustCount;
            int pileStart = FlashPiles.Start(x.B, FlashPile.Exhaust);
            for (int offset = 0; offset < pileCount; offset++)
            {
                ref readonly FlashCard candidate = ref x.B.Cards[pileStart + offset];
                if (candidate.Id == wanted.Id && candidate.Upgrade == wanted.Upgrade
                    && (x.R.Card(candidate.Id, candidate.Upgrade).Tags & FlashCardTag.Shiv) != 0)
                {
                    found = offset;
                    break;
                }
            }
            if (found < 0)
                throw new FlashUnsupportedException("KnifeTrap 的 Shiv 在结算前离开了消耗堆。");
            if (upgrade)
            {
                FlashKernel.UpgradeCard(x.R, ref FlashPiles.At(ref x.B, FlashPile.Exhaust, found));
            }
            if (!FlashKernel.AutoPlay(ref x, FlashPile.Exhaust, found, x.Target))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>EchoingSlash</c>: one attack context hits all enemies, and every target killed adds one more
/// wave. Existing engine: <c>CardOnPlaySupport.Batch042.ApplyEchoingSlash</c>.
/// </summary>
[FlashContent("ECHOING_SLASH")]
public sealed class EchoingSlash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.CardAttackRepeating(ref x, 0, initialWaves: 1);
}

[FlashContent("TOOLS_OF_THE_TRADE")]
public sealed class ToolsOfTheTrade : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["TOOLS_OF_THE_TRADE_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<ToolsOfTheTradePower>(owner, 1, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<ToolsOfTheTradePower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("BURST")]
public sealed class Burst : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Skills"];

    public override IReadOnlyList<string> AppliedPowers => ["BURST_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<BurstPower>(owner, card.DynamicVars["Skills"].IntValue, owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<BurstPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("SHADOWMELD")]
public sealed class Shadowmeld : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override IReadOnlyList<string> AppliedPowers => ["SHADOWMELD_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<ShadowmeldPower>(owner, "Power", owner).
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<ShadowmeldPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Mirage</c>: CalculatedBlock = CalculationBase + CalculationExtra × the poison on every living
/// enemy (<c>CalculatedVarSpecRegistry</c>'s Mirage multiplier). The card gains that block as a Move
/// card block; upgraded Mirage drops Exhaust, which is part of the rule-table keyword set.
/// </summary>
[FlashContent("MIRAGE")]
public sealed class Mirage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int poison = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (x.B.Creature(owner).IsAlive)
                poison += FlashKernel.GetAmount(x.B, owner, FlashRegistry.PowerId<PoisonPower>());
        }
        decimal block = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * poison;
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, block, FlashValueProps.Move, fromCard: true);
    }
}

/// <summary>
/// <c>TheHunt</c>: attack, and when the selected target's powers all allow a fatal kill and the hit
/// killed it, gain 1 <c>TheHuntPower</c> and 30 long-term resource. The extra card reward itself
/// lives on the combat room and has no representation in either simulated state; the existing
/// engine likewise only records the power and the long-term resource. Existing engine:
/// <c>CorePowerSupport</c>'s TheHunt case.
/// </summary>
[FlashContent("THE_HUNT")]
public sealed class TheHunt : FlashCardDef
{
    private const int LongTermResourceValue = 30;

    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<string> AppliedPowers => ["THE_HUNT_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        byte target = x.Target;
        bool fatal = target != FlashLimits.NoOwner && FlashKernel.TargetDeathTriggersFatal(x.B, target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || target == FlashLimits.NoOwner || x.B.Creature(target).IsAlive || !fatal)
            return;
        // CorePowerSupport: Apply<TheHuntPower>(owner, 1, owner) then RecordLongTermResource(30).
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<TheHuntPower>(), 1,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(FlashCounter.LongTermResourceValue, LongTermResourceValue);
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.TheHunt);
    }
}

[FlashContent("ANTICIPATE")]
public sealed class Anticipate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // SimulatedCombatState.ApplyAnticipate: apply the temporary power, then give Dexterity equal to
    // the amount that actually stuck (Artifact/stacking included).
    public override void OnPlay(ref FlashExec x)
    {
        ushort temporary = FlashRegistry.PowerId<AnticipatePower>();
        int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, temporary,
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        if (applied > 0)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
                applied, FlashLimits.PlayerOwner);
    }
}

[FlashContent("UNTOUCHABLE")]
public sealed class Untouchable : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("SERPENT_FORM")]
public sealed class SerpentForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SerpentFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["SERPENT_FORM_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<SerpentFormPower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}
