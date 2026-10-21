using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

[FlashContent("FYSH_OIL")]
public sealed class FyshOil : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    // FyshOil: Apply<StrengthPower>(player, Strength, owner), then Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("HEART_OF_IRON")]
public sealed class HeartOfIron : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    // HeartOfIron: combat.Apply<PlatingPower>(player, PlatingPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("LIQUID_BRONZE")]
public sealed class LiquidBronze : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // LiquidBronze: combat.Apply<ThornsPower>(player, ThornsPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ThornsPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("POTION_OF_BINDING")]
public sealed class PotionOfBinding : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower", "WeakPower"];

    // PotionOfBinding: for each hittable enemy, apply Weak with the VulnerablePower amount and
    // Vulnerable with the WeakPower amount (the existing engine's branch swaps the two variables).
    public override void OnUse(ref FlashExec x)
    {
        int weak = (int)FlashKernel.PotionVar(x, this, 0);
        int vulnerable = (int)FlashKernel.PotionVar(x, this, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.WeakPower>(), weak,
                FlashLimits.PlayerOwner);
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.VulnerablePower>(), vulnerable,
                FlashLimits.PlayerOwner);
        }
    }
}

[FlashContent("POWDERED_DEMISE")]
public sealed class PowderedDemise : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Demise"];

    // PowderedDemise when target != null: combat.Apply<DemisePower>(target, Demise, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DemisePower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("DUPLICATOR")]
public sealed class Duplicator : FlashPotionDef
{
    // PotionOnUseSupport.Use, Duplicator case: Apply<DuplicationPower>(player, 1, player).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DuplicationPower>(), 1, FlashLimits.PlayerOwner);
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

[FlashContent("ASHWATER")]
public sealed class Ashwater : FlashPotionDef
{
    // Ashwater is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // hand-selection exhaust.
    public override void OnUse(ref FlashExec x)
    {
    }
}

[FlashContent("BLESSING_OF_THE_FORGE")]
public sealed class BlessingOfTheForge : FlashPotionDef
{
    // BlessingOfTheForge: upgrade every upgradable card in the player's hand.
    public override void OnUse(ref FlashExec x)
    {
        for (int index = 0; index < x.B.HandCount; index++)
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }
}

[FlashContent("CLARITY")]
public sealed class Clarity : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ClarityPower", "Cards"];

    // Clarity: Draw(player, Cards), then Apply<ClarityPower>(player, ClarityPower, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 1), fromHandDraw: false);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ClarityPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("CURE_ALL")]
public sealed class CureAll : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CureAll: GainEnergy(player, Energy), then Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashKernel.PotionVar(x, this, 0));
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 1), fromHandDraw: false);
    }
}

[FlashContent("FORTIFIER")]
public sealed class Fortifier : FlashPotionDef
{
    // Fortifier: GainBlock(player, player.Block * 2, Unpowered).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.B.Player.Block * 2,
            FlashValueProps.Unpowered, fromCard: false);
}

[FlashContent("GAMBLERS_BREW")]
public sealed class GamblersBrew : FlashPotionDef
{
    // GamblersBrew is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // discard-and-draw hand selection.
    public override void OnUse(ref FlashExec x)
    {
    }
}

[FlashContent("RADIANT_TINCTURE")]
public sealed class RadiantTincture : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "RadiancePower"];

    // RadiantTincture: GainEnergy(player, Energy), then Apply<RadiancePower>(player, RadiancePower, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashKernel.PotionVar(x, this, 0));
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RadiancePower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("REGEN_POTION")]
public sealed class RegenPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["RegenPower"];

    // RegenPotion: combat.Apply<RegenPower>(player, RegenPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RegenPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("STABLE_SERUM")]
public sealed class StableSerum : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // StableSerum: combat.Apply<RetainHandPower>(player, Repeat, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("TOUCH_OF_INSANITY")]
public sealed class TouchOfInsanity : FlashPotionDef
{
    // TouchOfInsanity is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // hand selection that makes a card free for the combat.
    public override void OnUse(ref FlashExec x)
    {
    }
}

[FlashContent("KINGS_COURAGE")]
public sealed class KingsCourage : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards =>
        [(FlashKernel.SovereignBladeId, 0)];

    // KingsCourage: ForgeCmd.Forge(Forge variable, target.Player, this); the single-player target is
    // the player (PotionOnUseSupport.Use case KingsCourage).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.Forge(ref x, (int)FlashKernel.PotionVar(x, this, 0));
    }
}
