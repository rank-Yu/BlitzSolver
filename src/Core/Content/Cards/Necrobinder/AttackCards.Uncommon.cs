using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Necrobinder;

/// <summary>
/// <c>DeathMarch</c>: calculated attack with CalculationBase + ExtraDamage × non-hand draws this turn
/// (<c>CalculatedVarSpecRegistry.DeathMarch</c>).
/// </summary>
[BlitzContent("DEATH_MARCH")]
public sealed class DeathMarch : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref BlitzExec x)
    {
        int draws = x.B.Counter(BlitzCounter.NonHandDrawsThisTurn);
        BlitzCardEffects.Attack(ref x, NecrobinderContent.Calculated(ref x, 0, 1, draws));
    }
}

/// <summary>
/// <c>PullFromBelow</c>: Osty-free attack with CalculatedHits = CalculationBase + CalculationExtra ×
/// Ethereal card plays this combat (<c>CalculatedVarSpecRegistry.PullFromBelow</c>).
/// </summary>
[BlitzContent("PULL_FROM_BELOW")]
public sealed class PullFromBelow : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        int hits = (int)NecrobinderContent.Calculated(ref x, 1, 2,
            x.B.Counter(BlitzCounter.EtherealPlays));
        BlitzCardEffects.Attack(ref x, NecrobinderContent.Var(ref x, 0), hits);
    }
}

/// <summary>
/// Native <c>Fetch</c>: an Osty attack; if this exact card has not finished a play this turn, draw one.
/// Existing engine: <c>CardDrawCardMirrors.FetchOnPlay</c> and
/// <c>SimulatedCombatState.WasFetchPlayedThisTurn</c>/<c>_fetchCardsPlayedThisTurn</c>.
/// </summary>
[BlitzContent("FETCH")]
public sealed class Fetch : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Cards"];

    public override bool TracksFetchPlay => true;

    public override void OnPlay(ref BlitzExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        if ((x.Card.Private & BlitzKernel.FetchPlayedBit) == 0)
            BlitzKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>HighFive</c>: playable only while Osty is alive; Osty hits all enemies, then every hittable enemy
/// gains Vulnerable (<c>CorePowerSupport</c>'s HighFive case).
/// </summary>
[BlitzContent("HIGH_FIVE")]
public sealed class HighFive : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "VulnerablePower"];

    public override bool IsPlayable(in BlitzBattle battle, BlitzRules rules, in BlitzCard card)
        => NecrobinderContent.HasOsty(battle);

    public override void OnPlay(ref BlitzExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToAllEnemies(ref x, BlitzRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

/// <summary>
/// <c>BoneShards</c>: Osty hits all enemies, the player gains block, then the pet is killed with force
/// (<c>CardEffectSpecRegistry</c>'s BoneShards completion, after the inferred attack/block mirror).
/// </summary>
[BlitzContent("BONE_SHARDS")]
public sealed class BoneShards : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Block"];

    public override void OnPlay(ref BlitzExec x)
    {
        // BoneShardsOnPlay wraps attack + block in Osty.CheckMissingWithAnim: both are skipped when the
        // pet is missing or dead. The sacrifice in CardEffectSpecRegistry uses the same alive gate.
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice || !NecrobinderContent.HasOsty(x.B))
            return;
        Span<byte> osty = stackalloc byte[1];
        osty[0] = BlitzLimits.OstyOwner;
        BlitzKernel.Kill(ref x, osty, force: true);
    }
}

[BlitzContent("RIGHT_HAND_HAND")]
public sealed class RightHandHand : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Energy"];

    public override void OnPlay(ref BlitzExec x) => NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));

    // RightHandHand.AfterCardPlayedLate: the listener does NOT check that the played card is this copy;
    // after any play that spent at least the Energy variable, each RightHandHand copy sitting in the
    // discard pile goes back to hand (AfterCardPlayedMirrors.HandleRightHandHand).
    public override void AfterCardPlayedLate(ref BlitzExec x, BlitzListener self, in BlitzCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref readonly BlitzCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        if (x.CurrentPlayEnergyValue < x.R.Card(card.Id, card.Upgrade).Vars[1])
            return;
        int start = BlitzPiles.Start(x.B, BlitzPile.Discard);
        int count = BlitzPiles.Count(x.B, BlitzPile.Discard);
        if (self.Index < start || self.Index >= start + count)
            return;
        BlitzKernel.MoveCard(ref x.B, BlitzPile.Discard, self.Index - start, BlitzPile.Hand,
            BlitzPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Rattle</c>: Osty attack whose hit count is the CalculatedHits variable,
/// CalculationBase + CalculationExtra × (1 + Osty's attacks this turn).
/// </summary>
[BlitzContent("RATTLE")]
public sealed class Rattle : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        decimal multiplier = 1 + x.B.Counter(BlitzCounter.OstyAttacksThisTurn);
        int hits = (int)NecrobinderContent.Calculated(ref x, 1, 2, multiplier);
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0), hits);
    }
}

/// <summary>
/// <c>SicEm</c>: Osty attack, then apply SicEmPower to the target even when the pet was missing
/// (<c>CardEffectSpecRegistry</c>'s SicEm target power).
/// </summary>
[BlitzContent("SIC_EM")]
public sealed class SicEm : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["OstyDamage", "SicEmPower"];

    public override void OnPlay(ref BlitzExec x)
    {
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.SicEmPower>(), 1);
    }
}

/// <summary>
/// <c>Debilitate</c>: attack, then apply DebilitatePower to the target
/// (<c>CardEffectSpecRegistry</c>'s target DebilitatePower).
/// </summary>
[BlitzContent("DEBILITATE")]
public sealed class Debilitate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "DebilitatePower"];

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
        BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.DebilitatePower>(), 1);
    }
}

/// <summary>
/// <c>Veilpiercer</c>: attack, then one VeilpiercerPower on the player
/// (<c>CardEffectSpecRegistry</c>'s owner VeilpiercerPower).
/// </summary>
[BlitzContent("VEILPIERCER")]
public sealed class Veilpiercer : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.VeilpiercerPower>(), 1,
            BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("BURY")]
public sealed class Bury : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>Severance</c>: attack, then three unupgraded Souls — one to the draw pile at a random position,
/// one to the discard pile, one to hand (<c>CardEffectSpecRegistry</c>'s Severance completion).
/// </summary>
[BlitzContent("SEVERANCE")]
public sealed class Severance : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
        if (!BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Draw, BlitzPilePosition.Random)
            || x.NeedsChoice)
            return;
        if (!BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Discard)
            || x.NeedsChoice)
            return;
        BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Hand);
    }
}
