using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

/// <summary>
/// Native <c>PhantomBlades</c>: apply PhantomBladesPower and, on the first stack, give every Shiv in
/// combat the local Retain keyword (<c>PhantomBladesPowerMirrors.AfterApplied</c>, called when the
/// power transitions from zero). Existing engine: <c>CardPowerOnPlaySupport</c>'s PhantomBlades case.
/// </summary>
[BlitzContent("PHANTOM_BLADES")]
public sealed class PhantomBlades : BlitzCardDef
{
    public override bool IsIdentityContent => true;

    public override IReadOnlyList<string> VarNames => ["PhantomBladesPower"];

    public override IReadOnlyList<string> AppliedPowers => ["PHANTOM_BLADES_POWER"];

    public override void OnPlay(ref BlitzExec x)
    {
        if (!BlitzRegistry.TryPower("PHANTOM_BLADES_POWER", out ushort power))
            throw new BlitzUnsupportedException("PhantomBlades 需要 PHANTOM_BLADES_POWER 定义。");
        bool alreadyPresent = BlitzKernel.GetAmount(x.B, BlitzLimits.PlayerOwner, power) > 0;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, power, (int)BlitzCardEffects.Var(x, 0),
            BlitzLimits.PlayerOwner);
        if (x.NeedsChoice || alreadyPresent)
            return;
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref BlitzCard card = ref x.B.Cards[position];
            BlitzCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if ((stats.Tags & BlitzCardTag.Shiv) != 0)
                card.Keywords |= BlitzKeywords.Retain;
        }
    }
}

[BlitzContent("NOXIOUS_FUMES")]
public sealed class NoxiousFumes : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPerTurn"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.NoxiousFumesPower>(), 0);
}

[BlitzContent("ACCELERANT")]
public sealed class Accelerant : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Accelerant"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.AccelerantPower>(), 0);
}

[BlitzContent("INFINITE_BLADES")]
public sealed class InfiniteBlades : ShivGeneratingCardDef
{
    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.InfiniteBladesPower>(), 1, BlitzLimits.PlayerOwner);
}

[BlitzContent("ACCURACY")]
public sealed class Accuracy : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["AccuracyPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.AccuracyPower>(), 0);
}

[BlitzContent("SPEEDSTER")]
public sealed class Speedster : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["SpeedsterPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.SpeedsterPower>(), 0);
}

[BlitzContent("FOOTWORK")]
public sealed class Footwork : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToOwner(ref x, BlitzRegistry.PowerId<Powers.DexterityPower>(), 0);
}
