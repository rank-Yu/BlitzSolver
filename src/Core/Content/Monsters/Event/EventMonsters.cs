using BlitzSolver.Engine;

namespace BlitzSolver.Content.Monsters.Event;

/// <summary>
/// FakeMerchantMonster: SWIPE_MOVE is a plain attack, SPEW_COINS_MOVE is 2×8, THROW_RELIC_MOVE applies
/// Frail 1 to the player as a monster debuff, ENRAGE_MOVE applies Strength 2 to itself
/// (<c>MonsterMoveEffects</c> "FakeMerchantMonster").
/// </summary>
[BlitzContent("FAKE_MERCHANT_MONSTER")]
public sealed class FakeMerchantMonster : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        switch (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId)
        {
            case "ENRAGE_MOVE":
                BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 2, owner);
                break;
            case "THROW_RELIC_MOVE":
                BlitzKernel.ApplyPowerFromMonster(ref x, BlitzLimits.PlayerOwner,
                    BlitzRegistry.PowerId<Powers.FrailPower>(), 1, owner);
                break;
        }
    }
}

/// <summary>
/// MysteriousKnight: its move state machine is inherited from FlailKnight, so WAR_CHANT applies
/// Strength 3 and FLAIL_MOVE/RAM_MOVE are plain attacks (<c>MonsterMoveEffects</c>
/// "MysteriousKnight"/"FlailKnight"). Its starting Strength 6 and Plating 6 come from the root.
/// </summary>
[BlitzContent("MYSTERIOUS_KNIGHT")]
public sealed class MysteriousKnight : BlitzMonsterDef
{
    public override void ApplyMoveEffects(ref BlitzExec x, byte owner, int moveNode)
    {
        if (BlitzKernel.MonsterRules(ref x, owner).Nodes[moveNode].NativeId == "WAR_CHANT")
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.StrengthPower>(), 3, owner);
    }
}

/// <summary>
/// Architect: 9999 HP and a single no-op NOTHING_MOVE loop with a hidden intent. The event is a
/// dialogue scene; the encounter exists for presentation and has no searchable natural root (the
/// room is ended by the event script before the player acts). The definition keeps a synthesized
/// root simulatable without inventing an effect.
/// </summary>
[BlitzContent("ARCHITECT")]
public sealed class Architect : BlitzMonsterDef
{
}

/// <summary>
/// The three Battleworn Dummy variants (75/150/300 HP) share the native single-node NOTHING_MOVE loop
/// and the BattlewornDummyTimeLimitPower applied by <c>AfterAddedToRoom</c>. That power arrives in the
/// root power table; <see cref="Powers.BattlewornDummyTimeLimitPower"/> models the decrement and
/// rejects the timeout boundary (the native escape / encounter RanOutOfTime flag has no Blitz
/// outcome).
/// </summary>
[BlitzContent("BATTLE_FRIEND_V1")]
public sealed class BattleFriendV1 : BlitzMonsterDef
{
}

[BlitzContent("BATTLE_FRIEND_V2")]
public sealed class BattleFriendV2 : BlitzMonsterDef
{
}

[BlitzContent("BATTLE_FRIEND_V3")]
public sealed class BattleFriendV3 : BlitzMonsterDef
{
}
