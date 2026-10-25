using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Act1;

/// <summary>
/// FossilStalker: "TACKLE_MOVE" applies Frail 1 to the player (<c>MonsterMoveEffects</c>
/// "FossilStalker"). Its SuckPower is a starting power imported from the root; LATCH/LASH are pure
/// attacks and the random branch weights are compiled from the native model.
/// </summary>
[BlitzContent("FOSSIL_STALKER")]
public sealed class FossilStalker : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "TACKLE_MOVE")
            BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                BlitzRegistry.PowerId<Powers.FrailPower>(), 1, owner);
    }
}

/// <summary>
/// SoulFysh: "BECKON_MOVE" adds one Beckon to the draw pile at a random position and one to the
/// discard pile; "GAZE_MOVE" applies Vulnerable <c>ScreamMoveAmount</c> and adds
/// <c>GazeMoveAmount</c> Beckons to the discard pile; "SCREAM_MOVE" applies Vulnerable
/// <c>ScreamMoveAmount</c> to the player; "FADE_MOVE" applies Intangible 2 to itself
/// (<c>MonsterMoveEffects</c> "SoulFysh"; the Beckon counts are literals there: 1 random draw +
/// 1 discard, and the model's GazeMoveAmount/ScreamMoveAmount static values).
/// </summary>
[BlitzContent("SOUL_FYSH")]
public sealed class SoulFysh : BlitzMonsterDef
{
    private const int BeckonDrawAmount = 1;
    private const int BeckonDiscardAmount = 1;
    private const int FadeIntangible = 2;
    private const int ScreamMoveAmount = 0;
    private const int GazeMoveAmount = 1;

    public override IReadOnlyList<string> StaticIntNames => ["ScreamMoveAmount", "GazeMoveAmount"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("BECKON", 0)];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "BECKON_MOVE":
                for (int index = 0; index < BeckonDrawAmount; index++)
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Beckon>()),
                        BlitzPile.Draw, BlitzPilePosition.Random, creator: BlitzLimits.NoOwner);
                for (int index = 0; index < BeckonDiscardAmount; index++)
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Beckon>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
                break;
            case "GAZE_MOVE":
                for (int index = 0; index < rules.StaticInts[GazeMoveAmount]; index++)
                    BlitzKernel.AddGeneratedCard(ref x,
                        BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Status.Beckon>()), BlitzPile.Discard, creator: BlitzLimits.NoOwner);
                break;
            case "SCREAM_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.VulnerablePower>(), rules.StaticInts[ScreamMoveAmount], owner);
                break;
            case "FADE_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.IntangiblePower>(),
                    FadeIntangible, owner);
                break;
        }
    }
}

/// <summary>
/// SkulkingColony: "INERTIA_MOVE" applies Strength <c>InertiaStrengthGain</c> to itself
/// (<c>MonsterMoveEffects</c> "SkulkingColony"). Its HardenedShellPower is a starting power imported
/// from the root; ZOOM/PIERCING_STABS are pure attacks.
/// </summary>
[BlitzContent("SKULKING_COLONY")]
public sealed class SkulkingColony : BlitzMonsterDef
{
    private const int InertiaStrengthGain = 0;

    public override IReadOnlyList<string> StaticIntNames => ["InertiaStrengthGain"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        if (rules.Nodes[moveNode].NativeId == "INERTIA_MOVE")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
                rules.StaticInts[InertiaStrengthGain], owner);
    }
}

/// <summary>
/// TerrorEel: "TERROR_MOVE" applies Vulnerable 99 and "THRASH_MOVE" applies Vigor 6 to itself
/// (<c>MonsterMoveEffects.Apply</c> "TerrorEel"; both amounts are literals there). CRASH is a compiled
/// attack. Its ShriekPower starts in the root; STUN_MOVE is a native state the existing engine never
/// reaches (Shriek uses <c>CreatureCmd.Stun</c> with TERROR_MOVE as the follow-up).
/// </summary>
[BlitzContent("TERROR_EEL")]
public sealed class TerrorEel : BlitzMonsterDef
{
    private const int TerrorVulnerable = 99;
    private const int ThrashVigor = 6;

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "TERROR_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.VulnerablePower>(), TerrorVulnerable, owner);
                break;
            case "THRASH_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.VigorPower>(),
                    ThrashVigor, owner);
                break;
        }
    }
}

/// <summary>
/// Native <c>WaterfallGiant</c> (WATERFALL_GIANT_BOSS). The static values and move effects mirror
/// <c>MonsterMoveEffects</c> lines 870-905; the pressure-gun bonus and Steam Eruption damage live in
/// the AI registers projected from <c>SimulatedCombatState._pressureGunBonus</c> /
/// <c>_steamEruptionDamage</c>. The death rewrite and forced phases are kernel-side
/// (<see cref="BlitzKernel.TryTriggerSteamEruptionDeath"/> / <see cref="BlitzKernel.TryConsumeSteamForcedMove"/>).
/// </summary>
[BlitzContent("WATERFALL_GIANT")]
public sealed class WaterfallGiant : BlitzMonsterDef
{
    public override bool HasInfiniteHpDisplay => true;

    private const int PressurizeAmount = 0;
    private const int SiphonHeal = 1;
    private const int PressureGunIncrease = 2;

    public override IReadOnlyList<string> StaticIntNames =>
        ["PressurizeAmount", "SiphonHeal", "PressureGunIncrease"];

    public override IReadOnlyList<string> AiRegisterNames => ["PressureGunBonus", "SteamEruptionDamage"];

    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        BlitzMonsterRules rules = BlitzKernel.MonsterRules(ref x, owner);
        ushort steam = BlitzRegistry.PowerId<Powers.SteamEruptionPower>();
        switch (rules.Nodes[moveNode].NativeId)
        {
            case "PRESSURIZE_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, steam, rules.StaticInts[PressurizeAmount], owner);
                break;
            case "STOMP_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.WeakPower>(), 1, owner);
                BlitzKernel.ApplyPower(ref x, owner, steam, 3, owner);
                break;
            case "RAM_MOVE":
            case "PRESSURE_UP_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, steam, 3, owner);
                break;
            case "SIPHON_MOVE":
                // MonsterMoveEffects: Heal(SiphonHeal * combat.Players.Count); single-player is 1.
                BlitzKernel.Heal(ref x, owner, rules.StaticInts[SiphonHeal]);
                BlitzKernel.ApplyPower(ref x, owner, steam, 3, owner);
                break;
            case "PRESSURE_GUN_MOVE":
                x.B.Enemy(owner).Ai[BlitzKernel.PressureGunBonusRegister] = checked(
                    x.B.Enemy(owner).Ai[BlitzKernel.PressureGunBonusRegister] + rules.StaticInts[PressureGunIncrease]);
                BlitzKernel.ApplyPower(ref x, owner, steam, 3, owner);
                break;
            case "ABOUT_TO_BLOW_MOVE":
                // MonsterMoveEffects: combat.PrepareSteamEruption(move.Owner). The forced phase path
                // calls the same helper before executing anything.
                BlitzKernel.PrepareSteamEruption(ref x, owner);
                break;
            case "EXPLODE_MOVE":
            {
                Span<byte> self = stackalloc byte[1];
                self[0] = owner;
                BlitzKernel.Kill(ref x, self, force: true);
                break;
            }
        }
    }

    public override int ModifyAttackDamage(in BlitzBattle battle, BlitzRules rules, byte owner, int moveNode,
        int attackIndex, int damage)
    {
        string nativeId = rules.MonsterRule(battle.Enemies[owner - BlitzLimits.FirstEnemyOwner].Rules)
            .Nodes[moveNode].NativeId;
        if (nativeId == "PRESSURE_GUN_MOVE")
            return damage + battle.Enemies[owner - BlitzLimits.FirstEnemyOwner].Ai[BlitzKernel.PressureGunBonusRegister];
        if (nativeId == "EXPLODE_MOVE")
        {
            int recorded = battle.Enemies[owner - BlitzLimits.FirstEnemyOwner].Ai[BlitzKernel.SteamEruptionDamageRegister];
            return recorded != 0 ? recorded : damage;
        }
        return damage;
    }
}
