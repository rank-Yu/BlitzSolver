namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Defect;

// Defect pool cards that were still missing from the flash content set. Existing engine paths:
// CardOnPlayInferrer's generic attack/block recipes, OrbCardMirrors (MeteorStrike, Quadcast) and
// CardEffectSpecRegistry (BoostAway, MomentumStrike). The multiplayer-only pool cards (EnergySurge,
// Ignition, ImitationLearning, Hibernate, OneForAll) are filtered out of single-player generation by
// FilterForCombatAndPlayerCount and are intentionally not defined, matching the original.

[FlashContent("LEAP")]
public sealed class Leap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("BOOST_AWAY")]
public sealed class BoostAway : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DAZED", 0)];

    // CardEffectSpecRegistry: gain block, then AddFixed<Dazed>(PileType.Discard, 1).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("DAZED", out ushort dazed))
            throw new FlashUnsupportedException("Dazed 没有 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, dazed), FlashPile.Discard);
    }
}

[FlashContent("METEOR_STRIKE")]
public sealed class MeteorStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // OrbCardMirrors.MeteorStrikeOnPlay: AttackSingle, then channel three Plasma orbs.
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("PLASMA_ORB"), 3);
    }
}

[FlashContent("MOMENTUM_STRIKE")]
public sealed class MomentumStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    // CardEffectSpecRegistry: AttackSingle, then playedCard.MutablePreview.EnergyCost.SetThisCombat(0).
    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.AddEnergyCostModifier(ref FlashKernel.CurrentPlayCard(ref x), 0,
            FlashCostType.Absolute, FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

[FlashContent("QUADCAST")]
public sealed class Quadcast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // OrbCardMirrors.QuadcastOnPlay: OrbEvokeNext with Repeat hits; only the last one dequeues.
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.EvokeNext(ref x, (int)FlashCardEffects.Var(x, 0));
    }
}
