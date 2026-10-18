
namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Ironclad;

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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("WHIRLWIND")]
public sealed class Whirlwind : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: x.XValue);
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
            int handBefore = x.B.HandCount;
            FlashKernel.Draw(ref x, 1, fromHandDraw: false);
            if (x.B.HandCount >= x.B.MaxHandSize || x.B.HandCount == handBefore)
                break;
            FlashCard drawn = FlashPiles.ReadCards(x.B, FlashPile.Hand)[x.B.HandCount - 1];
            if (x.R.Card(drawn.Id, drawn.Upgrade).Type != FlashCardType.Attack)
                break;
        }
    }
}

[FlashContent("BULLY")]
public sealed class Bully : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

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
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || target == FlashLimits.NoOwner || x.B.Creature(target).IsAlive)
            return;
        // CorePowerSupport.Feed only gains MaxHp after a fatal hit and only when every power of the
        // target lets its owner's death trigger fatal (MinionPower is the supported such power).
        if (!FlashKernel.IsPrimaryEnemy(x.B, target))
            return;
        FlashKernel.GainMaxHp(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 1));
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

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
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
