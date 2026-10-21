using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>TimesUp</c>: calculated attack with CalculationBase 0 + ExtraDamage 1 × the target's Doom
/// (<c>CalculatedVarSpecRegistry.TimesUp</c>).
/// </summary>
[FlashContent("TIMES_UP")]
public sealed class TimesUp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        int doom = FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.DoomPower>());
        FlashCardEffects.Attack(ref x, NecrobinderContent.Calculated(ref x, 0, 1, doom));
    }
}

/// <summary>
/// <c>SoulStorm</c>: calculated attack with CalculationBase + ExtraDamage × Soul cards in the exhaust
/// pile (<c>CalculatedVarSpecRegistry.SoulStorm</c>).
/// </summary>
[FlashContent("SOUL_STORM")]
public sealed class SoulStorm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int souls = CountExhaustSouls(x.B, x.R);
        FlashCardEffects.Attack(ref x, NecrobinderContent.Calculated(ref x, 0, 1, souls));
    }

    private static int CountExhaustSouls(in FlashBattle battle, FlashRules rules)
    {
        int start = FlashPiles.Start(battle, FlashPile.Exhaust);
        int count = FlashPiles.Count(battle, FlashPile.Exhaust);
        int souls = 0;
        for (int offset = 0; offset < count; offset++)
        {
            ref readonly FlashCard card = ref battle.Cards[start + offset];
            if (rules.Card(card.Id, card.Upgrade).IsSoul)
                souls++;
        }
        return souls;
    }
}

/// <summary>
/// <c>Squeeze</c>: Osty attack whose calculated damage is CalculationBase + ExtraDamage × the number of
/// other OstyAttack cards in the combat piles (<c>CalculatedVarSpecRegistry.Squeeze</c>).
/// </summary>
[FlashContent("SQUEEZE")]
public sealed class Squeeze : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        int others = NecrobinderContent.CountOstyAttackCards(x.B, x.R) - 1;
        decimal damage = NecrobinderContent.Calculated(ref x, 0, 1, others);
        NecrobinderContent.OstyAttack(ref x, damage);
    }
}

/// <summary>
/// <c>Misery</c>: deal the attack, then copy every debuff on the selected target to every other
/// hittable enemy. Existing engine: <c>BespokeCardMirrors.MiseryOnPlay</c>. The clone carries the
/// original amount and applier. Temporary powers are the existing engine's <c>ITemporaryPower</c>
/// merge path (temporary → internal power amount); Flash has no per-instance dynamic-variable copy,
/// so a target carrying one is an explicit unsupported boundary rather than a half-clone.
/// </summary>
[FlashContent("MISERY")]
public sealed class Misery : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<ushort> ids = stackalloc ushort[FlashLimits.Powers];
        Span<int> amounts = stackalloc int[FlashLimits.Powers];
        Span<byte> appliers = stackalloc byte[FlashLimits.Powers];
        Span<byte> targets = stackalloc byte[FlashLimits.Powers];
        int count = 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Owner != x.Target || power.Amount == 0)
                continue;
            FlashPowerDef def = FlashRegistry.Power(power.Id);
            if (def.TypeForAmount(power.Amount) != FlashPowerType.Debuff)
                continue;
            ids[count] = power.Id;
            amounts[count] = power.Amount;
            appliers[count] = power.Applier;
            targets[count] = power.Target;
            count++;
        }
        // BespokeCardMirrors.MiseryOnPlay: a temporary power's amount is also merged into the
        // internally applied stat when that stat is itself among the cloned debuffs. The temporary
        // power row is still cloned (the native ApplyClonedPower skips BeforeApplied), exactly as the
        // existing engine does.
        for (int index = 0; index < count; index++)
        {
            FlashPowerDef def = FlashRegistry.Power(ids[index]);
            if (!def.IsTemporaryPower || def.InternallyAppliedPowerId is not { } internalId)
                continue;
            if (!FlashRegistry.TryPower(internalId, out ushort internalPower))
                continue;
            for (int targetIndex = 0; targetIndex < count; targetIndex++)
            {
                if (ids[targetIndex] == internalPower)
                {
                    amounts[targetIndex] = checked(amounts[targetIndex] + amounts[index]);
                    break;
                }
            }
        }
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        byte original = x.Target;
        // The existing engine iterates a HittableEnemies snapshot; a cloned debuff can kill its
        // target (Sleight of Flesh) and compact the live roster, which must not skip later enemies.
        Span<byte> enemies = stackalloc byte[FlashLimits.Enemies];
        int rosterCount = x.B.RosterCount;
        for (int position = 0; position < rosterCount; position++)
            enemies[position] = FlashKernel.EnemyOwner(x.B.Roster[position]);
        for (int position = 0; position < rosterCount; position++)
        {
            byte enemy = enemies[position];
            if (enemy == original || !FlashKernel.IsHittable(x.B, enemy))
                continue;
            for (int item = 0; item < count; item++)
            {
                FlashKernel.ApplyPower(ref x, enemy, ids[item], amounts[item], appliers[item], targets[item]);
                if (x.NeedsChoice)
                    return;
                FlashKernel.ResolvePowerAmountChanges(ref x);
                if (x.NeedsChoice)
                    return;
            }
        }
    }
}

/// <summary>
/// <c>Hang</c>: attack, then apply HangPower with max(2, current amount) and the 999,999,999 cap
/// (<c>CorePowerSupport</c>'s Hang case).
/// </summary>
[FlashContent("HANG")]
public sealed class Hang : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        ushort hang = FlashRegistry.PowerId<Powers.HangPower>();
        int current = FlashKernel.GetAmount(x.B, x.Target, hang);
        int amount = Math.Max(2, current);
        if (current + amount > 999_999_999)
            amount = Math.Max(0, 999_999_999 - current);
        FlashKernel.ApplyPower(ref x, x.Target, hang, amount, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Eradicate</c>: an X-cost attack whose hit count is the resolved X value
/// (<c>GeneralCardMirrors</c> uses <c>ResolveEnergyXValue</c> for <c>AttackCommand.WithHitCount</c>).
/// </summary>
[FlashContent("ERADICATE")]
public sealed class Eradicate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, NecrobinderContent.Var(ref x, 0), FlashKernel.ResolveEnergyXValue(ref x));
}

/// <summary>
/// <c>BansheesCry</c>: attack all enemies; its cost drops by Energy for every Ethereal card played and
/// for every Ethereal play already in this combat when it enters combat
/// (<c>AfterCardPlayedMirrors.HandleBansheesCry</c>, <c>SimulatedCombatState.CardEventHistory</c>).
/// </summary>
[FlashContent("BANSHEES_CRY")]
public sealed class BansheesCry : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id || !FlashKernel.HasKeyword(x.B, play.Card, FlashKeywords.Ethereal))
            return;
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        if (energy > 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, -energy, FlashCostType.Relative,
                FlashCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }

    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        if (card.Id != Id || card.IsClone)
            return;
        int plays = x.B.Counter(FlashCounter.EtherealPlays);
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        int reduction = plays * energy;
        if (reduction > 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, -reduction, FlashCostType.Relative,
                FlashCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }
}

/// <summary>
/// <c>TheScythe</c>: attack with its accumulated Damage variable, then add the Increase variable to
/// the played card's Damage (and record the deck-version long-term growth:
/// <c>BespokeCardMirrors.TheScytheOnPlay</c>). The accumulated value is the imported Damage-variable
/// delta (Var0), so no private counter is needed.
/// </summary>
[FlashContent("THE_SCYTHE")]
public sealed class TheScythe : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // TheScythe.AfterDowngraded calls UpdateDamage() with IncreasedDamage; Var0 is that accumulator.
    public override bool PreservesVar0OnDowngrade => true;

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // The native OnPlay reads the live card's Increase after the attack (Dampen restore may have
        // upgraded the played copy in the meantime).
        FlashKernel.SyncCurrentPlayCard(ref x);
        int increase = (int)NecrobinderContent.Var(ref x, 1);
        FlashCardEffects.AddCurrentCardVar(ref x, 0, increase);
        if ((x.Card.Flags & FlashCardFlags.DeckCard) != 0)
        {
            x.B.AddCounter(FlashCounter.LongTermResourceValue, increase);
            FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.TheScythe);
        }
    }
}
