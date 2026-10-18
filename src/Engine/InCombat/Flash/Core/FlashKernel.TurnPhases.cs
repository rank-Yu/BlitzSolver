namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Turn-phase skeleton mirroring the existing engine's phase functions. Two shapes occur there:
/// listener-order passes over powers or relics with a per-type switch (each switch case becomes the
/// definition's phase method), and fixed type sequences (kept here as ordered native-id lists, copied
/// from the existing code). A type without a Flash definition is skipped; admission already rejects
/// roots that hold it.
/// </summary>
public static partial class FlashKernel
{
    // PersistentPowerSupport.TriggerOwnerAfterSideTurnStart, in source order.
    private static readonly string[] OwnerAfterSideTurnStartOrder =
    [
        "BIASED_COGNITION_POWER", "COOLANT_POWER", "DEMON_FORM_POWER", "FERAL_POWER", "FURNACE_POWER",
        "NEUROSURGE_POWER", "NOXIOUS_FUMES_POWER", "PREP_TIME_POWER", "REFLECT_POWER", "SHADOW_STEP_POWER",
        "WRAITH_FORM_POWER", "CLARITY_POWER",
    ];

    // SimulatedCombatState.TriggerBaseSideTurnStart, in source order.
    private static readonly string[] BaseSideTurnStartOrder =
        ["BLUR_POWER", "DRAW_CARDS_NEXT_TURN_POWER", "PLATING_POWER", "SLOW_POWER"];

    // TurnStartPowerSupport.TriggerAfterSideTurnStart, in source order (Sandpit only on the enemy side).
    private static readonly string[] GlobalAfterSideTurnStartOrder = ["COUNTDOWN_POWER", "SANDPIT_POWER"];

    // CorePowerSupport.TriggerTransientSideTurnEndPowers: removals then decrements, per participant.
    private static readonly string[] TransientRemovals =
    [
        "BORROWED_TIME_POWER", "BURST_POWER", "DUPLICATION_POWER", "NO_ENERGY_GAIN_POWER", "ONE_TWO_PUNCH_POWER",
        "RAGE_POWER", "REBOUND_POWER", "SHADOWMELD_POWER",
    ];

    private static readonly string[] TransientDecrements = ["CONQUEROR_POWER", "RETAIN_HAND_POWER"];

    // CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects, per player, before TriggerRegular.
    private static readonly string[] PlayerSideTurnEndOrder = ["CONSTRICT_POWER", "TANGLED_POWER", "RINGING_POWER", "DOUBLE_DAMAGE_POWER"];

    // CorePowerSupport.TriggerEnemySideTurnEndEffects, per enemy, before TriggerRegular.
    private static readonly string[] EnemySideTurnEndOrder = ["REGEN_POWER", "PLATING_POWER", "DOUBLE_DAMAGE_POWER"];

    private static readonly Lazy<ushort[]> OwnerAfterSideTurnStartIds = new(() => Resolve(OwnerAfterSideTurnStartOrder));
    private static readonly Lazy<ushort[]> BaseSideTurnStartIds = new(() => Resolve(BaseSideTurnStartOrder));
    private static readonly Lazy<ushort[]> GlobalAfterSideTurnStartIds = new(() => Resolve(GlobalAfterSideTurnStartOrder));
    private static readonly Lazy<ushort[]> TransientRemovalIds = new(() => Resolve(TransientRemovals));
    private static readonly Lazy<ushort[]> TransientDecrementIds = new(() => Resolve(TransientDecrements));
    private static readonly Lazy<ushort[]> PlayerSideTurnEndIds = new(() => Resolve(PlayerSideTurnEndOrder));
    private static readonly Lazy<ushort[]> EnemySideTurnEndIds = new(() => Resolve(EnemySideTurnEndOrder));

    /// <summary>Registered internal ids of the listed native ids, keeping order; unregistered ones are dropped.</summary>
    private static ushort[] Resolve(string[] nativeIds)
    {
        List<ushort> ids = new(nativeIds.Length);
        foreach (string nativeId in nativeIds)
        {
            if (FlashRegistry.TryPower(nativeId, out ushort id))
                ids.Add(id);
        }
        return [.. ids];
    }

    /// <summary>Owner filter of a listener pass: the player creature and Osty (<c>power.Owner.Player == player</c>).</summary>
    private const byte PlayerSideOwners = FlashLimits.NoOwner - 1;

    private static ushort PowerIdOrMax(string nativeId) => FlashRegistry.TryPower(nativeId, out ushort id) ? id : ushort.MaxValue;

    private enum PowerPhase : byte
    {
        BeforeSideTurnStart,
        BaseSideTurnStart,
        OwnerAfterSideTurnStart,
        GlobalAfterSideTurnStart,
        AfterBlockCleared,
        EndTurnRegular,
        EndTurnBatch048,
        PlayerSideTurnEnd,
        EnemySideTurnEnd,
        AfterEnergyReset,
        BeforeHandDraw,
        AfterPlayerTurnStart,
    }

    private static void InvokePowerPhase(ref FlashExec x, PowerPhase phase, int index, FlashSide side, bool participates, bool flag)
    {
        ref FlashPower power = ref x.B.Powers[index];
        FlashPowerDef def = FlashRegistry.Power(power.Id);
        FlashListener self = new(FlashListenerKind.Power, (byte)index, power.Owner);
        switch (phase)
        {
            case PowerPhase.BeforeSideTurnStart: def.BeforeSideTurnStart(ref x, self, participates); break;
            case PowerPhase.BaseSideTurnStart: def.BaseSideTurnStart(ref x, self, flag); break;
            case PowerPhase.OwnerAfterSideTurnStart: def.OwnerAfterSideTurnStart(ref x, self); break;
            case PowerPhase.GlobalAfterSideTurnStart: def.GlobalAfterSideTurnStart(ref x, self, side, participates); break;
            case PowerPhase.AfterBlockCleared: def.AfterBlockCleared(ref x, self); break;
            case PowerPhase.EndTurnRegular: def.EndTurnRegular(ref x, self, side, participates); break;
            case PowerPhase.EndTurnBatch048: def.EndTurnBatch048(ref x, self, side, participates); break;
            case PowerPhase.PlayerSideTurnEnd: def.PlayerSideTurnEnd(ref x, self); break;
            case PowerPhase.EnemySideTurnEnd: def.EnemySideTurnEnd(ref x, self); break;
            case PowerPhase.AfterEnergyReset: def.AfterEnergyReset(ref x, self); break;
            case PowerPhase.BeforeHandDraw: def.BeforeHandDraw(ref x, self); break;
            case PowerPhase.AfterPlayerTurnStart: def.AfterPlayerTurnStart(ref x, self); break;
            default: throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    /// <summary>
    /// Listener-order pass over a snapshot of the power table (the existing engine iterates the
    /// published EffectivePowers array). Each entry is re-resolved and must still have a positive amount.
    /// </summary>
    private static void PowerListenerPass(ref FlashExec x, PowerPhase phase, FlashSide side,
        scoped ReadOnlySpan<byte> participants, byte ownerFilter, ushort onlyId, ushort exceptA, ushort exceptB, bool flag = false)
    {
        int count = x.B.PowerCount;
        if (count == 0)
            return;
        Span<(byte Owner, ushort Id)> snapshot = stackalloc (byte, ushort)[count];
        Span<byte> owners = stackalloc byte[FlashLimits.Enemies + 2];
        int ownerCount = ListenerCreatureOrder(x.B, owners);
        int taken = 0;
        // Listener order: creature order (player, Osty, roster enemies), then table order within an owner.
        for (int creature = 0; creature < ownerCount; creature++)
        {
            for (int index = 0; index < count; index++)
            {
                ref readonly FlashPower power = ref x.B.Powers[index];
                if (power.Owner == owners[creature])
                    snapshot[taken++] = (power.Owner, power.Id);
            }
        }
        for (int entry = 0; entry < taken; entry++)
        {
            (byte owner, ushort id) = snapshot[entry];
            if (ownerFilter == PlayerSideOwners ? !IsPlayerSide(owner) : ownerFilter != FlashLimits.NoOwner && owner != ownerFilter)
                continue;
            if (onlyId != ushort.MaxValue && id != onlyId || id == exceptA || id == exceptB)
                continue;
            int index = FindPower(x.B, owner, id);
            if (index < 0 || x.B.Powers[index].Amount <= 0)
                continue;
            InvokePowerPhase(ref x, phase, index, side, Contains(participants, owner), flag);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Fixed type sequence for one owner: each listed type the owner holds with a positive amount.</summary>
    private static void PowerTypeSequence(ref FlashExec x, PowerPhase phase, byte owner, scoped ReadOnlySpan<ushort> order,
        FlashSide side, bool participates, bool flag = false)
    {
        foreach (ushort id in order)
        {
            int index = FindPower(x.B, owner, id);
            if (index < 0 || x.B.Powers[index].Amount <= 0)
                continue;
            InvokePowerPhase(ref x, phase, index, side, participates, flag);
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>Player, Osty, then roster enemies: the creature order of the combat listener list.</summary>
    private static int ListenerCreatureOrder(in FlashBattle battle, Span<byte> owners)
    {
        int count = 0;
        owners[count++] = FlashLimits.PlayerOwner;
        if ((battle.Flags & FlashBattleFlags.OstyPresent) != 0)
            owners[count++] = FlashLimits.OstyOwner;
        for (int position = 0; position < battle.RosterCount; position++)
            owners[count++] = EnemyOwner(battle.Roster[position]);
        return count;
    }

    private static bool Contains(ReadOnlySpan<byte> owners, byte owner)
    {
        foreach (byte candidate in owners)
        {
            if (candidate == owner)
                return true;
        }
        return false;
    }

    private enum RelicPhase : byte
    {
        BeforeSideTurnStart,
        AfterEnergyReset,
        AfterEnergyResetLate,
        AfterPlayerTurnStart,
        AfterSideTurnStart,
        BeforeSideTurnEnd,
        AfterSideTurnEnd,
        AfterBlockCleared,
    }

    /// <summary>Relic pass in relic order over unmelted player relics (the player participates in the side).</summary>
    private static void RelicPass(ref FlashExec x, RelicPhase phase, FlashSide side)
    {
        for (int index = 0; index < x.B.RelicCount; index++)
        {
            ref FlashRelic relic = ref x.B.Relics[index];
            if ((relic.Flags & FlashRelicFlags.Melted) != 0)
                continue;
            FlashRelicDef def = FlashRegistry.Relic(relic.Id);
            FlashListener self = new(FlashListenerKind.Relic, (byte)index, FlashLimits.PlayerOwner);
            switch (phase)
            {
                case RelicPhase.BeforeSideTurnStart: def.BeforeSideTurnStart(ref x, self, side); break;
                case RelicPhase.AfterEnergyReset: def.AfterEnergyReset(ref x, self); break;
                case RelicPhase.AfterEnergyResetLate: def.AfterEnergyResetLate(ref x, self); break;
                case RelicPhase.AfterPlayerTurnStart: def.AfterPlayerTurnStart(ref x, self); break;
                case RelicPhase.AfterSideTurnStart: def.AfterSideTurnStart(ref x, self, side); break;
                case RelicPhase.BeforeSideTurnEnd: def.BeforeSideTurnEnd(ref x, self, side); break;
                case RelicPhase.AfterSideTurnEnd: def.AfterSideTurnEnd(ref x, self, side); break;
                case RelicPhase.AfterBlockCleared: def.AfterBlockCleared(ref x, self); break;
                default: throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
            }
            if (x.NeedsChoice)
                return;
        }
    }

    // ---- Phase functions, named after the existing ones. ----

    /// <summary>
    /// <c>HookMirrors.BeforeSideTurnStart</c> on the vanilla path: relic batch
    /// (<c>PrepareRelicsBeforeSideTurnStart</c>) then <c>TurnStartPowerSupport.TriggerBeforeSideTurnStart</c>
    /// (Plating pass, Aggression pass, then every other power in listener order).
    /// </summary>
    internal static void BeforeSideTurnStart(ref FlashExec x, FlashSide side, scoped ReadOnlySpan<byte> participants)
    {
        if (Contains(participants, FlashLimits.PlayerOwner))
        {
            RelicPass(ref x, RelicPhase.BeforeSideTurnStart, side);
            if (x.NeedsChoice)
                return;
        }
        ushort plating = PowerIdOrMax("PLATING_POWER");
        ushort aggression = PowerIdOrMax("AGGRESSION_POWER");
        if (plating != ushort.MaxValue)
            PowerListenerPass(ref x, PowerPhase.BeforeSideTurnStart, side, participants, FlashLimits.NoOwner, plating, ushort.MaxValue, ushort.MaxValue);
        if (x.NeedsChoice)
            return;
        if (aggression != ushort.MaxValue)
            PowerListenerPass(ref x, PowerPhase.BeforeSideTurnStart, side, participants, FlashLimits.NoOwner, aggression, ushort.MaxValue, ushort.MaxValue);
        if (x.NeedsChoice)
            return;
        PowerListenerPass(ref x, PowerPhase.BeforeSideTurnStart, side, participants, FlashLimits.NoOwner, ushort.MaxValue, plating, aggression);
    }

    /// <summary>Block clear at side start: <c>ShouldClearBlock</c> preventers, then <c>CorePowerSupport.TriggerAfterBlockCleared</c>.</summary>
    internal static void ClearBlockAtSideStart(ref FlashExec x, byte owner)
    {
        if (x.B.Creature(owner).Block > 0)
        {
            if (ShouldClearBlock(ref x, owner, out FlashListener preventer))
            {
                DamageBlock(ref x.B.Creature(owner), x.B.Creature(owner).Block, FlashValueProps.Move);
            }
            else
            {
                // PersistentRelicSupport.TriggerAfterPreventingBlockClear.
                int block = x.B.Creature(owner).Block;
                int retained = FlashHooks.Definition(x.B, preventer).BlockAfterPreventingClear(ref x, preventer, owner, block);
                if (retained < block)
                    DamageBlock(ref x.B.Creature(owner), block - retained, FlashValueProps.Unpowered);
            }
        }
        // TriggerAfterBlockCleared: BlockNextTurn first, then a listener-order pass, then relics.
        ushort blockNextTurn = PowerIdOrMax("BLOCK_NEXT_TURN_POWER");
        if (blockNextTurn != ushort.MaxValue)
        {
            PowerListenerPass(ref x, PowerPhase.AfterBlockCleared, FlashSide.None, default, owner, blockNextTurn, ushort.MaxValue, ushort.MaxValue);
            if (x.NeedsChoice)
                return;
        }
        PowerListenerPass(ref x, PowerPhase.AfterBlockCleared, FlashSide.None, default, owner, ushort.MaxValue, blockNextTurn, ushort.MaxValue);
        if (x.NeedsChoice || owner != FlashLimits.PlayerOwner)
            return;
        RelicPass(ref x, RelicPhase.AfterBlockCleared, FlashSide.None);
    }

    /// <summary>
    /// Native <c>Hook.ShouldClearBlock</c>: the first combat listener that declines is the preventer.
    /// Combat hook listeners are not enumerated while combat is over or ending.
    /// </summary>
    private static bool ShouldClearBlock(ref FlashExec x, byte owner, out FlashListener preventer)
    {
        preventer = default;
        if ((FlashRegistry.AnyHooks & FlashHookMask.ShouldClearBlock) == 0 || IsOverOrEnding(x.B))
            return true;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMask.ShouldClearBlock, listeners);
        for (int index = 0; index < count; index++)
        {
            if (!FlashHooks.Definition(x.B, listeners[index]).ShouldClearBlock(ref x, listeners[index], owner))
            {
                preventer = listeners[index];
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// <c>SimulatedCombatState.TriggerSideTurnStart</c>: base effects per participant, then
    /// <c>PersistentPowerSupport.TriggerAfterSideTurnStart</c>, <c>TurnStartPowerSupport.TriggerAfterSideTurnStart</c>
    /// and the relic <c>AfterSideTurnStart</c> batch.
    /// </summary>
    internal static void TriggerSideTurnStart(ref FlashExec x, FlashSide side, scoped ReadOnlySpan<byte> participants, bool decrementPlating)
    {
        foreach (byte owner in participants)
        {
            PowerTypeSequence(ref x, PowerPhase.BaseSideTurnStart, owner, BaseSideTurnStartIds.Value, side, participates: true, decrementPlating);
            if (x.NeedsChoice)
                return;
        }
        foreach (byte owner in participants)
        {
            PowerTypeSequence(ref x, PowerPhase.OwnerAfterSideTurnStart, owner, OwnerAfterSideTurnStartIds.Value, side, participates: true);
            if (x.NeedsChoice)
                return;
        }
        // Rampart (player side, not an extra turn) is not supported yet.
        foreach (ushort id in GlobalAfterSideTurnStartIds.Value)
        {
            PowerListenerPass(ref x, PowerPhase.GlobalAfterSideTurnStart, side, participants, FlashLimits.NoOwner, id, ushort.MaxValue, ushort.MaxValue);
            if (x.NeedsChoice)
                return;
        }
        if (Contains(participants, FlashLimits.PlayerOwner))
            RelicPass(ref x, RelicPhase.AfterSideTurnStart, side);
    }

    /// <summary><c>CorePowerSupport.TriggerPlayerRegularSideTurnEndEffects</c> plus the relic and late passes of phase two.</summary>
    internal static void PlayerTurnEndPhaseTwo(ref FlashExec x)
    {
        Span<byte> participants = stackalloc byte[1];
        participants[0] = FlashLimits.PlayerOwner;
        PowerTypeSequence(ref x, PowerPhase.PlayerSideTurnEnd, FlashLimits.PlayerOwner, PlayerSideTurnEndIds.Value, FlashSide.Player, participates: true);
        if (x.NeedsChoice)
            return;
        EndTurnRegularAndBatch(ref x, FlashSide.Player, participants);
        if (x.NeedsChoice)
            return;
        // ResolvePowerAmountChanges: no supported amount-change listeners yet.
        TriggerTransientSideTurnEndPowers(ref x, FlashSide.Player, participants);
        RelicPass(ref x, RelicPhase.AfterSideTurnEnd, FlashSide.Player);
        // AfterSideTurnEndLate: no supported listeners yet.
    }

    /// <summary><c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c>.</summary>
    internal static void TriggerEnemySideTurnEndEffects(ref FlashExec x, scoped ReadOnlySpan<byte> enemies)
    {
        // TriggerVeryEarly (Asleep) and Doom: not supported yet.
        foreach (byte owner in enemies)
        {
            PowerTypeSequence(ref x, PowerPhase.EnemySideTurnEnd, owner, EnemySideTurnEndIds.Value, FlashSide.Enemy, participates: true);
            if (x.NeedsChoice)
                return;
        }
        EndTurnRegularAndBatch(ref x, FlashSide.Enemy, enemies);
        if (x.NeedsChoice)
            return;
        TriggerTransientSideTurnEndPowers(ref x, FlashSide.Enemy, enemies);
        TickDurations(ref x.B);
        // AfterSideTurnEndLate: no supported listeners yet.
    }

    /// <summary>
    /// <c>EndTurnPowerSupport.TriggerRegular</c>: its listener-order pass, <c>ResetPowerLifecycleTurn</c>
    /// (no supported per-turn power state yet), then the <c>TriggerBatch048</c> pass.
    /// </summary>
    private static void EndTurnRegularAndBatch(ref FlashExec x, FlashSide side, scoped ReadOnlySpan<byte> participants)
    {
        PowerListenerPass(ref x, PowerPhase.EndTurnRegular, side, participants, FlashLimits.NoOwner, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
        if (x.NeedsChoice)
            return;
        PowerListenerPass(ref x, PowerPhase.EndTurnBatch048, side, participants, FlashLimits.NoOwner, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
    }

    /// <summary><c>CorePowerSupport.TriggerTransientSideTurnEndPowers</c>.</summary>
    private static void TriggerTransientSideTurnEndPowers(ref FlashExec x, FlashSide side, scoped ReadOnlySpan<byte> participants)
    {
        Span<byte> creatures = stackalloc byte[FlashLimits.Enemies + 2];
        int count = ListenerCreatureOrder(x.B, creatures);
        ushort flameBarrier = PowerIdOrMax("FLAME_BARRIER_POWER");
        for (int index = 0; index < count; index++)
        {
            byte creature = creatures[index];
            FlashSide creatureSide = IsPlayerSide(creature) ? FlashSide.Player : FlashSide.Enemy;
            if (flameBarrier != ushort.MaxValue && creatureSide != side && GetAmount(x.B, creature, flameBarrier) > 0)
                SetPowerAmount(ref x.B, creature, flameBarrier, 0);
            if (!Contains(participants, creature))
                continue;
            foreach (ushort id in TransientRemovalIds.Value)
            {
                if (GetAmount(x.B, creature, id) != 0)
                    SetPowerAmount(ref x.B, creature, id, 0);
            }
            foreach (ushort id in TransientDecrementIds.Value)
            {
                int amount = GetAmount(x.B, creature, id);
                if (amount > 0)
                    SetPowerAmount(ref x.B, creature, id, amount - 1);
            }
        }
    }

    /// <summary><c>PersistentPowerSupport.TriggerAfterEnergyReset</c>: the player's positive powers in listener order.</summary>
    internal static void PowersAfterEnergyReset(ref FlashExec x)
        => PowerListenerPass(ref x, PowerPhase.AfterEnergyReset, FlashSide.Player, default, FlashLimits.PlayerOwner, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

    /// <summary><c>TurnStartPowerSupport.TriggerBeforeHandDraw</c> then the relic stage of <c>PrepareBeforeHandDraw</c>.</summary>
    internal static void BeforeHandDraw(ref FlashExec x)
    {
        PowerListenerPass(ref x, PowerPhase.BeforeHandDraw, FlashSide.Player, default, PlayerSideOwners, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
        // Relic and returning-card stages: not supported yet.
    }

    /// <summary><c>HookMirrors.AfterPlayerTurnStart</c> on the vanilla path: powers, then relics.</summary>
    internal static void AfterPlayerTurnStart(ref FlashExec x)
    {
        PowerListenerPass(ref x, PowerPhase.AfterPlayerTurnStart, FlashSide.Player, default, FlashLimits.PlayerOwner, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
        if (x.NeedsChoice)
            return;
        RelicPass(ref x, RelicPhase.AfterPlayerTurnStart, FlashSide.Player);
    }

    /// <summary><c>PersistentPowerSupport.GetModifiedMaxEnergy</c>: every combat listener's modifier.</summary>
    internal static int ModifiedMaxEnergy(ref FlashExec x)
    {
        decimal result = x.R.BaseMaxEnergy;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMask.ModifyMaxEnergy, listeners);
        for (int index = 0; index < count; index++)
            result = FlashHooks.Definition(x.B, listeners[index]).ModifyMaxEnergy(ref x, listeners[index], result);
        return Math.Max(0, (int)result);
    }

    /// <summary><c>PersistentPowerSupport.GetModifiedHandDraw</c>: normal pass then late pass over the combat listeners.</summary>
    internal static int ModifiedHandDraw(ref FlashExec x, int baseDraw)
    {
        decimal result = baseDraw;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMask.ModifyHandDraw, listeners);
        for (int index = 0; index < count; index++)
            result = FlashHooks.Definition(x.B, listeners[index]).ModifyHandDraw(ref x, listeners[index], result);
        count = FlashHooks.CollectListeners(x.B, FlashHookMask.ModifyHandDrawLate, listeners);
        for (int index = 0; index < count; index++)
            result = FlashHooks.Definition(x.B, listeners[index]).ModifyHandDrawLate(ref x, listeners[index], result);
        return Math.Max(0, (int)result);
    }
}
