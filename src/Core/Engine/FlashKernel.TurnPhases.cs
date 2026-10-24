namespace CombatSolver.Flash.Kernel;

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

    // CorePowerSupport.CompletePlayerEarlySideTurnEndEffects, per player.
    private static readonly string[] CompletePlayerEarlyOrder = ["REGEN_POWER"];

    private static readonly Lazy<ushort[]> CompletePlayerEarlyIds = new(() => Resolve(CompletePlayerEarlyOrder));

    // CorePowerSupport.ApplyEnemyDeathPowers: checked on each player creature, in source order.
    private static readonly string[] ApplierDeathOrder = ["CONSTRICT_POWER", "HEX_POWER", "SHRINK_POWER"];

    private static readonly Lazy<ushort[]> ApplierDeathIds = new(() => Resolve(ApplierDeathOrder));
    private static readonly Lazy<ushort> MagicBombId = new(() => PowerIdOrMax("MAGIC_BOMB_POWER"));

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
        CompletePlayerEarlySideTurnEnd,
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
            case PowerPhase.CompletePlayerEarlySideTurnEnd: def.CompletePlayerEarlySideTurnEnd(ref x, self); break;
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
        Span<(byte Owner, ushort Id, int Occurrence)> snapshot =
            stackalloc (byte, ushort, int)[count];
        Span<byte> owners = stackalloc byte[FlashLimits.Enemies + 2];
        int ownerCount = ListenerCreatureOrder(x.B, owners);
        int taken = 0;
        // Listener order: creature order (player, Osty, roster enemies), then table order within an owner.
        // Instanced powers share (owner, id), so the snapshot keeps the occurrence to address the exact row.
        for (int creature = 0; creature < ownerCount; creature++)
        {
            for (int index = 0; index < count; index++)
            {
                ref readonly FlashPower power = ref x.B.Powers[index];
                if (power.Owner != owners[creature])
                    continue;
                int occurrence = 0;
                for (int prior = 0; prior < index; prior++)
                {
                    ref readonly FlashPower earlier = ref x.B.Powers[prior];
                    if (earlier.Owner == power.Owner && earlier.Id == power.Id)
                        occurrence++;
                }
                snapshot[taken++] = (power.Owner, power.Id, occurrence);
            }
        }
        if (!x.R.HasIdentityContent)
        {
            for (int entry = 0; entry < taken; entry++)
            {
                (byte owner, ushort id, int occurrence) = snapshot[entry];
                if (ownerFilter == PlayerSideOwners
                        ? !IsPlayerSide(owner)
                        : ownerFilter != FlashLimits.NoOwner && owner != ownerFilter)
                    continue;
                if (onlyId != ushort.MaxValue && id != onlyId || id == exceptA || id == exceptB)
                    continue;
                int index = FindPowerOccurrence(x.B, owner, id, occurrence);
                if (index < 0 || x.B.Powers[index].Amount <= 0)
                    continue;
                InvokePowerPhase(ref x, phase, index, side, Contains(participants, owner), flag);
                if (x.NeedsChoice)
                    return;
            }
            return;
        }
        // A phase can remove its own instanced row (Nightmare before hand draw, Monologue at the side
        // turn end), which shifts the occurrence of later same-(owner,id) rows. Track removals per key;
        // the listener snapshot is by object, so the original occurrence is authoritative.
        Span<byte> keyOwners = stackalloc byte[taken];
        Span<ushort> keyIds = stackalloc ushort[taken];
        Span<int> keyRemoved = stackalloc int[taken];
        int keyCount = 0;
        for (int entry = 0; entry < taken; entry++)
        {
            (byte owner, ushort id, int occurrence) = snapshot[entry];
            if (ownerFilter == PlayerSideOwners ? !IsPlayerSide(owner) : ownerFilter != FlashLimits.NoOwner && owner != ownerFilter)
                continue;
            if (onlyId != ushort.MaxValue && id != onlyId || id == exceptA || id == exceptB)
                continue;
            int key = -1;
            for (int existing = 0; existing < keyCount; existing++)
            {
                if (keyOwners[existing] == owner && keyIds[existing] == id)
                {
                    key = existing;
                    break;
                }
            }
            if (key < 0)
            {
                key = keyCount++;
                keyOwners[key] = owner;
                keyIds[key] = id;
                keyRemoved[key] = 0;
            }
            occurrence -= keyRemoved[key];
            int index = FindPowerOccurrence(x.B, owner, id, occurrence);
            if (index < 0 || x.B.Powers[index].Amount <= 0)
                continue;
            int before = CountPowers(x.B, owner, id);
            InvokePowerPhase(ref x, phase, index, side, Contains(participants, owner), flag);
            if (x.NeedsChoice)
                return;
            int after = CountPowers(x.B, owner, id);
            if (after < before)
                keyRemoved[key] += before - after;
        }
    }

    /// <summary>Number of rows of one (owner, id), including zeroed rows not yet compacted.</summary>
    private static int CountPowers(in FlashBattle battle, byte owner, ushort id)
    {
        int count = 0;
        for (int index = 0; index < battle.PowerCount; index++)
        {
            if (battle.Powers[index].Owner == owner && battle.Powers[index].Id == id)
                count++;
        }
        return count;
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
        PrepareBeforeSideTurnEnd,
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
                case RelicPhase.PrepareBeforeSideTurnEnd: def.PrepareBeforeSideTurnEnd(ref x, self, side); break;
                case RelicPhase.AfterSideTurnEnd: def.AfterSideTurnEnd(ref x, self, side); break;
                case RelicPhase.AfterBlockCleared: def.AfterBlockCleared(ref x, self); break;
                default: throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
            }
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// The applier cleanup of <c>CorePowerSupport.ApplyEnemyDeathPowers</c> for one newly dead enemy:
    /// Constrict, Hex and Shrink on the player creature whose applier it was, then every Magic Bomb it
    /// applied (listener order). Powers are read directly, without the positive-amount filter.
    /// </summary>
    private static void ApplierDeath(ref FlashExec x, byte dead)
    {
        foreach (ushort id in ApplierDeathIds.Value)
        {
            int index = FindPower(x.B, FlashLimits.PlayerOwner, id);
            if (index < 0 || x.B.Powers[index].Applier != dead)
                continue;
            FlashRegistry.Power(id).ApplierDied(ref x, new FlashListener(FlashListenerKind.Power, (byte)index, FlashLimits.PlayerOwner));
            if (x.NeedsChoice)
                return;
        }
        ushort magicBomb = MagicBombId.Value;
        if (magicBomb == ushort.MaxValue)
            return;
        Span<byte> owners = stackalloc byte[FlashLimits.Powers];
        int count = 0;
        Span<byte> creatures = stackalloc byte[FlashLimits.Enemies + 2];
        int creatureCount = ListenerCreatureOrder(x.B, creatures);
        for (int creature = 0; creature < creatureCount; creature++)
        {
            int index = FindPower(x.B, creatures[creature], magicBomb);
            if (index >= 0 && x.B.Powers[index].Applier == dead)
                owners[count++] = creatures[creature];
        }
        for (int entry = 0; entry < count; entry++)
        {
            int index = FindPower(x.B, owners[entry], magicBomb);
            if (index < 0)
                continue;
            FlashRegistry.Power(magicBomb).ApplierDied(ref x, new FlashListener(FlashListenerKind.Power, (byte)index, owners[entry]));
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
    /// <c>PersistentPowerSupport.TriggerRampart</c>: every positive RampartPower in listener order
    /// grants its amount as Unpowered Block to each living Turret Operator.
    /// </summary>
    private static void RampartPass(ref FlashExec x)
    {
        if ((FlashRegistry.AnyHooks & FlashHookMask.Rampart) == 0)
            return;
        // PersistentPowerSupport.TriggerRampart walks EffectivePowers() and only filters by amount,
        // unlike the ordinary listener passes: a Living Shield that left the roster keeps its
        // RampartPower row and still grants the Turret Operator its block. Walk the power table in
        // row order instead of CollectListeners, which skips non-roster owners.
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            FlashPowerDef def = FlashRegistry.Power(x.B.Powers[index].Id);
            if ((def.Hooks & FlashHookMask.Rampart) == 0 || x.B.Powers[index].Amount <= 0)
                continue;
            def.Rampart(ref x, new FlashListener(FlashListenerKind.Power, (byte)index, x.B.Powers[index].Owner));
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.TriggerSideTurnStart</c>: base effects per participant, then
    /// <c>PersistentPowerSupport.TriggerAfterSideTurnStart</c>, <c>TurnStartPowerSupport.TriggerAfterSideTurnStart</c>
    /// and the relic <c>AfterSideTurnStart</c> batch.
    /// </summary>
    internal static void TriggerSideTurnStart(ref FlashExec x, FlashSide side, scoped ReadOnlySpan<byte> participants,
        bool decrementPlating, bool isExtraTurn = false)
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
        // PersistentPowerSupport.TriggerAfterSideTurnStart: Rampart only at the player side turn
        // start of a non-extra turn.
        if (side == FlashSide.Player && !isExtraTurn && Contains(participants, FlashLimits.PlayerOwner))
        {
            RampartPass(ref x);
            if (x.NeedsChoice)
                return;
        }
        foreach (ushort id in GlobalAfterSideTurnStartIds.Value)
        {
            PowerListenerPass(ref x, PowerPhase.GlobalAfterSideTurnStart, side, participants, FlashLimits.NoOwner, id, ushort.MaxValue, ushort.MaxValue);
            if (x.NeedsChoice)
                return;
        }
        if (Contains(participants, FlashLimits.PlayerOwner))
            RelicPass(ref x, RelicPhase.AfterSideTurnStart, side);
        if (x.NeedsChoice)
            return;
        ResolvePowerAmountChanges(ref x);
    }

    /// <summary><c>CorePowerSupport.CompletePlayerEarlySideTurnEndEffects</c>.</summary>
    internal static void CompletePlayerEarlySideTurnEnd(ref FlashExec x)
        => PowerTypeSequence(ref x, PowerPhase.CompletePlayerEarlySideTurnEnd, FlashLimits.PlayerOwner, CompletePlayerEarlyIds.Value,
            FlashSide.Player, participates: true);

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
        ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        TriggerTransientSideTurnEndPowers(ref x, FlashSide.Player, participants);
        // CompleteRelicsAfterSideTurnEnd is not a listener filter: the existing engine always runs its
        // ArtOfWar/Kusarigama resets and JossPaper/LunarPastry/ParryingShield effects, even when the
        // last primary enemy died during this phase. Only the late listener pass uses
        // HookMirrors.IterateCombatHookListeners, which is empty once IsOverOrEnding (DEFECT-011).
        RelicPass(ref x, RelicPhase.AfterSideTurnEnd, FlashSide.Player);
        if (x.NeedsChoice)
            return;
        if (IsOverOrEnding(x.B))
            return;
        AfterSideTurnEndLatePass(ref x, FlashSide.Player, participants);
        if (!x.NeedsChoice)
            NormalizeCardAfflictions(ref x);
    }

    /// <summary><c>CorePowerSupport.TriggerEnemySideTurnEndEffects</c>.</summary>
    internal static void TriggerEnemySideTurnEndEffects(ref FlashExec x, scoped ReadOnlySpan<byte> enemies)
    {
        // EndTurnPowerSupport.TriggerVeryEarly has no supported source (Asleep is rejected at admission).
        TriggerEnemyDoom(ref x, enemies);
        if (x.NeedsChoice)
            return;
        foreach (byte owner in enemies)
        {
            PowerTypeSequence(ref x, PowerPhase.EnemySideTurnEnd, owner, EnemySideTurnEndIds.Value, FlashSide.Enemy, participates: true);
            if (x.NeedsChoice)
                return;
        }
        EndTurnRegularAndBatch(ref x, FlashSide.Enemy, enemies);
        if (x.NeedsChoice)
            return;
        ResolvePowerAmountChanges(ref x);
        if (x.NeedsChoice)
            return;
        TriggerTransientSideTurnEndPowers(ref x, FlashSide.Enemy, enemies);
        TickDurations(ref x.B);
        AfterSideTurnEndLatePass(ref x, FlashSide.Enemy, enemies);
    }

    /// <summary>
    /// <c>HookMirrors.AfterSideTurnEndLate</c>: a fresh combat-listener snapshot after the regular
    /// end-of-side hooks; only listeners whose definition overrides the Ext hook run.
    /// </summary>
    private static void AfterSideTurnEndLatePass(ref FlashExec x, FlashSide side, scoped ReadOnlySpan<byte> participants)
    {
        if ((FlashRegistry.AnyHooksExt & FlashHookMaskExt.AfterSideTurnEndLate) == 0)
            return;
        Span<FlashListener> listeners = stackalloc FlashListener[FlashHooks.MaxListenerCount];
        int count = FlashHooks.CollectListeners(x.B, FlashHookMaskExt.AfterSideTurnEndLate, listeners);
        for (int index = 0; index < count; index++)
        {
            FlashListener self = listeners[index];
            FlashHooks.Definition(x.B, self).AfterSideTurnEndLate(ref x, self, side, Contains(participants, self.Owner));
            if (x.NeedsChoice)
                return;
        }
    }

    /// <summary>
    /// <c>EndTurnPowerSupport.TriggerEnemyDoom</c>: every living participant whose Doom amount reaches
    /// its current HP dies together (so <c>ReattachPower</c>'s fatal count sees the whole batch).
    /// </summary>
    private static void TriggerEnemyDoom(ref FlashExec x, scoped ReadOnlySpan<byte> enemies)
    {
        ushort doom = PowerIdOrMax("DOOM_POWER");
        if (doom == ushort.MaxValue)
            return;
        Span<byte> doomed = stackalloc byte[FlashLimits.Enemies];
        int count = 0;
        foreach (byte owner in enemies)
        {
            ref readonly FlashCreature creature = ref x.B.Creature(owner);
            if (creature.IsAlive && GetAmount(x.B, owner, doom) >= creature.CurrentHp)
                doomed[count++] = owner;
        }
        if (count > 0)
            DoomKill(ref x, doomed[..count]);
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
        // EndTurnPowerSupport.TriggerRegular's ResetPowerLifecycleTurn for every participant.
        foreach (byte owner in participants)
        {
            if (owner == FlashLimits.PlayerOwner)
                x.B.SetCounter(FlashCounter.SkillPlayedThisSideTurn, 0);
        }
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
        if (x.NeedsChoice)
            return;
        // Relic stage: the modelled BeforeHandDraw relics (Pendulum, PollinousCore) act through
        // ModifyHandDraw, which the turn-start path reads separately.
        ReturnCardsToHand(ref x);
    }

    /// <summary>
    /// <c>SimulatedCombatState.PrepareBeforeHandDraw</c>'s returning-card stage: the cards played last
    /// turn with Bolas/ThrummingHatchet (native <c>_returnToHandNextTurn</c>) move to hand in current
    /// pile order. Their membership is carried on <see cref="FlashCard.ReturnToHandNextTurn"/>.
    /// </summary>
    private static void ReturnCardsToHand(ref FlashExec x)
    {
        // The existing engine moves every card in _returnToHandNextTurn (a set keyed by the played
        // object) to the hand bottom. Re-scan for the next flagged value instead of capturing
        // positions: moving an earlier card shifts the later ones, and a captured occurrence no longer
        // addresses the same card when two returning copies are byte-equal.
        while (true)
        {
            int position = -1;
            for (int candidate = 0; candidate < x.B.CardCount; candidate++)
            {
                if (x.B.Cards[candidate].ReturnToHandNextTurn)
                {
                    position = candidate;
                    break;
                }
            }
            if (position < 0)
                return;
            int handStart = FlashPiles.Start(x.B, FlashPile.Hand);
            int handEnd = handStart + x.B.HandCount;
            if (position >= handStart && position < handEnd)
            {
                x.B.Cards[position].ReturnToHandNextTurn = false;
                continue;
            }
            FlashPile pile = FlashPile.Hand;
            int index = position;
            ReadOnlySpan<FlashPile> sources = [FlashPile.Draw, FlashPile.Discard, FlashPile.Exhaust, FlashPile.Play];
            foreach (FlashPile candidate in sources)
            {
                int start = FlashPiles.Start(x.B, candidate);
                int pileCount = FlashPiles.Count(x.B, candidate);
                if (position < start || position >= start + pileCount)
                    continue;
                pile = candidate;
                index = position - start;
                break;
            }
            x.B.Cards[position].ReturnToHandNextTurn = false;
            MoveCard(ref x.B, pile, index, FlashPile.Hand, FlashPilePosition.Bottom);
        }
    }

    /// <summary>Position of the <paramref name="occurrence"/>-th byte-equal card over the whole battle.</summary>
    private static bool TryLocateCard(in FlashBattle battle, in FlashCard card, int occurrence, out int position)
    {
        for (int index = 0; index < battle.CardCount; index++)
        {
            if (!FlashFingerprint.Equal(battle.Cards[index], card))
                continue;
            if (occurrence-- == 0)
            {
                position = index;
                return true;
            }
        }
        position = -1;
        return false;
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
