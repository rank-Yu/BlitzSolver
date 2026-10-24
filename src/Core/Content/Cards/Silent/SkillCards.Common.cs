using CombatSolver.Flash.Content.Powers;
using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Cards.Silent;

[FlashContent("PREPARED")]
public sealed class Prepared : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref FlashExec x)
    {
        // GetSpec: Prepared → Discard, Hand, Cards, every hand card. The bounds are clamped to the
        // option count by ChooseCardsInRange, as CardChoiceSupport.BuildChoices does.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        int count = (int)FlashCardEffects.Var(x, 0);
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Discard, FlashPile.Hand, options, count, selected,
                out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], 0);
    }
}

[FlashContent("DEADLY_POISON")]
public sealed class DeadlyPoison : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 0);
}

[FlashContent("SNAKEBITE")]
public sealed class Snakebite : FlashCardDef
{
    // Snakebite.cs: a Retain Skill that applies Poison; it has no damage variable.
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.PoisonPower>(), 0);
}

[FlashContent("DODGE_AND_ROLL")]
public sealed class DodgeAndRoll : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CorePowerSupport.ApplyCardPowers.DodgeAndRoll: the block actually gained becomes Unpowered
    // block next turn (BlockNextTurnPower).
    public override void OnPlay(ref FlashExec x)
    {
        decimal gained = FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, FlashCardEffects.Var(x, 0),
            FlashValueProps.Move, fromCard: true);
        if (x.NeedsChoice || gained <= 0m)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.BlockNextTurnPower>(), (int)gained, FlashLimits.PlayerOwner);
    }
}

[FlashContent("PIERCING_WAIL")]
public sealed class PiercingWail : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    // CorePowerSupport.ApplyCardPowers.PiercingWail: ApplyPiercingWail to every hittable enemy.
    public override void OnPlay(ref FlashExec x)
    {
        ushort power = FlashRegistry.PowerId<Powers.PiercingWailPower>();
        int amount = (int)FlashCardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, enemy))
                continue;
            FlashKernel.ApplyTemporaryStrength(ref x, enemy, power, amount, FlashLimits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("BLADE_DANCE")]
public sealed class BladeDance : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
        => SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 0), upgraded: false);
}

[FlashContent("CLOAK_AND_DAGGER")]
public sealed class CloakAndDagger : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)FlashCardEffects.Var(x, 1), upgraded: false);
    }
}

[FlashContent("BACKFLIP")]
public sealed class Backflip : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, FlashCardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[FlashContent("DEFLECT")]
public sealed class Deflect : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}

[FlashContent("ANTICIPATE")]
public sealed class Anticipate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // SimulatedCombatState.ApplyAnticipate: apply the temporary power, then give Dexterity equal to
    // the amount that actually stuck (Artifact/stacking included).
    public override void OnPlay(ref FlashExec x)
    {
        ushort temporary = FlashRegistry.PowerId<AnticipatePower>();
        int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, temporary,
            (int)FlashCardEffects.Var(x, 0), FlashLimits.PlayerOwner);
        if (applied > 0)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<DexterityPower>(),
                applied, FlashLimits.PlayerOwner);
    }
}

[FlashContent("UNTOUCHABLE")]
public sealed class Untouchable : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);
}
