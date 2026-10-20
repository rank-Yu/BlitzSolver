namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Necrobinder;

// Doom, Soul and energy cards. Existing engine paths: CardOnPlaySupport (BlightStrike, Scourge,
// Wisp, BorrowedTime), CardEffectSpecRegistry (NegativePulse, Putrefy, DeathsDoor, Scourge),
// CorePowerSupport (Deathbringer, Putrefy, BlightStrike), CardPowerOnPlaySupport (Countdown,
// Neurosurge, Shroud, Calcify, Lethality, Haunt, Oblivion, Friendship), CalculatedVarSpecRegistry
// (SoulStorm, DeathMarch, NoEscape, TimesUp), and CalculatedHits for PullFromBelow.

/// <summary>
/// <c>Soul</c>: the 0-cost Exhaust token that draws its <c>Cards</c> variable
/// (<c>CardOnPlayInferrer</c>'s owner-draw recipe; <c>CardDrawCardMirrors</c>-style behavior).
/// </summary>
[FlashContent("SOUL")]
public sealed class Soul : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 0), fromHandDraw: false);
}

/// <summary>
/// <c>NegativePulse</c>: player block, then Doom to every hittable enemy
/// (<c>CardEffectSpecRegistry</c>'s AllEnemies Doom; the block is the general recipe).
/// </summary>
[FlashContent("NEGATIVE_PULSE")]
public sealed class NegativePulse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "DoomPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.DoomPower>(), 1);
    }
}

/// <summary>
/// <c>Deathbringer</c>: for each hittable enemy in roster order, apply Doom then Weak
/// (<c>CardOnPlaySupport.Apply</c>'s Deathbringer case).
/// </summary>
[FlashContent("DEATHBRINGER")]
public sealed class Deathbringer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        int doom = (int)NecrobinderContent.Var(ref x, 0);
        int weak = (int)NecrobinderContent.Var(ref x, 1);
        ushort doomId = FlashRegistry.PowerId<Powers.DoomPower>();
        ushort weakId = FlashRegistry.PowerId<Powers.WeakPower>();
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, doomId, doom, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
            FlashKernel.ApplyPower(ref x, enemy, weakId, weak, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("PUTREFY")]
public sealed class Putrefy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Power"];

    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)NecrobinderContent.Var(ref x, 0);
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 0);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.VulnerablePower>(), amount,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>NoEscape</c>: apply Doom equal to CalculationBase + CalculationExtra × floor(target Doom /
/// DoomThreshold) (<c>CalculatedVarSpecRegistry.NoEscape</c>).
/// </summary>
[FlashContent("NO_ESCAPE")]
public sealed class NoEscape : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomThreshold", "CalculationBase", "CalculationExtra"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        int doom = FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.DoomPower>());
        decimal multiplier = Math.Floor(doom / NecrobinderContent.Var(ref x, 0));
        int amount = (int)NecrobinderContent.Calculated(ref x, 1, 2, multiplier);
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DoomPower>(), amount,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>TimesUp</c>: calculated attack with CalculationBase 0 + ExtraDamage 1 × the target's Doom
/// (<c>CalculatedVarSpecRegistry.TimesUp</c>).
/// </summary>
[FlashContent("TIMES_UP")]
public sealed class TimesUp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        int doom = FlashKernel.GetAmount(x.B, x.Target, FlashRegistry.PowerId<Powers.DoomPower>());
        FlashCardEffects.Attack(ref x, NecrobinderContent.Calculated(ref x, 0, 1, doom));
    }
}

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
/// <c>SoulStorm</c>: calculated attack with CalculationBase + ExtraDamage × Soul cards in the exhaust
/// pile (<c>CalculatedVarSpecRegistry.SoulStorm</c>).
/// </summary>
[FlashContent("SOUL_STORM")]
public sealed class SoulStorm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int souls = CountExhaustSouls(x.B, x.R);
        FlashCardEffects.Attack(ref x, NecrobinderContent.Calculated(ref x, 0, 1, souls));
    }

    private static int CountExhaustSouls(in FlashBattle battle, FlashRules rules)
    {
        int start = FlashPiles.Start(battle, FlashPile.Exhaust);
        int count = FlashPiles.Count(battle, FlashPile.Exhaust);
        int souls = 0;
        for (int offset = 0; offset < count; offset++)
        {
            ref readonly FlashCard card = ref battle.Cards[start + offset];
            if (rules.Card(card.Id, card.Upgrade).IsSoul)
                souls++;
        }
        return souls;
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
/// <c>BlightStrike</c>: attack, then apply Doom equal to the attack's blocked + unblocked damage against
/// the target (<c>CorePowerSupport</c>'s BlightStrike case, which sums the play's damage entries;
/// the single-target attack makes the last damage call that total).
/// </summary>
[FlashContent("BLIGHT_STRIKE")]
public sealed class BlightStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        // CorePowerSupport.ApplyCardPowers runs ApplyEnemyDeathPowers after the mirror attack
        // and before the card-specific compensation, so the debuff sees the post-death target.
        FlashKernel.ApplyEnemyDeathPowers(ref x);
        if (x.NeedsChoice)
            return;
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        int damage = x.LastDamageTotal;
        if (damage > 0)
            FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DoomPower>(), damage,
                FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Scourge</c>: apply Doom to the target, then draw the card's Cards variable
/// (<c>CardEffectSpecRegistry</c>'s target Doom; the draw is the native <c>CardPileCmd.Draw</c>).
/// </summary>
[FlashContent("SCOURGE")]
public sealed class Scourge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.DoomPower>(), 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// <c>DeathsDoor</c>: gain block once, plus the Repeat variable's count when Doom was applied by the
/// player this turn (<c>CardEffectSpecRegistry</c>'s DeathsDoor case reads
/// <c>WasDoomAppliedThisTurn</c>).
/// </summary>
[FlashContent("DEATHS_DOOR")]
public sealed class DeathsDoor : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        int gains = 1;
        if (x.B.Counter(FlashCounter.DoomAppliedThisTurn) > 0)
            gains += (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < gains; index++)
        {
            FlashCardEffects.GeneralBlock(ref x, 0);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>EndOfDays</c>: apply Doom to every hittable enemy, then kill the doomed ones together
/// (<c>DoomPower.GetDoomedCreatures</c> + <c>DoomPower.DoomKill</c>; <c>CardOnPlaySupport.Batch042</c>).
/// </summary>
[FlashContent("END_OF_DAYS")]
public sealed class EndOfDays : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnPlay(ref FlashExec x)
    {
        int amount = (int)NecrobinderContent.Var(ref x, 0);
        ushort doomId = FlashRegistry.PowerId<Powers.DoomPower>();
        Span<byte> doomed = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyPower(ref x, enemy, doomId, amount, FlashLimits.PlayerOwner);
            if (x.NeedsChoice)
                return;
            // ApplyEndOfDays drains the pending amount changes before its Doom threshold check,
            // so ShroudPower's block gain lands while the combat is still running.
            FlashKernel.ResolvePowerAmountChanges(ref x);
            if (x.NeedsChoice)
                return;
            FlashKernel.ApplyEnemyDeathPowers(ref x);
            if (x.NeedsChoice)
                return;
        }
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            int doom = FlashKernel.GetAmount(x.B, enemy, doomId);
            if (doom > 0 && x.B.Creature(enemy).CurrentHp <= doom)
                doomed[count++] = enemy;
        }
        if (count > 0)
            FlashKernel.DoomKill(ref x, doomed[..count]);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyEnemyDeathPowers(ref x);
    }
}
