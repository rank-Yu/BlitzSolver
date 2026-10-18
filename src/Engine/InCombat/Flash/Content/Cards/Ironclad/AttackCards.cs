
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
