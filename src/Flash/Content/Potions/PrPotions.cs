using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

// The nine vanilla potions the FI inventory found missing. Each OnUse is the potion's branch in
// PotionOnUseSupport.Use (or CardGenerationPotionMirrors for CosmicConcoction), line by line; every
// number comes from the potion's compiled rule-table variables.

/// <summary>
/// <c>Ambergris</c>: heal <c>MaxHp * HealPercent / 100</c>, then apply 1 <c>AmbergrisPower</c>
/// (<c>PotionOnUseSupport.Use</c> case Ambergris; the power grants the extra turn).
/// </summary>
[FlashContent("AMBERGRIS")]
public sealed class AmbergrisPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    public override void OnUse(ref FlashExec x)
    {
        decimal percent = FlashKernel.PotionVar(x, this, 0);
        FlashKernel.Heal(ref x, FlashLimits.PlayerOwner, x.B.Creature(FlashLimits.PlayerOwner).MaxHp * percent / 100m);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.AmbergrisPower>(), 1, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>BoneBrew</c>: <c>combat.SummonOsty(simulator, PlayerTarget(), Summon)</c>
/// (<c>PotionOnUseSupport.Use</c> case BoneBrew).
/// </summary>
[FlashContent("BONE_BREW")]
public sealed class BoneBrewPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)FlashKernel.PotionVar(x, this, 0));
    }
}

/// <summary>
/// <c>CosmicConcoction</c>: <c>Cards</c> distinct colourless cards, each upgraded and added to hand
/// (<c>CardGenerationPotionMirrors.Generate</c> / <c>PotionOnUseSupport.Use</c> case CosmicConcoction).
/// </summary>
[FlashContent("COSMIC_CONCOCTION")]
public sealed class CosmicConcoctionPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnUse(ref FlashExec x)
    {
        int count = (int)FlashKernel.PotionVar(x, this, 0);
        if (count <= 0)
            return;
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("GeneratedCards", FlashLimits.Cards);
        Span<FlashCard> selected = stackalloc FlashCard[count];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.ColorlessAll, count,
            distinct: true, FlashRngStream.CombatCardGeneration, selected);
        for (int index = 0; index < sampled; index++)
        {
            FlashCard card = selected[index];
            FlashKernel.UpgradeCard(x.R, ref card);
            if (!FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>CunningPotion</c>: <c>CardPileOnPlaySupport.GenerateShivs(..., Cards, upgraded: true)</c>
/// (<c>PotionOnUseSupport.Use</c> case CunningPotion).
/// </summary>
[FlashContent("CUNNING_POTION")]
public sealed class CunningPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SHIV", 1)];

    public override void OnUse(ref FlashExec x)
    {
        Cards.Silent.SilentContent.GenerateShivs(ref x, (int)FlashKernel.PotionVar(x, this, 0), upgraded: true);
    }
}

/// <summary><c>FocusPotion</c>: <c>combat.Apply&lt;FocusPower&gt;(player, FocusPower, owner)</c>.</summary>
[FlashContent("FOCUS_POTION")]
public sealed class FocusPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["FocusPower"];

    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.FocusPower>(), (int)FlashKernel.PotionVar(x, this, 0),
            FlashLimits.PlayerOwner);
    }
}

/// <summary><c>PoisonPotion</c>: applies Poison to the chosen enemy (case PoisonPotion when target != null).</summary>
[FlashContent("POISON_POTION")]
public sealed class PoisonPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.PoisonPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>PotionOfCapacity</c>: <c>simulator.AddOrbSlots(PlayerTarget(), Repeat)</c>.</summary>
[FlashContent("POTION_OF_CAPACITY")]
public sealed class PotionOfCapacity : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.AddOrbSlots(ref x.B, (int)FlashKernel.PotionVar(x, this, 0));
    }
}

/// <summary><c>PotionOfDoom</c>: applies Doom to the chosen enemy (case PotionOfDoom when target != null).</summary>
[FlashContent("POTION_OF_DOOM")]
public sealed class PotionOfDoom : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DoomPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>PotOfGhouls</c>: creates <c>Cards</c> Soul cards and adds them to the hand bottom
/// (<c>PotionOnUseSupport.Use</c> case PotOfGhouls, CardGenerationResultKind.Fixed).
/// </summary>
[FlashContent("POT_OF_GHOULS")]
public sealed class PotOfGhouls : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnUse(ref FlashExec x)
    {
        int count = (int)FlashKernel.PotionVar(x, this, 0);
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Necrobinder.Soul>());
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}
