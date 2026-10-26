// Opening-combat effects that native models run before the captured play-phase root. These are the
// blitz-side counterparts of the pre-root imports documented in docs/content-table.md; the values
// are the native canonical bases (the same values the content table exports for the rules).
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Content.Relics;

internal static class StartVars
{
    public static int Int(ref Exec x, Listener self, string name)
        => CombatStartValues.IntRelicVar(ref x, self, name);
}

public sealed partial class Anchor : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
        => Kernel.GainBlock(ref x, Limits.PlayerOwner, StartVars.Int(ref x, self, "Block"), ValueProps.Unpowered, fromCard: false);
}

public sealed partial class FakeAnchor : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
        => Kernel.GainBlock(ref x, Limits.PlayerOwner, StartVars.Int(ref x, self, "Block"), ValueProps.Unpowered, fromCard: false);
}

public sealed partial class BronzeScales : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ThornsPower>(), StartVars.Int(ref x, self, "ThornsPower"),
            Limits.PlayerOwner);
}

public sealed partial class DataDisk : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.FocusPower>(), StartVars.Int(ref x, self, "FocusPower"),
            Limits.PlayerOwner);
}

public sealed partial class Gorget : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PlatingPower>(), StartVars.Int(ref x, self, "PlatingPower"),
            Limits.PlayerOwner);
}

public sealed partial class OddlySmoothStone : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DexterityPower>(), StartVars.Int(ref x, self, "DexterityPower"),
            Limits.PlayerOwner);
}

public sealed partial class Vajra : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(), StartVars.Int(ref x, self, "StrengthPower"),
            Limits.PlayerOwner);
}

public sealed partial class SwordOfJade : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(), StartVars.Int(ref x, self, "StrengthPower"),
            Limits.PlayerOwner);
}

public sealed partial class SlingOfCourage : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
    {
        if (elite)
            Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(), StartVars.Int(ref x, self, "StrengthPower"),
                Limits.PlayerOwner);
    }
}

public sealed partial class DivineRight : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.GainStars(ref x, StartVars.Int(ref x, self, "Stars"));
}

public sealed partial class PhilosophersStone : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
    {
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            if (x.B.Enemies[index].Creature.IsAlive)
                Kernel.ApplyPower(ref x, Kernel.EnemyOwner(checked((byte)index)),
                    Registry.PowerId<Powers.StrengthPower>(), StartVars.Int(ref x, self, "StrengthPower"), Limits.NoOwner);
        }
    }
}

public sealed partial class RedSkull : RelicDef
{
    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss)
    {
        decimal threshold = content.RelicVar(relicId, "HpThreshold") / 100m;
        return battle.Player.CurrentHp <= battle.Player.MaxHp * threshold ? (sbyte)1 : (sbyte)0;
    }

    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
    {
        decimal threshold = CombatStartValues.RelicVar(ref x, self, "HpThreshold") / 100m;
        if (x.B.Player.CurrentHp <= x.B.Player.MaxHp * threshold)
            Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(), StartVars.Int(ref x, self, "StrengthPower"),
                Limits.PlayerOwner);
    }
}

public sealed partial class BeltBuckle : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
    {
        for (int slot = 0; slot < x.B.PotionSlotCount; slot++)
        {
            if (x.B.Potions[slot] != 0)
                return;
        }
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DexterityPower>(), StartVars.Int(ref x, self, "DexterityPower"),
            Limits.NoOwner);
        x.B.Relics[self.Index].Counter = 1;
    }

    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss)
    {
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                return 0;
        }
        return 1;
    }
}

public sealed partial class SneckoEye : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ConfusedPower>(), StartVars.Int(ref x, self, "ConfusedPower"),
            Limits.PlayerOwner);
}

public sealed partial class FakeSneckoEye : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ConfusedPower>(), StartVars.Int(ref x, self, "ConfusedPower"),
            Limits.PlayerOwner);
}

public sealed partial class BoundPhylactery : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
        => Kernel.SummonOsty(ref x, StartVars.Int(ref x, self, "Summon"));
}

public sealed partial class PhylacteryUnbound : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
        => Kernel.SummonOsty(ref x, StartVars.Int(ref x, self, "StartOfCombat"));
}

public sealed partial class BagOfMarbles : RelicDef
{
    public override void BeforeSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side != Side.Player || x.B.PlayerTurn > 1)
            return;
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            if (x.B.Enemies[index].Creature.IsAlive)
                Kernel.ApplyPower(ref x, Kernel.EnemyOwner(checked((byte)index)),
                    Registry.PowerId<Powers.VulnerablePower>(), StartVars.Int(ref x, self, "VulnerablePower"), Limits.PlayerOwner);
        }
    }
}

public sealed partial class RedMask : RelicDef
{
    public override void BeforeSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side != Side.Player || x.B.PlayerTurn > 1)
            return;
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            if (x.B.Enemies[index].Creature.IsAlive)
                Kernel.ApplyPower(ref x, Kernel.EnemyOwner(checked((byte)index)),
                    Registry.PowerId<Powers.WeakPower>(), StartVars.Int(ref x, self, "WeakPower"), Limits.PlayerOwner);
        }
    }
}

public sealed partial class BagOfPreparation : RelicDef
{
    public override decimal ModifyHandDraw(ref Exec x, Listener self, decimal amount)
        => !x.CombatStart ? amount : x.B.PlayerTurn <= 1 ? amount + StartVars.Int(ref x, self, "Cards") : amount;
}

public sealed partial class BloodVial : RelicDef
{
    public override void AfterPlayerTurnStart(ref Exec x, Listener self)
    {
        if (!x.CombatStart)
            return;
        if (x.B.PlayerTurn <= 1)
            Kernel.Heal(ref x, Limits.PlayerOwner, StartVars.Int(ref x, self, "Heal"));
    }
}

public sealed partial class Lantern : RelicDef
{
    public override void AfterSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side == Side.Player && x.B.PlayerTurn <= 1)
            Kernel.GainEnergy(ref x, StartVars.Int(ref x, self, "Energy"));
    }
}

public sealed partial class VeryHotCocoa : RelicDef
{
    public override void AfterSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side == Side.Player && x.B.PlayerTurn <= 1)
            Kernel.GainEnergy(ref x, StartVars.Int(ref x, self, "Energy"));
    }
}

public sealed partial class FurCoat : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
    {
        if (!x.R.FurCoatSpawnHpOne)
            return;
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            ref Creature enemy = ref x.B.Enemies[index].Creature;
            if (enemy.IsAlive)
                enemy.CurrentHp = StartVars.Int(ref x, self, "SpawnHp");
        }
    }
}

public sealed partial class GhostSeed : RelicDef
{
    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
    {
        for (int index = 0; index < x.B.CardCount; index++)
        {
            ref Card card = ref x.B.Cards[index];
            CardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (stats.Rarity != CardRarity.Basic
                || (stats.Tags & (CardTag.Strike | CardTag.Defend)) == 0
                || (card.Keywords & Keywords.Ethereal) != 0)
            {
                continue;
            }
            card.Keywords |= Keywords.Ethereal;
        }
    }
}

public sealed partial class Pantograph : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
    {
        if (x.FirstRoomIsBoss)
            Kernel.Heal(ref x, Limits.PlayerOwner, StartVars.Int(ref x, self, "Heal"));
    }
}

public sealed partial class StoneCracker : RelicDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
    {
        int start = Piles.Start(x.B, Pile.Draw);
        int count = x.B.DrawCount;
        Span<int> positions = stackalloc int[count];
        int candidates = 0;
        for (int index = 0; index < count; index++)
        {
            ref readonly Card card = ref x.B.Cards[start + index];
            if (card.Upgrade < x.R.Card(card.Id, card.Upgrade).MaxUpgradeLevel)
                positions[candidates++] = index;
        }
        for (int index = 1; index < candidates; index++)
        {
            int value = positions[index];
            int move = index;
            while (move > 0 && CompareDrawCards(in x.B, start + positions[move - 1], start + value) > 0)
            {
                positions[move] = positions[move - 1];
                move--;
            }
            positions[move] = value;
        }
        ref RngState rng = ref x.B.RngStream(RngStream.CombatCardSelection);
        for (int index = candidates - 1; index > 0; index--)
        {
            int swap = rng.NextInt(index + 1);
            (positions[index], positions[swap]) = (positions[swap], positions[index]);
        }
        int upgrade = Math.Min(StartVars.Int(ref x, self, "Cards"), candidates);
        for (int index = 0; index < upgrade; index++)
        {
            int position = start + positions[index];
            ref Card card = ref x.B.Cards[position];
            card.Upgrade = checked((byte)x.R.Card(card.Id, card.Upgrade).MaxUpgradeLevel);
            CardStats upgraded = x.R.Card(card.Id, card.Upgrade);
            card.BaseCost = checked((sbyte)upgraded.EnergyCost);
            card.BaseStarCost = checked((sbyte)upgraded.StarCost);
        }
    }

    private static int CompareDrawCards(in Battle battle, int left, int right)
    {
        int result = battle.Cards[left].Id.CompareTo(battle.Cards[right].Id);
        if (result != 0)
            return result;
        result = battle.Cards[left].Upgrade.CompareTo(battle.Cards[right].Upgrade);
        if (result != 0)
            return result;
        return battle.Cards[left].EnchantmentId.CompareTo(battle.Cards[right].EnchantmentId);
    }
}

public sealed partial class Bellows : RelicDef
{
    public override void AfterPlayerTurnStart(ref Exec x, Listener self)
    {
        if (x.B.PlayerTurn > 1)
            return;
        int start = Piles.Start(x.B, Pile.Hand);
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref Card card = ref x.B.Cards[start + index];
            int max = x.R.Card(card.Id, card.Upgrade).MaxUpgradeLevel;
            if (card.Upgrade < max)
            {
                card.Upgrade = checked((byte)max);
                CardStats upgraded = x.R.Card(card.Id, card.Upgrade);
                card.BaseCost = checked((sbyte)upgraded.EnergyCost);
                card.BaseStarCost = checked((sbyte)upgraded.StarCost);
            }
        }
    }
}

public sealed partial class PetrifiedToad : RelicDef
{
    public override void BeforeCombatStartLate(ref Exec x, Listener self)
    {
        if (!Registry.TryPotion("POTION_SHAPED_ROCK", out ushort potion))
            throw new UnsupportedException("PetrifiedToad 需要 POTION_SHAPED_ROCK 定义。");
        for (int slot = 0; slot < x.B.PotionSlotCount; slot++)
        {
            if (x.B.Potions[slot] != 0)
                continue;
            x.B.Potions[slot] = checked((ushort)(potion + 1));
            return;
        }
    }
}

public sealed partial class Metronome : RelicDef
{
    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss) => 0;
}

public sealed partial class Bread : RelicDef
{
    public override void AfterSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side == Side.Player && x.B.PlayerTurn <= 1)
            Kernel.LoseEnergy(ref x, StartVars.Int(ref x, self, "LoseEnergy"));
    }
}

public sealed partial class TwistedFunnel : RelicDef
{
    public override void BeforeSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side != Side.Player || x.B.PlayerTurn > 1)
            return;
        for (int index = 0; index < x.B.EnemyCount; index++)
        {
            if (x.B.Enemies[index].Creature.IsAlive)
                Kernel.ApplyPower(ref x, Kernel.EnemyOwner(checked((byte)index)),
                    Registry.PowerId<Powers.PoisonPower>(), StartVars.Int(ref x, self, "PoisonPower"), Limits.PlayerOwner);
        }
    }
}

public sealed partial class EmberTea : RelicDef
{
    public override IReadOnlyList<string> VarNames => ["Combats", "StrengthPower"];

    public override void AfterRoomEntered(ref Exec x, Listener self, bool elite, bool boss)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            StartVars.Int(ref x, self, "StrengthPower"), Limits.NoOwner);
}

public sealed partial class FuneraryMask : RelicDef
{
    public override void BeforeHandDraw(ref Exec x, Listener self)
    {
        if (x.B.PlayerTurn > 1)
            return;
        for (int index = 0; index < StartVars.Int(ref x, self, "Cards"); index++)
        {
            Kernel.AddGeneratedCard(ref x, Kernel.CreateCard(x.R,
                Registry.CardId<BlitzSolver.Content.Cards.Necrobinder.Soul>()), Pile.Draw, PilePosition.Random,
                creator: Limits.PlayerOwner);
        }
    }
}

public sealed partial class FencingManual : RelicDef
{
    public override void AfterSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side == Side.Player && x.B.PlayerTurn <= 1)
            Kernel.Forge(ref x, StartVars.Int(ref x, self, "Forge"));
    }
}

public sealed partial class Akabeko : RelicDef
{
    public override void AfterSideTurnStart(ref Exec x, Listener self, Side side)
    {
        if (!x.CombatStart)
            return;
        if (side == Side.Player && x.B.PlayerTurn <= 1)
            Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.VigorPower>(), StartVars.Int(ref x, self, "VigorPower"),
                Limits.PlayerOwner);
    }
}

public sealed partial class FestivePopper : RelicDef
{
    public override void AfterPlayerTurnStart(ref Exec x, Listener self)
    {
        if (!x.CombatStart)
            return;
        if (x.B.PlayerTurn <= 1)
            CardEffects.DamageAllEnemies(ref x, StartVars.Int(ref x, self, "Damage"), ValueProps.Unpowered, Limits.PlayerOwner);
    }
}

public sealed partial class BurningSticks : RelicDef
{
    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss) => 1;
}

public sealed partial class ThrowingAxe : RelicDef
{
    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss) => 1;
}

public sealed partial class UnsettlingLamp : RelicDef
{
    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss) => 1;
}

public sealed partial class Vambrace : RelicDef
{
    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content, bool elite, bool boss) => 1;
}

public sealed partial class MeatOnTheBone : RelicDef
{
    public override void BeforeCombatStart(ref Exec x, Listener self)
    {
        // The relic's status is display-only; no battle state changes.
    }

    public override sbyte RoomEnteredStatus(in Battle battle, ushort relicId, ICombatStartContent content,
        bool elite, bool boss)
    {
        decimal threshold = content.RelicVar(relicId, "HpThreshold") / 100m;
        return battle.Player.CurrentHp <= battle.Player.MaxHp * threshold ? (sbyte)1 : (sbyte)-1;
    }
}
