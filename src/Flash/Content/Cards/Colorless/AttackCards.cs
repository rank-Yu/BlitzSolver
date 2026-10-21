using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Colorless;

// Colorless attack cards. Existing engine paths for this batch:
// - DramaticEntrance, UltimateStrike and Salvo infer the Attack recipe (Salvo is an
//   EngineInferred method recipe), then CardOnPlaySupport adds Salvo's RetainHandPower stack.
// - FlashOfSteel is CardDrawCardMirrors' inferred attack + owner draw.
// - MindBlast and Rend are CardCalculatedVar attacks: CalculationBase + ExtraDamage * multiplier,
//   resolved by CalculatedVarSpecRegistry (draw pile count / target non-temporary debuff powers).
// - Volley is RandomTargetAttackCardMirrors.VolleyOnPlay: ResolveEnergyXValue hits on random
//   hittable opponents, one CombatTargets pick per hit.

[FlashContent("DRAMATIC_ENTRANCE")]
public sealed class DramaticEntrance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("FLASH_OF_STEEL")]
public sealed class FlashOfSteel : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("MIND_BLAST")]
public sealed class MindBlast : FlashCardDef
{
    // Native CalculatedDamageVar: CalculationBase + ExtraDamage * draw pile card count.
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * x.B.DrawCount;
        FlashCardEffects.Attack(ref x, damage);
    }

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        FlashCardStats stats = x.R.Card(self.Id, self.Upgrade);
        damage = stats.Vars[0] + self.Var0 + (stats.Vars[1] + self.Var1) * x.B.DrawCount;
        return true;
    }
}

[FlashContent("REND")]
public sealed class Rend : FlashCardDef
{
    // Native CalculatedDamageVar: CalculationBase + ExtraDamage * the target's non-temporary
    // debuff count (CalculatedVarSpecRegistry.TryMultiplier, Rend case).
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        FlashCardStats stats = x.R.Card(self.Id, self.Upgrade);
        damage = stats.Vars[0] + self.Var0
            + (stats.Vars[1] + self.Var1) * CountTargetDebuffs(x, target);
        return true;
    }

    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * CountTargetDebuffs(x, x.Target);
        FlashCardEffects.Attack(ref x, damage);
    }

    private static int CountTargetDebuffs(in FlashExec x, byte target)
    {
        int debuffs = 0;
        if (target == FlashLimits.NoOwner)
            return 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Owner != target)
                continue;
            FlashPowerDef def = FlashRegistry.Power(power.Id);
            if (def.TypeForAmount(power.Amount) == FlashPowerType.Debuff && !def.IsTemporaryPower)
                debuffs++;
        }
        return debuffs;
    }
}

[FlashContent("SALVO")]
public sealed class Salvo : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("ULTIMATE_STRIKE")]
public sealed class UltimateStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("VOLLEY")]
public sealed class Volley : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        // HasEnergyCostX: one random-target hit per energy spent (ResolveEnergyXValue).
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), FlashKernel.ResolveEnergyXValue(ref x));
    }
}

[FlashContent("FISTICUFFS")]
public sealed class Fisticuffs : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CorePowerSupport's Fisticuffs case: the mirrored attack, the deferred enemy-death pass (the
    // existing engine settles deaths before this compensation), then block equal to the sum of this
    // card's damage results (TotalDamage + OverkillDamage).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.CardDamageTotal > 0)
            FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.CardDamageTotal, FlashValueProps.Move, fromCard: true);
    }
}

[FlashContent("GOLD_AXE")]
public sealed class GoldAxe : FlashCardDef
{
    // CalculatedDamage: CalculationBase + ExtraDamage * finished card plays this combat
    // (CalculatedVarSpecRegistry's GoldAxe multiplier).
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * x.B.Counter(FlashCounter.FinishedCardPlays);
        FlashCardEffects.Attack(ref x, damage);
    }

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        FlashCardStats stats = x.R.Card(self.Id, self.Upgrade);
        damage = stats.Vars[0] + self.Var0
            + (stats.Vars[1] + self.Var1) * x.B.Counter(FlashCounter.FinishedCardPlays);
        return true;
    }
}

[FlashContent("HAND_OF_GREED")]
public sealed class HandOfGreed : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Gold"];

    // CorePowerSupport's HandOfGreed case: mirrored attack, enemy-death pass, then gold when this
    // card's hit killed the target and every target power allows a fatal kill.
    public override void OnPlay(ref FlashExec x)
    {
        // CorePowerSupport captures targetDeathTriggersFatal before the card's effects run, so the
        // check must not read the powers after the target's death cleanup removed them.
        byte target = x.Target;
        bool fatal = target != FlashLimits.NoOwner && FlashKernel.TargetDeathTriggersFatal(x.B, target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || !x.CardKilledTarget || target == FlashLimits.NoOwner)
            return;
        if (x.B.Creature(target).IsAlive || !fatal)
            return;
        int gold = (int)FlashCardEffects.Var(x, 1);
        if (gold > 0)
        {
            // CombatPredictionSimulator.GainPlayerGold (the gold-modifier relics are not modeled; roots
            // holding them are outside this content set's evidence).
            x.B.Gold = (int)Math.Clamp((long)x.B.Gold + gold, 0, FlashLimits.ValueClamp);
            x.B.AddCounter(FlashCounter.LongTermResourceValue, gold);
        }
        // CorePowerSupport records the growth reward after the gold gain regardless of its clamp.
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.HandOfGreed);
    }
}

[FlashContent("OMNISLICE")]
public sealed class Omnislice : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardOnPlaySupport.Batch042's ApplyOmnislice: hit the target, settle deaths, then deal the hit's
    // TotalDamage + OverkillDamage to every other hittable enemy as Unpowered|Move.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int amount = x.LastDamageTotal + x.LastDamageOverkill;
        // CardOnPlaySupport.Batch042.ApplyOmnislice passes combat.Enemies (current roster) to the
        // death pass, so a lethal hit that already left the roster does not spawn Infested minions
        // before the copied hit is resolved.
        FlashKernel.ApplyEnemyDeathPowersInRoster(ref x);
        if (x.NeedsChoice || amount <= 0)
            return;
        Span<byte> targets = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (owner == x.Target || !FlashKernel.IsHittable(x.B, owner))
                continue;
            targets[count++] = owner;
        }
        if (count == 0)
            return;
        FlashKernel.Damage(ref x, targets[..count], amount,
            FlashValueProps.Unpowered | FlashValueProps.Move, FlashLimits.PlayerOwner,
            hasCardSource: true, x.Card);
        if (!x.NeedsChoice)
            FlashKernel.ApplyEnemyDeathPowersInRoster(ref x);
    }
}

[FlashContent("SEEKER_STRIKE")]
public sealed class SeekerStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    // CardChoiceSupport.BuildSeekerSpec: StableShuffle the draw pile with CombatCardSelection, take
    // Cards of them, present the whole draw pile filtered to that sample in pile order, then move the
    // chosen card to hand.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || x.B.DrawCount == 0)
            return;
        int draw = x.B.DrawCount;
        Span<FlashCard> order = stackalloc FlashCard[draw];
        Span<int> origins = stackalloc int[draw];
        FlashPiles.ReadCards(x.B, FlashPile.Draw).CopyTo(order);
        for (int index = 0; index < draw; index++)
            origins[index] = index;
        FlashCardSort.SortWithOrigins(order, origins, x.R);
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.CombatCardSelection);
        for (int n = draw; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
        int take = Math.Min((int)FlashCardEffects.Var(x, 1), draw);
        Span<bool> sampled = stackalloc bool[draw];
        for (int index = 0; index < take; index++)
            sampled[origins[index]] = true;
        Span<int> options = stackalloc int[draw];
        int optionCount = 0;
        for (int index = 0; index < draw; index++)
        {
            if (sampled[index])
                options[optionCount++] = index;
        }
        if (optionCount == 0)
            return;
        // CardChoiceSupport.BuildSeekerSpec records RecordRisk(UnresolvedPlayerChoice); the risk is
        // not in the compensated catalog, so the snapshot flips HasRisk and Flash must follow.
        x.B.Flags |= FlashBattleFlags.HasRisk;
        x.HistoryRisk = true;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw,
                options[..optionCount], 1, 1, selected, out int selectedCount))
            return;
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..selectedCount], FlashPile.Hand,
            FlashPilePosition.Bottom);
    }
}

[FlashContent("BOLAS")]
public sealed class Bolas : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardSelectionCardMirrors.BolasOnPlay (attack) plus SimulatedCombatState.RecordCardLifecycle's
    // ReturnsToHandAfterPlaying: the played card joins _returnToHandNextTurn.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int playPosition = x.B.PlayCount - 1;
        if (playPosition >= 0 && x.B.Cards[FlashPiles.Start(x.B, FlashPile.Play) + playPosition].Id == x.Card.Id)
            FlashPiles.At(ref x.B, FlashPile.Play, playPosition).ReturnToHandNextTurn = true;
    }
}

[FlashContent("THRUMMING_HATCHET")]
public sealed class ThrummingHatchet : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int playPosition = x.B.PlayCount - 1;
        if (playPosition >= 0 && x.B.Cards[FlashPiles.Start(x.B, FlashPile.Play) + playPosition].Id == x.Card.Id)
            FlashPiles.At(ref x.B, FlashPile.Play, playPosition).ReturnToHandNextTurn = true;
    }
}
