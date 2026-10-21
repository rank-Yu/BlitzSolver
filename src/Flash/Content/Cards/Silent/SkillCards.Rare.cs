using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("SHADOW_STEP")]
public sealed class ShadowStep : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // CardOnPlaySupport.ShadowStep: discard the whole hand, then apply ShadowStepPower 1.
        FlashKernel.DiscardAllInHand(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.ShadowStepPower>(), 1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("STORM_OF_STEEL")]
public sealed class StormOfSteel : ShivGeneratingCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // CardPileOnPlaySupport.ReplaceHandWithShivs: discard the whole hand, then add one Shiv per
        // discarded card (upgraded when this card is upgraded).
        int discarded = x.B.HandCount;
        FlashKernel.DiscardAllInHand(ref x, 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, discarded, upgraded: x.Card.Upgrade != 0);
    }
}

/// <summary>
/// Native <c>Nightmare</c>: choose a hand card, then apply an instanced NightmarePower that clones the
/// (affliction-cleared) selected card at the next BeforeHandDraw. Existing engine:
/// <c>CardChoiceSupport.GetSpec</c>'s Nightmare case, <c>CardChoiceResolution.ApplyNightmare</c> and
/// <c>SimulatedCombatState.SetNightmareSelection</c>.
/// </summary>
[FlashContent("NIGHTMARE")]
public sealed class Nightmare : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> AppliedPowers => ["NIGHTMARE_POWER"];

    // Nightmare's OnPlay only starts the hand selection; the power application is its post-choice
    // effect (CardChoiceResolution.ApplyNightmare), so the card effect itself is empty.
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Nightmare, FlashPile.Hand, options, 1, selected,
                out int chosen))
            return;
        if (chosen == 0)
            return;
        if (!FlashRegistry.TryPower("NIGHTMARE_POWER", out ushort power))
            throw new FlashUnsupportedException("Nightmare 需要 NIGHTMARE_POWER 定义。");
        FlashCard snapshot = FlashKernel.CloneCard(FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]));
        snapshot.AfflictionId = 0;
        snapshot.AfflictionAmount = 0;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, 3, FlashLimits.PlayerOwner,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int row = FlashKernel.FindLastPower(x.B, FlashLimits.PlayerOwner, power);
        if (row < 0)
            return;
        int slot = FlashKernel.AllocateDetachedCard(ref x.B, snapshot);
        x.B.Powers[row].Aux0 = slot + 1;
    }
}

[FlashContent("CORROSIVE_WAVE")]
public sealed class CorrosiveWave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CorrosiveWave"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.CorrosiveWavePower>(), 0);
}

[FlashContent("MALAISE")]
public sealed class Malaise : FlashCardDef
{
    // CorePowerSupport.ApplyCardPowers.Malaise: X value (after ModifyXValue) plus one when upgraded;
    // Strength -malaise then Weak malaise on the target. The card has no dynamic variables.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        int amount = FlashKernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade != 0 ? 1 : 0);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.StrengthPower>(), -amount,
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.WeakPower>(), amount,
            FlashLimits.PlayerOwner);
    }
}

[FlashContent("BULLET_TIME")]
public sealed class BulletTime : FlashCardDef
{
    // CardOnPlaySupport.BulletTime: every non-X hand card becomes free this turn, then NoDraw 1.
    public override void OnPlay(ref FlashExec x)
    {
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref FlashCard card = ref x.B.Cards[index];
            if ((x.R.Card(card.Id, card.Upgrade).CostsX))
                continue;
            FlashKernel.MakeFreeThisTurn(x.R, ref card);
        }
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NoDrawPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

[FlashContent("OUTBREAK")]
public sealed class Outbreak : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    // CardOnPlaySupport.Batch042.ApplyOutbreak: Poison every living hittable enemy, then trigger each
    // one's Poison (snapshot order) with the deferred death pass after each.
    public override void OnPlay(ref FlashExec x)
    {
        ushort poison = FlashRegistry.PowerId<Powers.PoisonPower>();
        int amount = (int)FlashCardEffects.Var(x, 0);
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            targets[count++] = enemy;
            FlashKernel.ApplyPower(ref x, enemy, poison, amount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
        Span<byte> single = stackalloc byte[1];
        for (int index = 0; index < count; index++)
        {
            single[0] = targets[index];
            if (!FlashKernel.TriggerPoison(ref x, single))
                return;
            FlashKernel.ApplyEnemyDeathPowers(ref x);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("BLADE_OF_INK")]
public sealed class BladeOfInk : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => SilentContent.GenerateInkyShivs(ref x, (int)FlashCardEffects.Var(x, 0));
}

[FlashContent("ADRENALINE")]
public sealed class Adrenaline : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CardDrawCardMirrors.AdrenalineOnPlay: gain Energy, then draw Cards.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

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
