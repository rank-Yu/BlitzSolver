namespace CombatSolver.Engine.InCombat.Flash.Content.Powers;

// Second-act (Hive) monster powers added by task KM2. Each mirrors the existing engine
// (src/Prediction and src/Engine/InCombat/Mirrors), not the decompiled original where the two
// differ.

/// <summary>
/// Native <c>ImbalancedPower</c> (BowlbugRock's starting power, applied in <c>AfterAddedToRoom</c>):
/// a fully blocked attack from its owner stuns it. The existing engine does not implement the power's
/// own hook; <c>MonsterMoveSemantics.ApplyForecastMove</c> checks the power amount after the attack
/// and forces <c>STUNNED</c> (HEADBUTT_MOVE for BowlbugRock) plus a skipped next move. The setting is
/// pure marker content here, read by <see cref="FlashKernel.ExecuteMonsterMove"/>.
/// </summary>
[FlashContent("IMBALANCED_POWER")]
public sealed class ImbalancedPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;
}

/// <summary>
/// Native <c>ReattachPower</c> (Decimillipede segments, 25 stacks applied in <c>AfterAddedToRoom</c>):
/// its death case is <c>SimulatedCombatState.BeginReattach</c> — if at least one other segment can
/// still act, the owner stays in the roster as <c>Reviving</c> and is forced into <c>DEAD_MOVE</c>;
/// otherwise every segment becomes <c>PermanentlyDead</c>. The power is kept after the owner's death
/// and keeps the corpse in the roster. It also carries the heal amount used by the segment's
/// REATTACH_MOVE.
/// </summary>
[FlashContent("REATTACH_POWER")]
public sealed class ReattachPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    // ICombatPredictionCreatureSemantics.ShouldRemoveAfterDeath keeps an owner with ReattachPower.
    public override bool PreventsRemovalAfterDeath => true;

    // Native ReattachPower.ShouldPowerBeRemovedAfterOwnerDeath: false.
    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    // Native ReattachPower.ShouldOwnerDeathTriggerFatal: AreAllOtherSegmentsDead().
    public override bool ShouldOwnerDeathTriggerFatal(in FlashBattle battle, FlashListener self)
    {
        for (int position = 0; position < battle.RosterCount; position++)
        {
            byte other = FlashKernel.EnemyOwner(battle.Roster[position]);
            if (other == self.Owner || FlashKernel.GetAmount(battle, other, Id) <= 0)
                continue;
            if (!battle.Creature(other).IsDead)
                return false;
        }
        return true;
    }

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        bool allDead = true;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte other = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (other == dead || FlashKernel.GetAmount(x.B, other, Id) <= 0)
                continue;
            if (!x.B.Creature(other).IsDead)
            {
                allDead = false;
                break;
            }
        }
        if (allDead)
        {
            for (int position = 0; position < x.B.RosterCount; position++)
            {
                byte other = FlashKernel.EnemyOwner(x.B.Roster[position]);
                if (other != dead && FlashKernel.GetAmount(x.B, other, Id) > 0)
                    x.B.Enemy(other).DeathPhase = FlashDeathPhase.PermanentlyDead;
            }
            x.B.Enemy(dead).DeathPhase = FlashDeathPhase.PermanentlyDead;
            return;
        }
        x.B.Enemy(dead).DeathPhase = FlashDeathPhase.Reviving;
        FlashKernel.ForceMonsterMove(ref x, dead,
            FlashKernel.MonsterRules(ref x, dead).NodeIndex("DEAD_MOVE"));
    }
}

/// <summary>
/// Native <c>SlumberPower</c> (SlumberingBeetle, three stacks applied in <c>AfterAddedToRoom</c>):
/// unblocked damage removes one stack and, at zero, forces <c>STUNNED</c> with ROLL_OUT_MOVE as the
/// follow-up (<c>AfterDamageReceivedMirrors.HandleSlumberPower</c>). At its side turn end it loses one
/// stack; at zero it removes Plating and wakes up (<c>EndTurnPowerSupport.TriggerRegular</c>).
/// </summary>
[FlashContent("SLUMBER_POWER")]
public sealed class SlumberPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage == 0)
            return;
        int remaining = x.B.Powers[self.Index].Amount - 1;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, remaining);
        if (remaining <= 0)
        {
            FlashKernel.ForceStunnedMove(ref x, self.Owner,
                FlashKernel.MonsterRules(ref x, self.Owner).NodeIndex("ROLL_OUT_MOVE"));
        }
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int remaining = x.B.Powers[self.Index].Amount - 1;
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, remaining);
        if (remaining <= 0)
        {
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, FlashRegistry.PowerId<PlatingPower>(), 0);
        }
    }
}

/// <summary>
/// Native <c>CurlUpPower</c> (LouseProgenitor, CurlBlock stacks applied in <c>AfterAddedToRoom</c>):
/// the first powered card attack that hits the owner records the played card; that card's
/// <c>AfterCardPlayed</c> gains the power's amount as Unpowered block, consumes the power and marks
/// the state consumed (<c>AfterDamageReceivedMirrors.HandleCurlUpPower</c>,
/// <c>AfterCardPlayedMirrors.HandleCurlUpPower</c>). Aux0 = Consumed; Aux1 = a card is recorded.
/// Flash cards have no instance identity, so the recorded "same card" test is the fact that the next
/// <c>AfterCardPlayed</c> after a card-sourced hit is that card's own (nested auto-plays finish their
/// own <c>AfterCardPlayed</c> first), exactly the window the native reference comparison covers.
/// </summary>
[FlashContent("CURL_UP_POWER")]
public sealed class CurlUpPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result, byte dealer)
    {
        if (result.Receiver != self.Owner || !result.Props.IsPoweredAttack() || !result.HasCardSource)
            return;
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux0 != 0 || power.Aux1 != 0)
            return;
        power.Aux1 = 1;
    }

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        ref FlashPower power = ref x.B.Powers[self.Index];
        if (power.Aux1 == 0)
            return;
        power.Aux1 = 0;
        FlashKernel.GainBlock(ref x, self.Owner, power.Amount, FlashValueProps.Unpowered, fromCard: false);
        if (x.NeedsChoice)
            return;
        x.B.Powers[self.Index].Aux0 = 1;
        // The native power removes itself; the existing engine consumes its PowerAmountPredictionState
        // and SynchronizePowerAmountPredictionStates applies the removal at the card tail.
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>SurroundedPower</c> (Rocket applies it to the player in <c>AfterAddedToRoom</c>): the
/// player's attacks deal 1.5x damage while the power faces the back-attack marker the played card
/// targets. The facing is private branch state (<c>SurroundedPredictionState</c>), projected into
/// Aux0 (0 = Right, 1 = Left). It updates on <c>BeforeCardPlayed</c>
/// (<c>BeforeCardPlayedMirrors.HandleSurroundedPower</c>) and when an enemy dies
/// (<c>DeathPowerSupport.Trigger</c> through <c>PowerLifecycleSupport.UpdateSurroundedForTarget</c>),
/// and is read by the multiplicative damage hook (<c>ModifyDamageMirrors.HandleSurroundedPower</c>).
/// </summary>
[FlashContent("SURROUNDED_POWER")]
public sealed class SurroundedPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override void BeforeCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (play.Target == FlashLimits.NoOwner)
            return;
        UpdateFacing(ref x, self, play.Target);
    }

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self, in FlashDamageContext damage, decimal amount)
    {
        if (damage.Dealer == FlashLimits.NoOwner || damage.Target != self.Owner)
            return 1m;
        if (!FlashRegistry.TryPower("BACK_ATTACK_LEFT_POWER", out ushort backLeft)
            || !FlashRegistry.TryPower("BACK_ATTACK_RIGHT_POWER", out ushort backRight))
        {
            return 1m;
        }
        int facing = x.B.Powers[self.Index].Aux0;
        if (facing == 0 && FlashKernel.GetAmount(x.B, damage.Dealer, backLeft) > 0)
        {
            return 1.5m;
        }
        if (facing == 1 && FlashKernel.GetAmount(x.B, damage.Dealer, backRight) > 0)
        {
            return 1.5m;
        }
        return 1m;
    }

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (!FlashKernel.IsEnemyOwner(dead))
            return;
        int remaining = -1;
        bool allLeft = true;
        bool allRight = true;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            if (remaining < 0)
                remaining = enemy;
            bool left = FlashRegistry.TryPower("BACK_ATTACK_LEFT_POWER", out ushort backLeft)
                && FlashKernel.GetAmount(x.B, enemy, backLeft) > 0;
            bool right = FlashRegistry.TryPower("BACK_ATTACK_RIGHT_POWER", out ushort backRight)
                && FlashKernel.GetAmount(x.B, enemy, backRight) > 0;
            allLeft &= left;
            allRight &= right;
        }
        if (remaining < 0 || (!allLeft && !allRight))
            return;
        UpdateFacing(ref x, self, checked((byte)remaining));
    }

    private static void UpdateFacing(ref FlashExec x, FlashListener self, byte target)
    {
        if (FlashRegistry.TryPower("BACK_ATTACK_LEFT_POWER", out ushort backLeft)
            && FlashKernel.GetAmount(x.B, target, backLeft) > 0)
        {
            x.B.Powers[self.Index].Aux0 = 1;
            return;
        }
        if (FlashRegistry.TryPower("BACK_ATTACK_RIGHT_POWER", out ushort backRight)
            && FlashKernel.GetAmount(x.B, target, backRight) > 0)
        {
            x.B.Powers[self.Index].Aux0 = 0;
        }
    }
}

/// <summary>Native <c>BackAttackLeftPower</c>, the marker SurroundedPower checks for (Crusher).</summary>
[FlashContent("BACK_ATTACK_LEFT_POWER")]
public sealed class BackAttackLeftPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;
}

/// <summary>Native <c>BackAttackRightPower</c>, the marker SurroundedPower checks for (Rocket).</summary>
[FlashContent("BACK_ATTACK_RIGHT_POWER")]
public sealed class BackAttackRightPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;
}

/// <summary>
/// Native <c>CrabRagePower</c> (Crusher and Rocket, one stack applied in <c>AfterAddedToRoom</c>):
/// when a teammate on its side dies it gains its Strength variable and its Block variable as Unpowered
/// block, then removes itself (<c>AfterDeathMirrors.HandleCrabRage</c>). The existing handler does not
/// check the removal-prevented flag, so neither does this one.
/// </summary>
[FlashContent("CRAB_RAGE_POWER")]
public sealed class CrabRagePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override IReadOnlyList<string> VarNames => ["StrengthPower", "Block"];

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (creature == self.Owner || !FlashKernel.IsEnemyOwner(creature))
            return;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), (int)x.R.PowerVar(Id, 0), self.Owner);
        FlashKernel.GainBlock(ref x, self.Owner, x.R.PowerVar(Id, 1), FlashValueProps.Unpowered, fromCard: false);
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
    }
}

/// <summary>
/// Native <c>InfestedPower</c> (PhrogParasite, four stacks applied in <c>AfterAddedToRoom</c>): when
/// its owner dies it spawns four Wrigglers into the encounter's wriggler1..4 slots, created with
/// <c>StartStunned = true</c>, and the four are primary enemies. The existing engine settles this in
/// <c>DeathPowerSupport.Trigger</c> and keeps the combat alive through
/// <c>SpawnsPrimaryEnemyOnDeath</c>/<c>HasUnresolvedSpawningDeath</c>.
/// </summary>
[FlashContent("INFESTED_POWER")]
public sealed class InfestedPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool SpawnsPrimaryEnemyOnDeath => true;

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (dead != self.Owner)
            return;
        int rulesId = x.R.SpawnRulesId(FlashRegistry.MonsterId<Monsters.Act2.Wriggler>());
        for (int index = 0; index < 4; index++)
        {
            int slot = FlashKernel.SlotIndex(x.R, $"wriggler{index + 1}");
            FlashKernel.SpawnMonster(ref x, dead, rulesId, slot, minion: false);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>TenderPower</c> (HunterKiller's TENDERIZING_GOOP_MOVE, one stack): every card its owner
/// plays removes one Strength and one Dexterity (unless the combat is ending) and counts toward
/// Aux0; at its side turn end it gains that many Strength and Dexterity and resets the count
/// (<c>AfterCardPlayedMirrors.HandleTenderPower</c>, <c>EndTurnPowerSupport.RestoreTender</c>). The
/// existing engine keeps the count in <c>_tenderCardsPlayed</c>; the bridge projects it.
/// </summary>
[FlashContent("TENDER_POWER")]
public sealed class TenderPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Debuff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        x.B.Powers[self.Index].Aux0++;
        if (FlashKernel.IsEnding(x.B))
            return;
        byte applier = x.B.Powers[self.Index].Applier;
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), -1, applier);
        FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DexterityPower>(), -1, applier);
    }

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (!participates)
            return;
        int count = x.B.Powers[self.Index].Aux0;
        byte applier = x.B.Powers[self.Index].Applier;
        if (count > 0)
        {
            FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<StrengthPower>(), count, applier);
            FlashKernel.ApplyPower(ref x, self.Owner, FlashRegistry.PowerId<DexterityPower>(), count, applier);
        }
        int index = FlashKernel.FindPower(x.B, self.Owner, Id);
        if (index >= 0)
            x.B.Powers[index].Aux0 = 0;
    }
}
