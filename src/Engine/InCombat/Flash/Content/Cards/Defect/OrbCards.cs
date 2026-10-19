namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Defect;

// Defect orb cards whose existing-engine mirrors are OrbCardMirrors. Compensation powers are applied
// in the same relative order the existing engine uses: the mirror (attack/block/channel/evoke) first,
// then CorePowerSupport / CardEffectSpecRegistry / CardPowerOnPlaySupport.

[FlashContent("GLACIER")]
public sealed class Glacier : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("FROST_ORB"), 2);
    }
}

[FlashContent("GLASSWORK")]
public sealed class Glasswork : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("GLASS_ORB"));
    }
}

[FlashContent("ICE_LANCE")]
public sealed class IceLance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("FROST_ORB"), (int)FlashCardEffects.Var(x, 1));
    }
}

[FlashContent("FUSION")]
public sealed class Fusion : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("PLASMA_ORB"));
}

[FlashContent("RAINBOW")]
public sealed class Rainbow : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("LIGHTNING_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("FROST_ORB"));
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
    }
}

[FlashContent("CHILL")]
public sealed class Chill : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        // OrbCardMirrors.ChillOnPlay: one Frost per hittable enemy, counted when the card resolves.
        int count = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (FlashKernel.IsHittable(x.B, owner))
                count++;
        }
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("FROST_ORB"), count);
    }
}

[FlashContent("CHAOS")]
public sealed class Chaos : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnPlay(ref FlashExec x)
    {
        int count = (int)FlashCardEffects.Var(x, 0);
        for (int index = 0; index < count; index++)
        {
            ushort orb = FlashKernel.RandomOrb(ref x.B);
            if (!FlashKernel.ChannelOrb(ref x, orb))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("DARKNESS")]
public sealed class Darkness : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
        if (x.NeedsChoice)
            return;
        // OrbCardMirrors.DarknessOnPlay: every Dark orb in the resulting queue triggers Passive once
        // (twice when upgraded). OrbCmd.Passive, so no ModifyOrbPassiveTriggerCounts.
        Span<int> dark = stackalloc int[FlashLimits.Orbs];
        int count = DarkIndices(x.B, dark);
        int triggers = x.Card.Upgrade > 0 ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            for (int trigger = 0; trigger < triggers; trigger++)
            {
                FlashKernel.OrbPassiveAt(ref x, dark[index]);
                if (x.NeedsChoice)
                    return;
            }
        }
    }

    private static int DarkIndices(in FlashBattle battle, Span<int> output)
    {
        if (!FlashRegistry.TryOrb("DARK_ORB", out ushort id))
            return 0;
        int count = 0;
        for (int index = 0; index < battle.OrbCount; index++)
        {
            if (battle.Orbs[index].Id == id)
                output[count++] = index;
        }
        return count;
    }
}

[FlashContent("SHATTER")]
public sealed class Shatter : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int orbs = x.B.OrbCount;
        for (int index = 0; index < orbs; index++)
        {
            FlashKernel.EvokeNext(ref x, repeat: 2);
            if (x.NeedsChoice)
                return;
        }
    }
}

[FlashContent("MULTI_CAST")]
public sealed class MultiCast : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        int count = FlashKernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        FlashKernel.EvokeNext(ref x, repeat: count);
    }
}

[FlashContent("TEMPEST")]
public sealed class Tempest : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        int count = FlashKernel.ResolveEnergyXValue(ref x);
        if (x.Card.Upgrade > 0)
            count++;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("LIGHTNING_ORB"), count);
    }
}

[FlashContent("TESLA_COIL")]
public sealed class TeslaCoil : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        Span<int> lightning = stackalloc int[FlashLimits.Orbs];
        if (!FlashRegistry.TryOrb("LIGHTNING_ORB", out ushort id))
            return;
        int count = 0;
        for (int index = 0; index < x.B.OrbCount; index++)
        {
            if (x.B.Orbs[index].Id == id)
                lightning[count++] = index;
        }
        int triggers = x.Card.Upgrade > 0 ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            for (int trigger = 0; trigger < triggers; trigger++)
            {
                FlashKernel.OrbPassiveAt(ref x, lightning[index], x.Target);
                if (x.NeedsChoice)
                    return;
            }
        }
    }
}

[FlashContent("REFRACT")]
public sealed class Refract : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.Attack(ref x, FlashCardEffects.Var(x, 1), hitCount: 2);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("GLASS_ORB"), (int)FlashCardEffects.Var(x, 0));
    }
}

[FlashContent("NULL")]
public sealed class Null : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        // OrbCardMirrors.NullOnPlay mirrors attack then channel; CorePowerSupport applies Weak after.
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ChannelOrb(ref x, FlashKernel.OrbId("DARK_ORB"));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

[FlashContent("LIGHTNING_ROD")]
public sealed class LightningRod : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "LightningRodPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.LightningRodPower>(), 1);
    }
}

[FlashContent("CONSUMING_SHADOW")]
public sealed class ConsumingShadow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat", "ConsumingShadowPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("DARK_ORB"), (int)FlashCardEffects.Var(x, 0));
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToOwner(ref x, FlashRegistry.PowerId<Powers.ConsumingShadowPower>(), 1);
    }
}
