using CombatSolver.Flash.Kernel;


namespace CombatSolver.Flash.Content.Cards.Ironclad;

// Existing engine paths for this batch:
// - Bludgeon, Break, BodySlam, PerfectedStrike, Bully and AshenStrike run the strict inferred attack
//   recipe; CalculatedDamage is resolved by CalculatedVarSpecRegistry after the mirror.
// - Breakthrough and Hemokinesis infer only the attack; CardEffectSpecRegistry applies the self
//   HpLoss afterwards, so Flash also attacks before losing HP.
// - SwordBoomerang, Pillage, Cinder and Dismantle are explicit OnPlay registrations.
// - Whirlwind, Conflagration and FightMe infer the attack; CorePowerSupport applies FightMe's
//   Strength afterwards.
// - MoltenFist infers the attack; CardEffectSpecRegistry re-applies the target's Vulnerable after it.

[FlashContent("BLUDGEON")]
public sealed class Bludgeon : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("BODY_SLAM")]
public sealed class BodySlam : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x,
            FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * x.B.Player.Block);

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        damage = SelfVar(x, self, 0) + SelfVar(x, self, 1) * x.B.Player.Block;
        return true;
    }

    internal static decimal SelfVar(in FlashExec x, in FlashCard card, int index)
    {
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        decimal delta = index == 0 ? card.Var0 : card.Var1;
        return stats.Vars[index] + delta;
    }
}

[FlashContent("BREAKTHROUGH")]
public sealed class Breakthrough : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "HpLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.LoseHp(ref x, FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("HEMOKINESIS")]
public sealed class Hemokinesis : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss", "Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.LoseHp(ref x, FlashCardEffects.Var(x, 0));
    }
}

[FlashContent("PERFECTED_STRIKE")]
public sealed class PerfectedStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x,
            FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * FlashCardEffects.CountCardsWithTag(x, FlashCardTag.Strike));

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        damage = BodySlam.SelfVar(x, self, 0)
            + BodySlam.SelfVar(x, self, 1) * FlashCardEffects.CountCardsWithTag(x, FlashCardTag.Strike);
        return true;
    }
}

[FlashContent("BREAK")]
public sealed class Break : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("WHIRLWIND")]
public sealed class Whirlwind : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: FlashKernel.ResolveEnergyXValue(ref x));
}

[FlashContent("SWORD_BOOMERANG")]
public sealed class SwordBoomerang : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("PILLAGE")]
public sealed class Pillage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardDrawCardMirrors.PillageOnPlay: draw one card at a time until the drawn card is not an
        // Attack or the hand is full.
        while (true)
        {
            if (!FlashKernel.TryDrawOne(ref x, fromHandDraw: false, out FlashCard drawn))
                return;
            if (x.B.HandCount >= x.B.MaxHandSize)
                return;
            // CardDrawCardMirrors.PillageOnPlay tests the returned drawn card, which stays the same
            // value even when AfterCardDrawn auto-played (Hellraiser) and removed it from hand.
            if (FlashKernel.CardType(x.B, x.R, drawn) != FlashCardType.Attack)
                return;
        }
    }
}

[FlashContent("BULLY")]
public sealed class Bully : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        int vulnerable = target == FlashLimits.NoOwner
            ? 0
            : FlashKernel.GetAmount(x.B, target, FlashRegistry.PowerId<Powers.VulnerablePower>());
        damage = BodySlam.SelfVar(x, self, 0) + BodySlam.SelfVar(x, self, 1) * vulnerable;
        return true;
    }

    public override void OnPlay(ref FlashExec x)
    {
        int vulnerable = x.Target == FlashLimits.NoOwner
            ? 0
            : FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>());
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * vulnerable);
    }
}

[FlashContent("DISMANTLE")]
public sealed class Dismantle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        int vulnerable = x.Target == FlashLimits.NoOwner
            ? 0
            : FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>());
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: vulnerable > 0 ? 2 : 1);
    }
}

[FlashContent("ASHEN_STRIKE")]
public sealed class AshenStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x,
            FlashCardEffects.Var(x, 0) + FlashCardEffects.Var(x, 1) * x.B.ExhaustCount);

    public override bool TryGetCalculatedDamage(ref FlashExec x, in FlashCard self, byte target, out decimal damage)
    {
        damage = BodySlam.SelfVar(x, self, 0) + BodySlam.SelfVar(x, self, 1) * x.B.ExhaustCount;
        return true;
    }
}

[FlashContent("CINDER")]
public sealed class Cinder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardSelectionCardMirrors.CinderOnPlay: Rng.CombatCardSelection.NextItem over the hand.
        if (x.B.HandCount == 0)
            return;
        int index = x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(x.B.HandCount);
        FlashKernel.Exhaust(ref x, FlashPile.Hand, index);
    }
}

[FlashContent("MOLTEN_FIST")]
public sealed class MoltenFist : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.Target == FlashLimits.NoOwner || !x.B.Creature(x.Target).IsAlive)
            return;
        ushort vulnerablePower = FlashRegistry.PowerId<Powers.VulnerablePower>();
        int vulnerable = FlashKernel.GetAmount(x.B, x.Target, vulnerablePower);
        if (vulnerable > 0)
            FlashKernel.ApplyPower(ref x, x.Target, vulnerablePower, vulnerable, FlashLimits.PlayerOwner);
    }
}

[FlashContent("FIGHT_ME")]
public sealed class FightMe : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat", "StrengthPower", "EnemyStrength"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        ushort strength = FlashRegistry.PowerId<Powers.StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength,
            (int)FlashCardEffects.Var(x, 2), FlashLimits.PlayerOwner);
        FlashCardEffects.ApplyToTarget(ref x, strength, 3);
    }
}

[FlashContent("CONFLAGRATION")]
public sealed class Conflagration : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("FEED")]
public sealed class Feed : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "MaxHp"];

    public override void OnPlay(ref FlashExec x)
    {
        byte target = x.Target;
        // CorePowerSupport captures targetDeathTriggersFatal before the card's effects run. MinionPower
        // is the only supported power that rejects a fatal kill, and it does so by making the owner a
        // secondary enemy, which IsPrimaryEnemy reports.
        bool fatal = target != FlashLimits.NoOwner && FlashKernel.IsPrimaryEnemy(x.B, target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || target == FlashLimits.NoOwner || x.B.Creature(target).IsAlive)
            return;
        // CorePowerSupport.Feed only gains MaxHp after a fatal hit and only when every power of the
        // target lets its owner's death trigger fatal (MinionPower is the supported such power).
        if (!fatal)
            return;
        FlashKernel.GainMaxHp(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 1));
        FlashGrowthRewards.Add(ref x.B, FlashGrowthSource.Feed);
    }
}

[FlashContent("ANGER")]
public sealed class Anger : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("ANGER", 0), ("ANGER", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.Anger adds a clone of the played card to the discard pile.
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CloneCard(x.Card), FlashPile.Discard);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(FlashCounter.AngerCopiesGenerated, 1);
    }
}

[FlashContent("PACTS_END")]
public sealed class PactsEnd : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        // BespokeCardMirrors.PactsEndOnPlay only attacks when the exhaust pile already has the
        // CardsVar amount; the card itself stays in the play pile during this check.
        if (x.B.ExhaustCount < (int)FlashCardEffects.Var(x, 1))
            return;
        FlashCardEffects.GeneralAttack(ref x, 0);
    }
}

// P4 batch:
// - Spite is BespokeCardMirrors.SpiteOnPlay: one or Repeat hits depending on the owner's
//   LostHpThisTurn flag (flash keeps that flag on the player creature).
// - Mangle and SetupStrike infer the attack; CardEffectSpecRegistry then applies the temporary-strength
//   power through ApplyTemporaryStrength (Artifact, BeforeApplied and the stacking Strength change).
// - Rampage infers the attack; CardEffectSpecRegistry then adds Increase to the played card's Damage
//   var, which Flash stores as the card's first dynamic-variable delta.
// - FiendFire is BespokeCardMirrors.FiendFireOnPlay: exhaust a snapshot of the hand, then hit once
//   per card in that snapshot.

[FlashContent("SPITE")]
public sealed class Spite : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        bool lostHp = (x.B.Player.Flags & FlashCreatureFlags.LostHpThisTurn) != 0;
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: lostHp ? (int)FlashCardEffects.Var(x, 1) : 1);
    }
}

[FlashContent("MANGLE")]
public sealed class Mangle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        // CardEffectSpecRegistry: ApplyTemporaryStrengthLoss<ManglePower>.
        FlashKernel.ApplyTemporaryStrength(ref x, x.Target, FlashRegistry.PowerId<Powers.ManglePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner, sign: -1);
    }
}

[FlashContent("SETUP_STRIKE")]
public sealed class SetupStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry: ApplyTemporaryStrengthGain<SetupStrikePower>.
        FlashKernel.ApplyTemporaryStrength(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SetupStrikePower>(),
            (int)FlashCardEffects.Var(x, 1), FlashLimits.PlayerOwner, sign: 1);
    }
}

[FlashContent("RAMPAGE")]
public sealed class Rampage : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    // Rampage.AfterDowngraded re-adds _extraDamageFromPlays; the delta in Var0 is that accumulator.
    public override bool PreservesVar0OnDowngrade => true;

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // The native DynamicVars are read from the live CardModel after the attack; a mid-play
        // Dampen restore can upgrade this card before the Increase is added.
        FlashKernel.SyncCurrentPlayCard(ref x);
        FlashCardEffects.AddCurrentCardVar(ref x, 0, FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("FIEND_FIRE")]
public sealed class FiendFire : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        int cardCount = x.B.HandCount;
        for (int index = 0; index < cardCount; index++)
        {
            if (FlashKernel.IsOverOrEnding(x.B))
                return;
            FlashKernel.Exhaust(ref x, FlashPile.Hand, 0);
            if (x.NeedsChoice)
                return;
        }
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: cardCount);
    }
}

// P14 batch: Stomp is not defined. Its AllEnemies attack is the inferred recipe, but its two cost
// reactions (SimulatedCombatState.CardEventHistory.AfterCardEnteredCombat's Stomp case and
// BeforeCardPlayedMirrors.HandleStomp) each append one CardEnergyCost.AddThisTurn modifier per
// Attack played; FlashCard holds only FlashLimits.CardEnergyCostModifiers == 2 modifier slots and
// the importer maps the native list one-to-one, so a third Attack in the same turn overflows the
// card. Raising that capacity requires FlashBattle/FlashLimits (kernel core), which this batch may
// not change; see the P14 report's skip list.

[FlashContent("UNRELENTING")]
public sealed class Unrelenting : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // Unrelenting.cs: the single-target attack runs first, then FreeAttackPower 1 is applied.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FreeAttackPower>(),
            1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("STOMP")]
public sealed class Stomp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0);

    // Stomp.AfterCardEnteredCombat: reduce the cost by the Attacks finished this turn.
    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        int attacks = x.B.Counter(FlashCounter.AttacksPlayedThisTurn);
        if (attacks <= 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        AddThisTurn(ref card, -attacks);
    }

    // BeforeCardPlayedMirrors.HandleStomp: every Attack the owner plays reduces this card by 1.
    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (FlashKernel.CardType(x.B, x.R, play.Card) != FlashCardType.Attack)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        AddThisTurn(ref card, -1);
    }

    private static void AddThisTurn(ref FlashCard card, int amount)
    {
        if (amount == 0)
            return;
        if (card.EnergyModifierCount >= FlashLimits.CardEnergyCostModifiers)
            throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
        card.EnergyModifiers[card.EnergyModifierCount] = FlashCostModifier.Create(amount, FlashCostType.Relative,
            FlashCostExpiration.EndOfTurn, reduceOnly: false);
        card.EnergyModifierCount++;
    }
}

[FlashContent("TEAR_ASUNDER")]
public sealed class TearAsunder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CalculatedHits = 1 + History DamageReceived entries for the owner with unblocked damage.
    public override void OnPlay(ref FlashExec x)
    {
        int hits = 1 + x.B.Counter(FlashCounter.PlayerUnblockedDamageEvents);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("THRASH")]
public sealed class Thrash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // The native card adds ModifyDamage's decimal result to DynamicVars.Damage.BaseValue and to
    // the private ExtraDamage field, and AfterDowngraded re-adds ExtraDamage. Flash keeps the
    // exact delta in the battle's decimal table; downgrade leaves the table untouched.
    public override bool UsesDecimalDamageVar => true;

    public override void OnPlay(ref FlashExec x)
    {
        // Thrash.cs: two hits, then a random Attack in hand is read for its damage, that value is
        // added to this card's Damage (unmodified by Strength/Vulnerable because target is null in
        // the native ModifyDamage call), and the selected card is exhausted.
        FlashCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
        if (x.NeedsChoice)
            return;
        int selected = PickRandomHandAttack(ref x);
        if (selected < 0)
            return;
        FlashCard victim = x.B.Cards[selected];
        FlashCardDef def = FlashRegistry.Card(victim.Id);
        if (!def.TryGetCalculatedDamage(ref x, victim, FlashLimits.NoOwner, out decimal damage))
            damage = NamedDamage(ref x, victim, def);
        damage = FlashHooks.ModifyDamage(ref x, FlashLimits.NoOwner, FlashLimits.PlayerOwner, damage,
            FlashValueProps.Move, hasCardSource: true, victim);
        FlashCardEffects.AddCurrentCardVar(ref x, 0, damage);
        FlashKernel.Exhaust(ref x, FlashPile.Hand, selected);
    }

    private static int PickRandomHandAttack(ref FlashExec x)
    {
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (FlashKernel.CardType(x.B, x.R, x.B.Cards[index]) == FlashCardType.Attack)
                count++;
        }
        if (count == 0)
            return -1;
        int pick = x.B.RngStream(FlashRngStream.CombatCardSelection).NextInt(0, count);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (FlashKernel.CardType(x.B, x.R, x.B.Cards[index]) != FlashCardType.Attack)
                continue;
            if (pick-- == 0)
                return index;
        }
        throw new InvalidOperationException("Thrash 的手牌随机选择没有落到任何候选。");
    }

    private static decimal NamedDamage(ref FlashExec x, in FlashCard card, FlashCardDef def)
    {
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        for (int index = 0; index < def.VarNames.Count && index < stats.Vars.Length; index++)
        {
            string name = def.VarNames[index];
            if (name is not ("Damage" or "OstyDamage"))
                continue;
            return stats.Vars[index] + FlashDecimalVars.Delta(in x.B, in card, index);
        }
        throw new FlashUnsupportedException($"Thrash 选中的攻击 {def.NativeId} 没有 Damage 变量。");
    }
}
