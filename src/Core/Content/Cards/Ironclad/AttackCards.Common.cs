using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Ironclad;

[BlitzContent("BODY_SLAM")]
public sealed class BodySlam : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x,
            BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * x.B.Player.Block);

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        damage = SelfVar(x, self, 0) + SelfVar(x, self, 1) * x.B.Player.Block;
        return true;
    }

    internal static decimal SelfVar(in BlitzExec x, in BlitzCard card, int index)
    {
        BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
        decimal delta = index == 0 ? card.Var0 : card.Var1;
        return stats.Vars[index] + delta;
    }
}

[BlitzContent("BREAKTHROUGH")]
public sealed class Breakthrough : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "HpLoss"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.LoseHp(ref x, BlitzCardEffects.Var(x, 1));
    }
}

[BlitzContent("PERFECTED_STRIKE")]
public sealed class PerfectedStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.Attack(ref x,
            BlitzCardEffects.Var(x, 0) + BlitzCardEffects.Var(x, 1) * BlitzCardEffects.CountCardsWithTag(x, BlitzCardTag.Strike));

    public override bool TryGetCalculatedDamage(ref BlitzExec x, in BlitzCard self, byte target, out decimal damage)
    {
        damage = BodySlam.SelfVar(x, self, 0)
            + BodySlam.SelfVar(x, self, 1) * BlitzCardEffects.CountCardsWithTag(x, BlitzCardTag.Strike);
        return true;
    }
}

[BlitzContent("SWORD_BOOMERANG")]
public sealed class SwordBoomerang : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: (int)BlitzCardEffects.Var(x, 1));
}

[BlitzContent("CINDER")]
public sealed class Cinder : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardSelectionCardMirrors.CinderOnPlay: Rng.CombatCardSelection.NextItem over the hand.
        if (x.B.HandCount == 0)
            return;
        int index = x.B.RngStream(BlitzRngStream.CombatCardSelection).NextInt(x.B.HandCount);
        BlitzKernel.Exhaust(ref x, BlitzPile.Hand, index);
    }
}

[BlitzContent("MOLTEN_FIST")]
public sealed class MoltenFist : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.Target == BlitzLimits.NoOwner || !x.B.Creature(x.Target).IsAlive)
            return;
        ushort vulnerablePower = BlitzRegistry.PowerId<Powers.VulnerablePower>();
        int vulnerable = BlitzKernel.GetAmount(x.B, x.Target, vulnerablePower);
        if (vulnerable > 0)
            BlitzKernel.ApplyPower(ref x, x.Target, vulnerablePower, vulnerable, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("ANGER")]
public sealed class Anger : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("ANGER", 0), ("ANGER", 1)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack and
        // before the card-specific compensation. The death pass can remove the last DampenPower
        // caster, which restores the played card's original upgrade before the clone is made.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        // The native OnPlay clones the live CardModel after that restore, while BlitzExec.Card is
        // the pre-restore copy taken when the card left the hand.
        BlitzKernel.SyncCurrentPlayCard(ref x);
        // CorePowerSupport.Anger adds a clone of the played card to the discard pile.
        BlitzKernel.AddGeneratedCard(ref x, BlitzKernel.CloneCard(x.Card), BlitzPile.Discard);
        if (x.NeedsChoice)
            return;
        x.B.AddCounter(BlitzCounter.AngerCopiesGenerated, 1);
    }
}

[BlitzContent("SETUP_STRIKE")]
public sealed class SetupStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "StrengthPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // CardEffectSpecRegistry: ApplyTemporaryStrengthGain<SetupStrikePower>.
        BlitzKernel.ApplyTemporaryStrength(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.SetupStrikePower>(),
            (int)BlitzCardEffects.Var(x, 1), BlitzLimits.PlayerOwner, sign: 1);
    }
}

[BlitzContent("TWIN_STRIKE")]
public sealed class TwinStrike : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0, hitCount: 2);
}

[BlitzContent("IRON_WAVE")]
public sealed class IronWave : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 1);
        BlitzCardEffects.GeneralAttack(ref x, 0);
    }
}

[BlitzContent("POMMEL_STRIKE")]
public sealed class PommelStrike : BlitzCardDef
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

[BlitzContent("THUNDERCLAP")]
public sealed class Thunderclap : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        // ApplyCardPowers settles newly dead enemies after the mirrored attack and before its
        // card-specific switch; Thunderclap's Vulnerable must therefore see any Wrigglers spawned by
        // the Phrog Parasite the attack just killed.
        BlitzKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToAllEnemies(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

[BlitzContent("HEADBUTT")]
public sealed class Headbutt : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: MoveToDrawTop, Discard, 1, the discard pile without the played card (which is in
        // the play pile during resolution).
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.MoveToDrawTop, BlitzPile.Discard, options, 1, selected, out int chosen))
            return;
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..chosen], BlitzPile.Draw, BlitzPilePosition.Top);
    }
}
