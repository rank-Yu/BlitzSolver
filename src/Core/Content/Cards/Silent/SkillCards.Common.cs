using BlitzSolver.Content.Powers;
using BlitzSolver.Engine;

namespace BlitzSolver.Content.Cards.Silent;

[ContentId("PREPARED")]
public sealed class Prepared : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref Exec x)
        => Kernel.Draw(ref x, CardEffects.Var(x, 0), fromHandDraw: false);

    public override void OwnChoice(ref Exec x)
    {
        // GetSpec: Prepared → Discard, Hand, Cards, every hand card. The bounds are clamped to the
        // option count by ChooseCardsInRange, as CardChoiceSupport.BuildChoices does.
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        int count = (int)CardEffects.Var(x, 0);
        if (!Kernel.ChooseCards(ref x, ChoiceEffect.Discard, Pile.Hand, options, count, selected,
                out int chosen))
        {
            return;
        }
        Kernel.DiscardAndDraw(ref x, Pile.Hand, selected[..chosen], 0);
    }
}

[ContentId("DEADLY_POISON")]
public sealed class DeadlyPoison : CardDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToTarget(ref x, Registry.PowerId<Powers.PoisonPower>(), 0);
}

[ContentId("SNAKEBITE")]
public sealed class Snakebite : CardDef
{
    // Snakebite.cs: a Retain Skill that applies Poison; it has no damage variable.
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnPlay(ref Exec x)
        => CardEffects.ApplyToTarget(ref x, Registry.PowerId<Powers.PoisonPower>(), 0);
}

[ContentId("DODGE_AND_ROLL")]
public sealed class DodgeAndRoll : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // CorePowerSupport.ApplyCardPowers.DodgeAndRoll: the block actually gained becomes Unpowered
    // block next turn (BlockNextTurnPower).
    public override void OnPlay(ref Exec x)
    {
        decimal gained = Kernel.GainBlock(ref x, Limits.PlayerOwner, CardEffects.Var(x, 0),
            ValueProps.Move, fromCard: true);
        if (x.NeedsChoice || gained <= 0m)
            return;
        Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.BlockNextTurnPower>(), (int)gained, Limits.PlayerOwner);
    }
}

[ContentId("PIERCING_WAIL")]
public sealed class PiercingWail : CardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    // CorePowerSupport.ApplyCardPowers.PiercingWail: ApplyPiercingWail to every hittable enemy.
    public override void OnPlay(ref Exec x)
    {
        ushort power = Registry.PowerId<Powers.PiercingWailPower>();
        int amount = (int)CardEffects.Var(x, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte enemy = Kernel.EnemyOwner(x.B.Roster[position]);
            if (!Kernel.IsHittable(x.B, enemy))
                continue;
            Kernel.ApplyTemporaryStrength(ref x, enemy, power, amount, Limits.PlayerOwner, -1);
            if (x.NeedsChoice)
                return;
        }
    }
}

[ContentId("BLADE_DANCE")]
public sealed class BladeDance : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref Exec x)
        => SilentContent.GenerateShivs(ref x, (int)CardEffects.Var(x, 0), upgraded: false);
}

[ContentId("CLOAK_AND_DAGGER")]
public sealed class CloakAndDagger : ShivGeneratingCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        SilentContent.GenerateShivs(ref x, (int)CardEffects.Var(x, 1), upgraded: false);
    }
}

[ContentId("BACKFLIP")]
public sealed class Backflip : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override void OnPlay(ref Exec x)
    {
        CardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        Kernel.Draw(ref x, CardEffects.Var(x, 1), fromHandDraw: false);
    }
}

[ContentId("DEFLECT")]
public sealed class Deflect : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}

[ContentId("ANTICIPATE")]
public sealed class Anticipate : CardDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    // Native TemporaryDexterityPower.BeforeApplied applies Dexterity before PowerCmd.Apply inserts
    // the temporary power row, so the row order is [DEXTERITY_POWER, ANTICIPATE_POWER].
    public override void OnPlay(ref Exec x)
        => Kernel.ApplyTemporaryDexterity(ref x, Limits.PlayerOwner,
            Registry.PowerId<AnticipatePower>(), (int)CardEffects.Var(x, 0), Limits.PlayerOwner);
}

[ContentId("UNTOUCHABLE")]
public sealed class Untouchable : CardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref Exec x) => CardEffects.GeneralBlock(ref x, 0);
}
