using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Ironclad;

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

[FlashContent("SWORD_BOOMERANG")]
public sealed class SwordBoomerang : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: (int)FlashCardEffects.Var(x, 1));
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

[FlashContent("TWIN_STRIKE")]
public sealed class TwinStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
}

[FlashContent("IRON_WAVE")]
public sealed class IronWave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        FlashCardEffects.GeneralAttack(ref x, 0);
    }
}

[FlashContent("POMMEL_STRIKE")]
public sealed class PommelStrike : FlashCardDef
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

[FlashContent("THUNDERCLAP")]
public sealed class Thunderclap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // ApplyCardPowers settles newly dead enemies after the mirrored attack and before its
        // card-specific switch; Thunderclap's Vulnerable must therefore see any Wrigglers spawned by
        // the Phrog Parasite the attack just killed.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[FlashContent("HEADBUTT")]
public sealed class Headbutt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: MoveToDrawTop, Discard, 1, the discard pile without the played card (which is in
        // the play pile during resolution).
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToDrawTop, FlashPile.Discard, options, 1, selected, out int chosen))
            return;
        FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Draw, FlashPilePosition.Top);
    }
}
