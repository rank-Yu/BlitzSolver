using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Powers;

// Theft encounter and Waterfall Giant powers. Each mirrors the existing engine's implementation
// (src/Prediction), not the decompiled original where the two differ.

/// <summary>
/// Native <c>ThieveryPower</c> (GremlinMerc, one instanced stack of 20 applied in
/// <c>AfterAddedToRoom</c>, targeting the player). Its counter is the steal cap per GIMME-family move;
/// <see cref="FlashPower.Aux0"/> is the native <c>DynamicVars.Gold</c> ledger accumulated by
/// <c>SimulatedCombatState.RecordThievery</c>. On death, <c>SurprisePower</c> transfers Aux0 into a
/// <c>HeistPower</c> on the replacement fat gremlin.
/// </summary>
[FlashContent("THIEVERY_POWER")]
public sealed class ThieveryPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    // Native PowerInstanceType.Instanced: every application is its own row.
    public override bool IsInstanced => true;
}

/// <summary>
/// Native <c>HeistPower</c>: instanced gold carried by the fat gremlin that replaced the merc. Its
/// amount is the stolen gold; killing the carrier returns it
/// (<c>SimulatedCombatState.RecoverStolenResources</c>). <c>BeforeDeath</c> only adds a run reward and
/// records the map history, neither of which is prediction state.
/// </summary>
[FlashContent("HEIST_POWER")]
public sealed class HeistPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool IsInstanced => true;
}

/// <summary>
/// Native <c>SwipePower</c>: one instanced row per card stolen by THIEVERY. The complete stolen-card
/// value lives in <see cref="FlashBattle.DetachedCards"/> and <see cref="FlashPower.Aux0"/> is its
/// slot + 1; removing the power clears the slot. Killing the owner recovers the card through
/// <c>RecoverStolenResources</c>; escaping removes the power without returning it.
/// </summary>
[FlashContent("SWIPE_POWER")]
public sealed class SwipePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool IsInstanced => true;

    public override bool UsesDetachedCard => true;
}

/// <summary>
/// Native <c>SurprisePower</c> (GremlinMerc, one stack). While positive it stops the combat from
/// ending, and its deferred death pass creates the fat gremlin (with the merc's Thievery gold
/// transferred into Heist rows) and a sneaky gremlin. The native order is preserved:
/// <c>CreateCreature(fat)</c> (CombatId and Niche HP draw first) → add sneaky → add fat.
/// </summary>
[FlashContent("SURPRISE_POWER")]
public sealed class SurprisePower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Single;

    public override bool PreventsCombatEnding => true;

    public override bool SpawnsPrimaryEnemyOnDeath => true;

    public override void DeathTrigger(ref FlashExec x, FlashListener self, byte dead)
    {
        if (self.Owner != dead)
            return;
        // DeathPowerSupport.Trigger: MonsterSpawnSupport.Create<FatGremlin>(..., "fat") first.
        int fatRulesId = x.R.SpawnRulesId(FlashRegistry.MonsterId<Monsters.Act1.FatGremlin>());
        FlashEnemy fat = default;
        FlashKernel.CreateMonster(ref x, fatRulesId, FlashKernel.SlotIndex(x.R, "fat"), int.MinValue, ref fat);
        // Stolen gold is read from the still-present Thievery rows before they are cleaned up.
        Span<int> gold = stackalloc int[FlashLimits.Powers];
        Span<byte> targets = stackalloc byte[FlashLimits.Powers];
        int transfers = 0;
        ushort thievery = FlashRegistry.PowerId<ThieveryPower>();
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Id != thievery || power.Owner != dead || power.Amount <= 0)
                continue;
            gold[transfers] = power.Aux0;
            targets[transfers] = power.Target;
            transfers++;
        }
        // MonsterSpawnSupport.Spawn<SneakyGremlin>(simulator, combat, dead, "sneaky"): the sneaky
        // gremlin is added before the fat one, so its CombatId follows the fat create and its HP draw
        // excludes only the creatures already in the roster.
        int sneakyRulesId = x.R.SpawnRulesId(FlashRegistry.MonsterId<Monsters.Act1.SneakyGremlin>());
        FlashKernel.SpawnMonster(ref x, dead, sneakyRulesId, FlashKernel.SlotIndex(x.R, "sneaky"), minion: false);
        if (x.NeedsChoice)
            return;
        byte fatOwner = FlashKernel.AddCreatedMonster(ref x, dead, ref fat, minion: false);
        if (x.NeedsChoice)
            return;
        ushort heist = FlashRegistry.PowerId<HeistPower>();
        for (int index = 0; index < transfers; index++)
        {
            if (gold[index] == 0)
                continue;
            FlashKernel.ApplyPowerTargeted(ref x, fatOwner, targets[index], heist, gold[index], dead);
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// Native <c>SteamEruptionPower</c> (Waterfall Giant): a counter that stops the combat from ending
/// and keeps the dead owner in the roster. The death rewrite itself is kernel-side
/// (<c>SimulatedCombatState.TryTriggerSteamEruptionDeath</c> in
/// <c>CorePowerSupport.ApplyEnemyDeathPowers</c>), and the power survives the owner's death cleanup
/// (<c>ShouldPowerBeRemovedAfterOwnerDeath</c>).
/// </summary>
[FlashContent("STEAM_ERUPTION_POWER")]
public sealed class SteamEruptionPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override bool PreventsRemovalAfterDeath => true;

    public override bool PreventsCombatEnding => true;

    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;
}

/// <summary>
/// Native <c>EscapeArtistPower</c> (ThievingHopper, 5 stacks applied in <c>AfterAddedToRoom</c>): a
/// visual countdown. At the end of the enemy side it decrements while above 1
/// (<c>EndTurnPowerSupport.TriggerRegular</c> case).
/// </summary>
[FlashContent("ESCAPE_ARTIST_POWER")]
public sealed class EscapeArtistPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override void EndTurnRegular(ref FlashExec x, FlashListener self, FlashSide side, bool participates)
    {
        if (side != FlashSide.Enemy || !participates)
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount > 1)
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
    }
}

/// <summary>
/// Native <c>FlutterPower</c> (ThievingHopper, 5 stacks from FLUTTER_MOVE): powered attacks against
/// the owner deal <c>DamageDecrease</c>% damage; each unblocked powered hit removes one stack, and
/// the last stack stuns the owner with the next state of its most recently logged move
/// (<c>AfterDamageReceivedMirrors.HandleFlutterPower</c>).
/// </summary>
[FlashContent("FLUTTER_POWER")]
public sealed class FlutterPower : FlashPowerDef
{
    public override FlashPowerType Type => FlashPowerType.Buff;

    public override FlashPowerStackType StackType => FlashPowerStackType.Counter;

    public override IReadOnlyList<string> VarNames => ["DamageDecrease"];

    // The existing engine decrements Flutter through a PowerAmountPredictionState; when the hopper
    // dies before the next synchronization, its death cleanup zeroes the model but the pending amount
    // is restored afterwards, leaving the row on the removed enemy. Flash has no such lag, so keep the
    // row explicitly. It is inert once the owner leaves the roster, and no other monster has Flutter.
    public override bool ShouldPowerBeRemovedAfterOwnerDeath => false;

    public override bool PowerRemainsAfterOwnerLeavesRoster => true;

    public override decimal ModifyDamageMultiplicative(ref FlashExec x, FlashListener self,
        in FlashDamageContext damage, decimal amount)
    {
        if (damage.Target != self.Owner || !damage.Props.IsPoweredAttack())
            return 1m;
        return x.R.PowerVar(Id, 0) / 100m;
    }

    public override void AfterDamageReceived(ref FlashExec x, FlashListener self, in FlashDamageResult result,
        byte dealer)
    {
        if (result.Receiver != self.Owner || result.UnblockedDamage == 0 || !result.Props.IsPoweredAttack())
            return;
        int amount = x.B.Powers[self.Index].Amount;
        if (amount <= 0)
            return;
        if (amount > 1)
        {
            FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, amount - 1);
            return;
        }
        FlashKernel.SetPowerAmount(ref x.B, self.Owner, Id, 0);
        FlashKernel.ForceStunnedAfterLastMove(ref x, self.Owner);
    }
}
