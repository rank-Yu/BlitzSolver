using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[BlitzContent("PREPARED")]
public sealed class Prepared : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref BlitzExec x)
    {
        // GetSpec: Prepared → Discard, Hand, Cards, every hand card. The bounds are clamped to the
        // option count by ChooseCardsInRange, as CardChoiceSupport.BuildChoices does.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        int count = (int)BlitzCardEffects.Var(x, 0);
        if (!BlitzKernel.ChooseCards(ref x, BlitzChoiceEffect.Discard, BlitzPile.Hand, options, count, selected,
                out int chosen))
        {
            return;
        }
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], 0);
    }
}

[BlitzContent("DEADLY_POISON")]
public sealed class DeadlyPoison : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.PoisonPower>(), 0);
}

[BlitzContent("SNAKEBITE")]
public sealed class Snakebite : BlitzCardDef
{
    // Snakebite.cs: a Retain Skill that applies Poison; it has no damage variable.
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref BlitzExec x)
        => BlitzCardEffects.ApplyToTarget(ref x, BlitzRegistry.PowerId<Powers.PoisonPower>(), 0);
}

[BlitzContent("DODGE_AND_ROLL")]
public sealed class DodgeAndRoll : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CorePowerSupport.ApplyCardPowers.DodgeAndRoll: the block actually gained becomes Unpowered
    // block next turn (BlockNextTurnPower).
    public override void OnPlay(ref BlitzExec x)
    {
        decimal gained = BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, BlitzCardEffects.Var(x, 0),
            BlitzValueProps.Move, fromCard: true);
        if (x.NeedsChoice || gained <= 0m)
            return;
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.BlockNextTurnPower>(), (int)gained, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("PIERCING_WAIL")]
public sealed class PiercingWail : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    // CorePowerSupport.ApplyCardPowers.PiercingWail: ApplyPiercingWail to every hittable enemy.
    public override void OnPlay(ref BlitzExec x)
    {
        ushort power = BlitzRegistry.PowerId<Powers.PiercingWailPower>();
        int amount = (int)BlitzCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, enemy))
                continue;
            BlitzKernel.ApplyTemporaryStrength(ref x, enemy, power, amount, BlitzLimits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("BLADE_DANCE")]
public sealed class BladeDance : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref BlitzExec x)
        => SilentContent.GenerateShivs(ref x, (int)BlitzCardEffects.Var(x, 0), upgraded: false);
}

[BlitzContent("CLOAK_AND_DAGGER")]
public sealed class CloakAndDagger : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)BlitzCardEffects.Var(x, 1), upgraded: false);
    }
}

[BlitzContent("BACKFLIP")]
public sealed class Backflip : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref BlitzExec x)
    {
        BlitzCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        BlitzKernel.Draw(ref x, BlitzCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[BlitzContent("DEFLECT")]
public sealed class Deflect : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}

[BlitzContent("ANTICIPATE")]
public sealed class Anticipate : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // SimulatedCombatState.ApplyAnticipate: apply the temporary power, then give Dexterity equal to
    // the amount that actually stuck (Artifact/stacking included).
    public override void OnPlay(ref BlitzExec x)
    {
        ushort temporary = BlitzRegistry.PowerId<AnticipatePower>();
        int applied = BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, temporary,
            (int)BlitzCardEffects.Var(x, 0), BlitzLimits.PlayerOwner);
        if (applied > 0)
            BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<DexterityPower>(),
                applied, BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("UNTOUCHABLE")]
public sealed class Untouchable : BlitzCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref BlitzExec x) => BlitzCardEffects.GeneralBlock(ref x, 0);
}
