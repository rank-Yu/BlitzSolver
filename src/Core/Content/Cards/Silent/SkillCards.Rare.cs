using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("SHADOW_STEP")]
public sealed class ShadowStep : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        // CardOnPlaySupport.ShadowStep: discard the whole hand, then apply ShadowStepPower 1.
        BlitzKernel.DiscardAllInHand(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.ShadowStepPower>(), 1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("STORM_OF_STEEL")]
public sealed class StormOfSteel : ShivGeneratingCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        // CardPileOnPlaySupport.ReplaceHandWithShivs: discard the whole hand, then add one Shiv per
        // discarded card (upgraded when this card is upgraded).
        int discarded = x.B.HandCount;
        BlitzKernel.DiscardAllInHand(ref x, 0);
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
[BlitzContent("NIGHTMARE")]
public sealed class Nightmare : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override bool MayGenerateIdentityContent => true;

    public override IReadOnlyList<string> AppliedPowers => ["NIGHTMARE_POWER"];

    // Nightmare's OnPlay only starts the hand selection; the power application is its post-choice
    // effect (CardChoiceResolution.ApplyNightmare), so the card effect itself is empty.
    public override void OnPlay(ref BlitzExec x)
    {
    }

    public override void OwnChoice(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Nightmare, BlitzPile.Hand, options, 1, selected,
                out int chosen))
            return;
        if (chosen == 0)
            return;
        if (!BlitzRegistry.TryPower("NIGHTMARE_POWER", out ushort power))
            throw new BlitzUnsupportedException("Nightmare 需要 NIGHTMARE_POWER 定义。");
        BlitzCard snapshot = BlitzKernel.CloneCard(BlitzPiles.At(ref x.B, BlitzPile.Hand, selected[0]));
        snapshot.AfflictionId = 0;
        snapshot.AfflictionAmount = 0;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, 3, BlitzLimits.PlayerOwner,
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        int row = BlitzKernel.FindLastPower(x.B, BlitzLimits.PlayerOwner, power);
        if (row < 0)
            return;
        int slot = BlitzKernel.AllocateDetachedCard(ref x.B, snapshot);
        x.B.Powers[row].Aux0 = slot + 1;
    }
}

[BlitzContent("CORROSIVE_WAVE")]
public sealed class CorrosiveWave : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CorrosiveWave"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.CorrosiveWavePower>(), 0);
}

[BlitzContent("MALAISE")]
public sealed class Malaise : BlitzCardDef
{
    // CorePowerSupport.ApplyCardPowers.Malaise: X value (after ModifyXValue) plus one when upgraded;
    // Strength -malaise then Weak malaise on the target. The card has no dynamic variables.
    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        int amount = BlitzKernel.ResolveEnergyXValue(ref x) + (x.Card.Upgrade != 0 ? 1 : 0);
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.StrengthPower>(), -amount,
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.WeakPower>(), amount,
            BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("BULLET_TIME")]
public sealed class BulletTime : BlitzCardDef
{
    // CardOnPlaySupport.BulletTime: every non-X hand card becomes free this turn, then NoDraw 1.
    public override void OnPlay(ref BlitzExec x)
    {
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref BlitzCard card = ref x.B.Cards[index];
            if ((x.R.Card(card.Id, card.Upgrade).CostsX))
                continue;
            BlitzKernel.MakeFreeThisTurn(x.R, ref card);
        }
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.NoDrawPower>(), 1,
            BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("OUTBREAK")]
public sealed class Outbreak : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    // CardOnPlaySupport.Batch042.ApplyOutbreak: Poison every living hittable enemy, then trigger each
    // one's Poison (snapshot order) with the deferred death pass after each.
    public override void OnPlay(ref BlitzExec x)
    {
        ushort poison = BlitzRegistry.PowerId<Powers.PoisonPower>();
        int amount = (int)BlitzCardEffects.Var(x, 0);
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            targets[count++] = enemy;
            BlitzKernel.ApplyPower(ref x, enemy, poison, amount, BlitzLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
        Span<byte> single = stackalloc byte[1];
        for (int index = 0; index < count; index++)
        {
            single[0] = targets[index];
            if (!BlitzKernel.TriggerPoison(ref x, single))
                return;
            BlitzKernel.ApplyEnemyDeathPowers(ref x);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("BLADE_OF_INK")]
public sealed class BladeOfInk : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => SilentContent.GenerateInkyShivs(ref x, (int)BlitzCardEffects.Var(x, 0));
}

[BlitzContent("ADRENALINE")]
public sealed class Adrenaline : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CardDrawCardMirrors.AdrenalineOnPlay: gain Energy, then draw Cards.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzKernel.GainEnergy(ref x, BlitzCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>KnifeTrap</c>: auto-play every Shiv in the exhaust pile at the selected target. An upgraded
/// KnifeTrap upgrades each Shiv first. Existing engine: <c>CardOnPlaySupport.Batch043.ApplyKnifeTrap</c>
/// and <c>CardExecutionSupport.AutoPlay</c>; the exhaust pile is snapshotted in order and each
/// auto-played Shiv exhausts back to the pile, so the next search takes the next byte-equal copy.
/// </summary>
[BlitzContent("KNIFE_TRAP")]
public sealed class KnifeTrap : BlitzCardDef
{
    public override void OnPlay(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            throw new BlitzUnsupportedException("KnifeTrap 需要目标。");
        int exhaust = x.B.ExhaustCount;
        if (exhaust == 0)
            return;
        Span<BlitzCard> shivs = stackalloc BlitzCard[exhaust];
        int count = 0;
        int start = BlitzPiles.Start(x.B, BlitzPile.Exhaust);
        for (int offset = 0; offset < exhaust; offset++)
        {
            BlitzCard card = x.B.Cards[start + offset];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & BlitzCardTag.Shiv) != 0)
                shivs[count++] = card;
        }
        bool upgrade = x.Card.Upgrade > 0;
        for (int item = 0; item < count; item++)
        {
            BlitzCard wanted = shivs[item];
            int found = -1;
            int pileCount = x.B.ExhaustCount;
            int pileStart = BlitzPiles.Start(x.B, BlitzPile.Exhaust);
            for (int offset = 0; offset < pileCount; offset++)
            {
                ref readonly BlitzCard candidate = ref x.B.Cards[pileStart + offset];
                if (candidate.Id == wanted.Id && candidate.Upgrade == wanted.Upgrade
                    && (x.R.Card(candidate.Id, candidate.Upgrade).Tags & BlitzCardTag.Shiv) != 0)
                {
                    found = offset;
                    break;
                }
            }
            if (found < 0)
                throw new BlitzUnsupportedException("KnifeTrap 的 Shiv 在结算前离开了消耗堆。");
            if (upgrade)
            {
                BlitzKernel.UpgradeCard(x.R, ref BlitzPiles.At(ref x.B, BlitzPile.Exhaust, found));
            }
            if (!BlitzKernel.AutoPlay(ref x, BlitzPile.Exhaust, found, x.Target))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("BURST")]
public sealed class Burst : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Skills"];

    public override IReadOnlyList<string> AppliedPowers => ["BURST_POWER"];

    // CardPowerOnPlaySupport: combat.Apply<BurstPower>(owner, card.DynamicVars["Skills"].IntValue, owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<BurstPower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("SHADOWMELD")]
public sealed class Shadowmeld : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override IReadOnlyList<string> AppliedPowers => ["SHADOWMELD_POWER"];

    // CardPowerOnPlaySupport.SToZ: combat.Apply<ShadowmeldPower>(owner, "Power", owner).
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<ShadowmeldPower>(), (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
}
