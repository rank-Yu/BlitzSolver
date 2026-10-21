using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Monsters.Act1;

// Act 1 monsters whose behavior depends on card afflictions, plus Living Fog's Gas Bomb. Attacks,
// hit counts and state transitions are compiled from the native move state machine by the bridge;
// each ApplyMoveEffects case mirrors the existing engine's MonsterMoveEffects.Apply for the same
// (monster, move), and ApplyBeforeAttack equivalents live in BeforeMove.

/// <summary>
/// Ceremonial Beast (CEREMONIAL_BEAST_BOSS): STAMP applies PlowPower (<c>PlowAmount</c>), PLOW and
/// CRUSH add Strength after their attack, and BEAST_CRY afflicts the player with RingingPower.
/// STUN_MOVE is the native must-perform-once stun state; the Plow removal forces the separate
/// synthetic STUNNED follow-up to BEAST_CRY_MOVE (see <see cref="PlowPower"/>).
/// </summary>
[FlashContent("CEREMONIAL_BEAST")]
public sealed class CeremonialBeast : FlashMonsterDef
{
    private const int PlowStrength = 0;
    private const int CrushStrength = 1;
    private const int PlowAmount = 2;

    public override IReadOnlyList<string> StaticIntNames => ["PlowStrength", "CrushStrength", "PlowAmount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "STAMP_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<PlowPower>(),
                    rules.StaticInts[PlowAmount], owner);
                break;
            case "PLOW_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<StrengthPower>(),
                    rules.StaticInts[PlowStrength], owner);
                break;
            case "CRUSH_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<StrengthPower>(),
                    rules.StaticInts[CrushStrength], owner);
                break;
            case "BEAST_CRY_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<RingingPower>(), 1, owner);
                break;
        }
    }
}

/// <summary>
/// Vine Shambler (VINE_SHAMBLER_NORMAL): GRASPING_VINES_MOVE applies TangledPower 1 to the player
/// after its attack; SWIPE_MOVE and CHOMP_MOVE are attacks only.
/// </summary>
[FlashContent("VINE_SHAMBLER")]
public sealed class VineShambler : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "GRASPING_VINES_MOVE")
        {
            FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                FlashRegistry.PowerId<TangledPower>(), 1, owner);
        }
    }
}

/// <summary>
/// Infested Prism (INFESTED_PRISMS_ELITE): RADIATE gains <c>RadiateBlock</c>, PULSATE gains
/// <c>PulsateBlock</c> and applies VitalSparkPower <c>VitalSparkAmount</c> to itself. Its entrance
/// VitalSpark is already in the root; JAB_MOVE and WHIRLWIND_MOVE are attacks only.
/// </summary>
[FlashContent("INFESTED_PRISM")]
public sealed class InfestedPrism : FlashMonsterDef
{
    private const int RadiateBlock = 0;
    private const int PulsateBlock = 1;
    private const int VitalSparkAmount = 2;

    public override IReadOnlyList<string> StaticIntNames => ["RadiateBlock", "PulsateBlock", "VitalSparkAmount"];

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "RADIATE_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[RadiateBlock], FlashValueProps.Move, fromCard: false);
                break;
            case "PULSATE_MOVE":
                FlashKernel.GainBlock(ref x, owner, rules.StaticInts[PulsateBlock], FlashValueProps.Move, fromCard: false);
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<VitalSparkPower>(),
                    rules.StaticInts[VitalSparkAmount], owner);
                break;
        }
    }
}

/// <summary>
/// Living Fog (LIVING_FOG_NORMAL): BLOAT summons <c>BloatAmount</c> Gas Bombs into the next free
/// encounter slots before its attack (<c>MonsterMoveEffects.ApplyBeforeAttack</c>); ADVANCED_GAS_MOVE
/// applies SmoggyPower 1 to the player after its attack.
/// </summary>
[FlashContent("LIVING_FOG")]
public sealed class LivingFog : FlashMonsterDef
{
    private const int BloatAmount = 0;

    public override IReadOnlyList<string> StaticIntNames => ["BloatAmount"];

    public override IReadOnlyList<string> SpawnedMonsterIds => ["GAS_BOMB"];

    public override void BeforeMove(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "BLOAT_MOVE")
            return;
        int count = rules.StaticInts[BloatAmount];
        if (!x.R.TrySpawnRulesId(FlashRegistry.MonsterId<GasBomb>(), out int spawnRules))
            throw new FlashUnsupportedException("Living Fog 的生成闭包缺少 Gas Bomb。");
        for (int index = 0; index < count; index++)
        {
            string? slot = FlashKernel.NextFreeSlot(x.B, x.R);
            if (string.IsNullOrEmpty(slot))
                break;
            FlashKernel.SpawnMonster(ref x, owner, spawnRules, FlashKernel.SlotIndex(x.R, slot), minion: false);
            if (x.NeedsChoice)
                return;
        }
    }

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "ADVANCED_GAS_MOVE")
        {
            FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                FlashRegistry.PowerId<SmoggyPower>(), 1, owner);
        }
    }
}

/// <summary>
/// Gas Bomb (summoned by Living Fog): gains MinionPower 1 on entrance and EXPLODE_MOVE attacks then
/// kills itself. Mirrors MonsterSpawnSupport.ApplyNativeEntrancePowers and the GasBomb EXPLODE case.
/// </summary>
[FlashContent("GAS_BOMB")]
public sealed class GasBomb : FlashMonsterDef
{
    public override void ApplyEntrancePowers(ref FlashExec x, byte owner)
        => FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<MinionPower>(), 1, owner);

    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        FlashMonsterRules rules = FlashKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId != "EXPLODE_MOVE")
            return;
        Span<byte> self = stackalloc byte[1];
        self[0] = owner;
        FlashKernel.Kill(ref x, self, force: true);
    }
}
