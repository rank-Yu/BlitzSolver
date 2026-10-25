using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Colorless;

[BlitzContent("DRAMATIC_ENTRANCE")]
public sealed class DramaticEntrance : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("FLASH_OF_STEEL")]
public sealed class FlashOfSteel : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers settles newly dead enemies after the mirror attack and
        // before the card-specific draw compensation; HittableEnemies/Hellraiser read that phase.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[BlitzContent("MIND_BLAST")]
public sealed class MindBlast : BlitzCardDef
{
    // Native CalculatedDamageVar: CalculationBase + ExtraDamage * draw pile card count.
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        decimal damage = BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * x.B.DrawCount;
        BlitzCardEffects.Attack(ref x, damage);
    }

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        BlitzCardStats stats = x.R.Card(self.Id, self.Upgrade);
        damage = stats.Vars[0] + self.Var0 + (stats.Vars[1] + self.Var1) * x.B.DrawCount;
        return true;
    }
}

[BlitzContent("REND")]
public sealed class Rend : BlitzCardDef
{
    // Native CalculatedDamageVar: CalculationBase + ExtraDamage * the target's non-temporary
    // debuff count (CalculatedVarSpecRegistry.TryMultiplier, Rend case).
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        BlitzCardStats stats = x.R.Card(self.Id, self.Upgrade);
        damage = stats.Vars[0] + self.Var0
            + (stats.Vars[1] + self.Var1) * CountTargetDebuffs(x, target);
        return true;
    }

    public override void OnPlay(ref BlitzExec x)
    {
        decimal damage = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * CountTargetDebuffs(x, x.Target);
        BlitzCardEffects.Attack(ref x, damage);
    }

    private static int CountTargetDebuffs(in BlitzExec x, byte target)
    {
        int debuffs = 0;
        if (target == BlitzLimits.NoOwner)
            return 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly BlitzPower power = ref x.B.Powers[index];
            if (power.Owner != target)
                continue;
            BlitzPowerDef def = BlitzRegistry.Power(power.Id);
            if (def.TypeForAmount(power.Amount) == BlitzPowerType.Debuff && !def.IsTemporaryPower)
                debuffs++;
        }
        return debuffs;
    }
}

[BlitzContent("SALVO")]
public sealed class Salvo : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RetainHandPower>(),
            1, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("ULTIMATE_STRIKE")]
public sealed class UltimateStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

[BlitzContent("VOLLEY")]
public sealed class Volley : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        // HasEnergyCostX: one random-target hit per energy spent (ResolveEnergyXValue).
        BlitzCardEffects.Attack(ref x, BlitzCardEffects.Var(x, 0), BlitzKernel.ResolveEnergyXValue(ref x));
    }
}

[BlitzContent("FISTICUFFS")]
public sealed class Fisticuffs : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CorePowerSupport's Fisticuffs case: the mirrored attack, the deferred enemy-death pass (the
    // existing engine settles deaths before this compensation), then block equal to the sum of this
    // card's damage results (TotalDamage + OverkillDamage).
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.CardDamageTotal > 0)
            BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.CardDamageTotal, BlitzValueProps.Move, fromCard: true);
    }
}

[BlitzContent("GOLD_AXE")]
public sealed class GoldAxe : BlitzCardDef
{
    // CalculatedDamage: CalculationBase + ExtraDamage * finished card plays this combat
    // (CalculatedVarSpecRegistry's GoldAxe multiplier).
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        decimal damage = BlitzCardEffects.Var(x, 0)
            + BlitzCardEffects.Var(x, 1) * x.B.Counter(BlitzCounter.FinishedCardPlays);
        BlitzCardEffects.Attack(ref x, damage);
    }

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        BlitzCardStats stats = x.R.Card(self.Id, self.Upgrade);
        damage = stats.Vars[0] + self.Var0
            + (stats.Vars[1] + self.Var1) * x.B.Counter(BlitzCounter.FinishedCardPlays);
        return true;
    }
}

[BlitzContent("HAND_OF_GREED")]
public sealed class HandOfGreed : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Gold"];

    // CorePowerSupport's HandOfGreed case: mirrored attack, enemy-death pass, then gold when this
    // card's hit killed the target and every target power allows a fatal kill.
    public override void OnPlay(ref BlitzExec x)
    {
        // CorePowerSupport captures targetDeathTriggersFatal before the card's effects run, so the
        // check must not read the powers after the target's death cleanup removed them.
        byte target = x.Target;
        bool fatal = target != BlitzLimits.NoOwner && BlitzKernel.TargetDeathTriggersFatal(x.B, target);
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice || !x.CardKilledTarget || target == BlitzLimits.NoOwner)
            return;
        if (x.B.Creature(target).IsAlive || !fatal)
            return;
        int gold = (int)BlitzCardEffects.Var(x, 1);
        if (gold > 0)
        {
            // CombatPredictionSimulator.GainPlayerGold (the gold-modifier relics are not modeled; roots
            // holding them are outside this content set's evidence).
            x.B.Gold = (int)Math.Clamp((long)x.B.Gold + gold, 0, BlitzLimits.ValueClamp);
            x.B.AddCounter(BlitzCounter.LongTermResourceValue, gold);
        }
        // CorePowerSupport records the growth reward after the gold gain regardless of its clamp.
        BlitzGrowthRewards.Add(ref x.B, BlitzGrowthSource.HandOfGreed);
    }
}

[BlitzContent("OMNISLICE")]
public sealed class Omnislice : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardOnPlaySupport.Batch042's ApplyOmnislice: hit the target, settle deaths, then deal the hit's
    // TotalDamage + OverkillDamage to every other hittable enemy as Unpowered|Move.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int amount = x.LastDamageTotal + x.LastDamageOverkill;
        // CardOnPlaySupport.Batch042.ApplyOmnislice passes combat.Enemies (current roster) to the
        // death pass, so a lethal hit that already left the roster does not spawn Infested minions
        // before the copied hit is resolved.
        BlitzKernel.ApplyEnemyDeathPowersInRoster(ref x);
        if (x.NeedsChoice || amount <= 0)
            return;
        Span<byte> targets = stackalloc byte[BlitzLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (owner == x.Target || !BlitzKernel.IsHittable(x.B, owner))
                continue;
            targets[count++] = owner;
        }
        if (count == 0)
            return;
        BlitzKernel.Damage(ref x, targets[..count], amount,
            BlitzValueProps.Unpowered | BlitzValueProps.Move, BlitzLimits.PlayerOwner,
            hasCardSource: true, x.Card);
        if (!x.NeedsChoice)
            BlitzKernel.ApplyEnemyDeathPowersInRoster(ref x);
    }
}

[BlitzContent("SEEKER_STRIKE")]
public sealed class SeekerStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    // CardChoiceSupport.BuildSeekerSpec: StableShuffle the draw pile with CombatCardSelection, take
    // Cards of them, present the whole draw pile filtered to that sample in pile order, then move the
    // chosen card to hand.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || x.B.DrawCount == 0)
            return;
        int draw = x.B.DrawCount;
        Span<BlitzCard> order = stackalloc BlitzCard[draw];
        Span<int> origins = stackalloc int[draw];
        BlitzPiles.ReadCards(x.B, BlitzPile.Draw).CopyTo(order);
        for (int index = 0; index < draw; index++)
            origins[index] = index;
        BlitzCardSort.SortWithOrigins(order, origins, x.R);
        ref BlitzRngState rng = ref x.B.RngStream(BlitzRngStream.CombatCardSelection);
        for (int n = draw; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (origins[k], origins[n]) = (origins[n], origins[k]);
        }
        int take = Math.Min((int)BlitzCardEffects.Var(x, 1), draw);
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
        // not in the compensated catalog, so the snapshot flips HasRisk and Blitz must follow.
        x.B.Flags |= BlitzBattleFlags.HasRisk;
        x.HistoryRisk = true;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Draw,
                options[..optionCount], 1, 1, selected, out int selectedCount))
            return;
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, selected[..selectedCount], BlitzPile.Hand,
            BlitzPilePosition.Bottom);
    }
}

[BlitzContent("BOLAS")]
public sealed class Bolas : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardSelectionCardMirrors.BolasOnPlay (attack) plus SimulatedCombatState.RecordCardLifecycle's
    // ReturnsToHandAfterPlaying: the played card joins _returnToHandNextTurn.
    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int playPosition = x.B.PlayCount - 1;
        if (playPosition >= 0 && x.B.Cards[BlitzPiles.Start(x.B, BlitzPile.Play) + playPosition].Id == x.Card.Id)
            BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition).ReturnToHandNextTurn = true;
    }
}

[BlitzContent("THRUMMING_HATCHET")]
public sealed class ThrummingHatchet : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int playPosition = x.B.PlayCount - 1;
        if (playPosition >= 0 && x.B.Cards[BlitzPiles.Start(x.B, BlitzPile.Play) + playPosition].Id == x.Card.Id)
            BlitzPiles.At(ref x.B, BlitzPile.Play, playPosition).ReturnToHandNextTurn = true;
    }
}
