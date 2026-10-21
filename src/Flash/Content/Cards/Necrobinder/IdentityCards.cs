using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

// Necrobinder cards that need a per-card identity bit or a card the closure reaches.

/// <summary>
/// Native <c>Fetch</c>: an Osty attack; if this exact card has not finished a play this turn, draw one.
/// Existing engine: <c>CardDrawCardMirrors.FetchOnPlay</c> and
/// <c>SimulatedCombatState.WasFetchPlayedThisTurn</c>/<c>_fetchCardsPlayedThisTurn</c>.
/// </summary>
[FlashContent("FETCH")]
public sealed class Fetch : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["OstyDamage", "Cards"];

    public override bool TracksFetchPlay => true;

    public override void OnPlay(ref FlashExec x)
    {
        if (!NecrobinderContent.HasOsty(x.B))
            return;
        NecrobinderContent.OstyAttack(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        if ((x.Card.Private & FlashKernel.FetchPlayedBit) == 0)
            FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 1), fromHandDraw: false);
    }
}

/// <summary>
/// Native <c>Delay</c>: gain <c>Block</c>, then apply EnergyNextTurnPower. Existing engine:
/// the inferred block recipe plus <c>CardEffectSpecRegistry</c>'s Delay energy application.
/// </summary>
[FlashContent("DELAY")]
public sealed class Delay : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override IReadOnlyList<string> AppliedPowers => ["ENERGY_NEXT_TURN_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryPower("ENERGY_NEXT_TURN_POWER", out ushort power))
            throw new FlashUnsupportedException("Delay 需要 ENERGY_NEXT_TURN_POWER 定义。");
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)NecrobinderContent.Var(ref x, 1),
            FlashLimits.PlayerOwner);
    }
}
