using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Necrobinder;

// Necrobinder pool cards that the CallOfTheVoid character-pool closure needs. Their existing engine
// paths are BeforeCardPlayedMirrors (DanseMacabre), AfterDamageGivenMirrors (ReaperForm) and
// CardChoiceSupport/CardChoiceResolution (Seance).

[FlashContent("DANSE_MACABRE")]
public sealed class DanseMacabre : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DanseMacabrePower"];

    public override IReadOnlyList<string> AppliedPowers => ["DANSE_MACABRE_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<DanseMacabrePower>(), (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("REAPER_FORM")]
public sealed class ReaperForm : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["REAPER_FORM_POWER"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<ReaperFormPower>(), 1, FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Seance</c>: choose Cards cards from the draw pile and transform each into <c>Soul</c>
/// (<c>CardChoiceSupport</c>'s Seance spec, <c>CardChoiceResolution</c>'s TransformSelectedCards).
/// The replacement is the level-0 canonical Soul; the upgrade only reduces Seance's cost.
/// </summary>
[FlashContent("SEANCE")]
public sealed class Seance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // TransformSelectedCards creates the level-0 canonical Soul; without this declaration the rule
    // table has no Soul stats and the first transform reports an unsupported kernel boundary.
    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.B.DrawCount == 0)
            return;
        if (!FlashRegistry.TryCard("SOUL", out ushort soul))
            throw new FlashUnsupportedException("Soul 没有 Flash 定义。");
        int count = Math.Min((int)FlashCardEffects.Var(x, 0), x.B.DrawCount);
        if (count <= 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Transform, FlashPile.Draw, options, count, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.TransformCards(ref x, FlashPile.Draw, selected[..chosen],
            FlashKernel.CreateCard(x.R, soul), upgrade: 0);
    }
}
