using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

/// <summary>
/// Native <c>PhantomBlades</c>: apply PhantomBladesPower and, on the first stack, give every Shiv in
/// combat the local Retain keyword (<c>PhantomBladesPowerMirrors.AfterApplied</c>, called when the
/// power transitions from zero). Existing engine: <c>CardPowerOnPlaySupport</c>'s PhantomBlades case.
/// </summary>
[FlashContent("PHANTOM_BLADES")]
public sealed class PhantomBlades : FlashCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["PhantomBladesPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PHANTOM_BLADES_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        if (!FlashRegistry.TryPower("PHANTOM_BLADES_POWER", out ushort power))
            throw new FlashUnsupportedException("PhantomBlades 需要 PHANTOM_BLADES_POWER 定义。");
        bool alreadyPresent = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, power) > 0;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice || alreadyPresent)
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if ((stats.Tags & FlashCardTag.Shiv) != 0)
                card.Keywords |= FlashKeywords.Retain;
        }
    }
}

[FlashContent("NOXIOUS_FUMES")]
public sealed class NoxiousFumes : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPerTurn"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.NoxiousFumesPower>(), 0);
}

[FlashContent("ACCELERANT")]
public sealed class Accelerant : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Accelerant"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.AccelerantPower>(), 0);
}

[FlashContent("INFINITE_BLADES")]
public sealed class InfiniteBlades : ShivGeneratingCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.InfiniteBladesPower>(), 1, FlashLimits.PlayerOwner);
}

[FlashContent("ACCURACY")]
public sealed class Accuracy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["AccuracyPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.AccuracyPower>(), 0);
}

[FlashContent("SPEEDSTER")]
public sealed class Speedster : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SpeedsterPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.SpeedsterPower>(), 0);
}

[FlashContent("FOOTWORK")]
public sealed class Footwork : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.DexterityPower>(), 0);
}
