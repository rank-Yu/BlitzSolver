namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Silent;

// Second Silent batch: X-cost, random-target, calculated-damage and the remaining simple powers.
// Existing engine: CardOnPlayInferrer's general attack recipe (which reads CalculatedHits /
// CalculatedDamage through CalculatedVarSpecRegistry), CardOnPlaySupport
// (Expose/BulletTime/Omnislice), CardPowerOnPlaySupport (Panache), CardEffectSpecRegistry (Pounce),
// EndTurnPowerSupport's temporary-strength retirement (PiercingWail), and
// RandomTargetAttackCardMirrors.RicochetOnPlay.

[FlashContent("EXPOSE")]
public sealed class Expose : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    // CardOnPlaySupport.Expose: lose all block as Move block damage, clear Artifact, then Vulnerable.
    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        ref FlashCreature target = ref x.B.Creature(x.Target);
        if (target.Block > 0)
            FlashKernel.DamageBlock(ref target, target.Block, FlashValueProps.Move);
        if (FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.ArtifactPower>()) > 0)
            FlashKernel.SetPowerAmount(ref x.B, x.Target, FlashRegistry.PowerId<Powers.ArtifactPower>(), 0);
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 0);
    }
}

[FlashContent("SKEWER")]
public sealed class Skewer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // GeneralCardMirrors.GeneralAttackOnPlay: an X-cost attack uses ResolveEnergyXValue as its hit
    // count (resolved once).
    public override void OnPlay(ref FlashExec x)
    {
        int hits = FlashKernel.ResolveEnergyXValue(ref x);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("DODGE_AND_ROLL")]
public sealed class DodgeAndRoll : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CorePowerSupport.ApplyCardPowers.DodgeAndRoll: the block actually gained becomes Unpowered
    // block next turn (BlockNextTurnPower).
    public override void OnPlay(ref FlashExec x)
    {
        decimal gained = FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 0),
            FlashValueProps.Move, fromCard: true);
        if (x.NeedsChoice || gained <= 0m)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.BlockNextTurnPower>(), (int)gained, FlashLimits.PlayerOwner);
    }
}

[FlashContent("RICOCHET")]
public sealed class Ricochet : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    // RandomTargetAttackCardMirrors.RicochetOnPlay: Repeat randomly aimed hits.
    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), (int)FlashCardEffects.Var(x, 1));
}

[FlashContent("DASH")]
public sealed class Dash : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Block"];

    // Dash.cs: GainBlock before the attack; the inferred recipe keeps that order.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.GeneralAttack(ref x, 0);
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

[FlashContent("POUNCE")]
public sealed class Pounce : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry.Pounce: attack, then FreeSkillPower 1.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FreeSkillPower>(), 1, FlashLimits.PlayerOwner);
    }
}

[FlashContent("PIERCING_WAIL")]
public sealed class PiercingWail : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    // CorePowerSupport.ApplyCardPowers.PiercingWail: ApplyPiercingWail to every hittable enemy.
    public override void OnPlay(ref FlashExec x)
    {
        ushort power = FlashRegistry.PowerId<Powers.PiercingWailPower>();
        int amount = (int)FlashCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyTemporaryStrength(ref x, enemy, power, amount, FlashLimits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("MURDER")]
public sealed class Murder : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.Murder: CalculationBase + ExtraDamage * cards drawn this combat.
    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * x.B.Counter(FlashCounter.CardsDrawn);
        FlashCardEffects.Attack(ref x, damage);
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

[FlashContent("MEMENTO_MORI")]
public sealed class MementoMori : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.MementoMori: CalculationBase + ExtraDamage * cards discarded this turn.
    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * x.B.Counter(FlashCounter.CardsDiscardedThisTurn);
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("PRECISE_CUT")]
public sealed class PreciseCut : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    // CalculatedVarSpecRegistry.PreciseCut: the played card is in the play pile, so the multiplier is
    // -hand count.
    public override void OnPlay(ref FlashExec x)
    {
        decimal damage = FlashCardEffects.Var(x, 0) - FlashCardEffects.Var(x, 1) * x.B.HandCount;
        FlashCardEffects.Attack(ref x, damage);
    }
}

[FlashContent("FINISHER")]
public sealed class Finisher : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    // CalculatedVarSpecRegistry.Finisher: CalculatedHits = CalculationBase + CalculationExtra *
    // attacks played this turn (GetAttacksPlayedThisTurn), the hit count of the Damage attack.
    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)(FlashCardEffects.Var(x, 1)
            + FlashCardEffects.Var(x, 2) * x.B.Counter(FlashCounter.AttacksPlayedThisTurn));
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}

[FlashContent("FLECHETTES")]
public sealed class Flechettes : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    // CalculatedVarSpecRegistry.Flechettes: CalculatedHits counts Skill cards in hand.
    public override void OnPlay(ref FlashExec x)
    {
        int skills = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[index];
            if (FlashKernel.CardType(x.B, x.R, card) == FlashCardType.Skill)
                skills++;
        }
        int hits = (int)(FlashCardEffects.Var(x, 1) + FlashCardEffects.Var(x, 2) * skills);
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 0), hits);
    }
}
