using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

/// <summary>
/// <c>DeathMarch</c>: calculated attack with CalculationBase + ExtraDamage × non-hand draws this turn
/// (<c>CalculatedVarSpecRegistry.DeathMarch</c>).
/// </summary>
[FlashContent("DEATH_MARCH")]
public sealed class DeathMarch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int draws = x.B.Counter(FlashCounter.NonHandDrawsThisTurn);
        FlashCardEffects.Attack(ref x, NecrobinderContent.Calculated(ref x, 0, 1, draws));
    }
}

/// <summary>
/// <c>PullFromBelow</c>: Osty-free attack with CalculatedHits = CalculationBase + CalculationExtra ×
/// Ethereal card plays this combat (<c>CalculatedVarSpecRegistry.PullFromBelow</c>).
/// </summary>
[FlashContent("PULL_FROM_BELOW")]
public sealed class PullFromBelow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        int hits = (int)NecrobinderContent.Calculated(ref x, 1, 2,
            x.B.Counter(FlashCounter.EtherealPlays));
        FlashCardEffects.Attack(ref x, NecrobinderContent.Var(ref x, 0), hits);
    }
}

/// <summary>
/// Native <c>Fetch</c>: an Osty attack; if this exact card has not finished a play this turn, draw one.
/// Existing engine: <c>CardDrawCardMirrors.FetchOnPlay</c> and
/// <c>SimulatedCombatState.WasFetchPlayedThisTurn</c>/<c>_fetchCardsPlayedThisTurn</c>.
/// </summary>
[FlashContent("FETCH")]
public sealed class Fetch : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Cards"];

    public override bool TracksFetchPlay => true;

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        if ((x.Card.Private & FlashKernel.FetchPlayedBit) == 0)
            FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>HighFive</c>: playable only while Osty is alive; Osty hits all enemies, then every hittable enemy
/// gains Vulnerable (<c>CorePowerSupport</c>'s HighFive case).
/// </summary>
[FlashContent("HIGH_FIVE")]
public sealed class HighFive : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "VulnerablePower"];

    public override bool IsPlayable(in FlashBattle battle, FlashRules rules, in FlashCard card)
        => NecrobinderContent.HasOsty(battle);

    public override void OnPlay(ref FlashExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

/// <summary>
/// <c>BoneShards</c>: Osty hits all enemies, the player gains block, then the pet is killed with force
/// (<c>CardEffectSpecRegistry</c>'s BoneShards completion, after the inferred attack/block mirror).
/// </summary>
[FlashContent("BONE_SHARDS")]
public sealed class BoneShards : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        // BoneShardsOnPlay wraps attack + block in Osty.CheckMissingWithAnim: both are skipped when the
        // pet is missing or dead. The sacrifice in CardEffectSpecRegistry uses the same alive gate.
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice || !NecrobinderContent.HasOsty(x.B))
            return;
        Span<byte> osty = stackalloc byte[1];
        osty[0] = FlashLimits.OstyOwner;
        FlashKernel.Kill(ref x, osty, force: true);
    }
}

[FlashContent("RIGHT_HAND_HAND")]
public sealed class RightHandHand : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Energy"];

    public override void OnPlay(ref FlashExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    // RightHandHand.AfterCardPlayedLate: the listener does NOT check that the played card is this copy;
    // after any play that spent at least the Energy variable, each RightHandHand copy sitting in the
    // discard pile goes back to hand (AfterCardPlayedMirrors.HandleRightHandHand).
    public override void AfterCardPlayedLate(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref readonly FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        if (x.CurrentPlayEnergyValue < x.R.Card(card.Id, card.Upgrade).Vars[1])
            return;
        int start = FlashPiles.Start(x.B, FlashPile.Discard);
        int count = FlashPiles.Count(x.B, FlashPile.Discard);
        if (self.Index < start || self.Index >= start + count)
            return;
        FlashKernel.MoveCard(ref x.B, FlashPile.Discard, self.Index - start, FlashPile.Hand,
            FlashPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Rattle</c>: Osty attack whose hit count is the CalculatedHits variable,
/// CalculationBase + CalculationExtra × (1 + Osty's attacks this turn).
/// </summary>
[FlashContent("RATTLE")]
public sealed class Rattle : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal multiplier = 1 + x.B.Counter(FlashCounter.OstyAttacksThisTurn);
        int hits = (int)NecrobinderContent.Calculated(ref x, 1, 2, multiplier);
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0), hits);
    }
}

/// <summary>
/// <c>SicEm</c>: Osty attack, then apply SicEmPower to the target even when the pet was missing
/// (<c>CardEffectSpecRegistry</c>'s SicEm target power).
/// </summary>
[FlashContent("SIC_EM")]
public sealed class SicEm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "SicEmPower"];

    public override void OnPlay(ref FlashExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.SicEmPower>(), 1);
    }
}

/// <summary>
/// <c>Debilitate</c>: attack, then apply DebilitatePower to the target
/// (<c>CardEffectSpecRegistry</c>'s target DebilitatePower).
/// </summary>
[FlashContent("DEBILITATE")]
public sealed class Debilitate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "DebilitatePower"];

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
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.DebilitatePower>(), 1);
    }
}

/// <summary>
/// <c>Veilpiercer</c>: attack, then one VeilpiercerPower on the player
/// (<c>CardEffectSpecRegistry</c>'s owner VeilpiercerPower).
/// </summary>
[FlashContent("VEILPIERCER")]
public sealed class Veilpiercer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.VeilpiercerPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

[FlashContent("BURY")]
public sealed class Bury : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>Severance</c>: attack, then three unupgraded Souls — one to the draw pile at a random position,
/// one to the discard pile, one to hand (<c>CardEffectSpecRegistry</c>'s Severance completion).
/// </summary>
[FlashContent("SEVERANCE")]
public sealed class Severance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
        if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
            || x.NeedsChoice)
            return;
        if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Discard)
            || x.NeedsChoice)
            return;
        FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Hand);
    }
}
