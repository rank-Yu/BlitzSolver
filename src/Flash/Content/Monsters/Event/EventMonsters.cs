using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Monsters.Event;

// Event-encounter monsters (EC batch). Attacks, hit counts and branch weights come from the compiled
// native state machine; ApplyMoveEffects copies the post-attack part of MonsterMoveEffects.Apply for
// the same (monster, move). Starting powers (MysteriousKnight's Strength/Plating, the Battleworn
// Dummy's time limit) are applied by the event/room setup outside combat and therefore arrive in the
// root power table.

/// <summary>
/// FakeMerchantMonster: SWIPE_MOVE is a plain attack, SPEW_COINS_MOVE is 2×8, THROW_RELIC_MOVE applies
/// Frail 1 to the player as a monster debuff, ENRAGE_MOVE applies Strength 2 to itself
/// (<c>MonsterMoveEffects</c> "FakeMerchantMonster").
/// </summary>
[FlashContent("FAKE_MERCHANT_MONSTER")]
public sealed class FakeMerchantMonster : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        switch (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "ENRAGE_MOVE":
                FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
            case "THROW_RELIC_MOVE":
                FlashKernel.ApplyPowerFromMonster(ref x, FlashLimits.PlayerOwner,
                    FlashRegistry.PowerId<Powers.FrailPower>(), 1, owner);
                break;
        }
    }
}

/// <summary>
/// MysteriousKnight: its move state machine is inherited from FlailKnight, so WAR_CHANT applies
/// Strength 3 and FLAIL_MOVE/RAM_MOVE are plain attacks (<c>MonsterMoveEffects</c>
/// "MysteriousKnight"/"FlailKnight"). Its starting Strength 6 and Plating 6 come from the root.
/// </summary>
[FlashContent("MYSTERIOUS_KNIGHT")]
public sealed class MysteriousKnight : FlashMonsterDef
{
    public override void ApplyMoveEffects(ref FlashExec x, byte owner, int moveNode)
    {
        if (FlashKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "WAR_CHANT")
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
    }
}

/// <summary>
/// Architect: 9999 HP and a single no-op NOTHING_MOVE loop with a hidden intent. The event is a
/// dialogue scene; the encounter exists for presentation and has no searchable natural root (the
/// room is ended by the event script before the player acts). The definition keeps a synthesized
/// root simulatable without inventing an effect.
/// </summary>
[FlashContent("ARCHITECT")]
public sealed class Architect : FlashMonsterDef
{
}

/// <summary>
/// The three Battleworn Dummy variants (75/150/300 HP) share the native single-node NOTHING_MOVE loop
/// and the BattlewornDummyTimeLimitPower applied by <c>AfterAddedToRoom</c>. That power arrives in the
/// root power table; <see cref="Powers.BattlewornDummyTimeLimitPower"/> models the decrement and
/// rejects the timeout boundary (the native escape / encounter RanOutOfTime flag has no Flash
/// outcome).
/// </summary>
[FlashContent("BATTLE_FRIEND_V1")]
public sealed class BattleFriendV1 : FlashMonsterDef
{
}

[FlashContent("BATTLE_FRIEND_V2")]
public sealed class BattleFriendV2 : FlashMonsterDef
{
}

[FlashContent("BATTLE_FRIEND_V3")]
public sealed class BattleFriendV3 : FlashMonsterDef
{
}
