// Generated from tests/Golden/data/d1/*/config.br (all enabled D1 bounds share these arrays).
// Source: CombatSolver a62f1850 FlashBoundBridge classification of the native content tables.
// The arrays are the exact per-content results the D1 export froze; M2a-4 does not re-derive
// the predicates. Every enabled config (384/410) was compared byte for byte.
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Ai.Combat;

namespace Sts2Solver.Import;

/// <summary>Frozen healing-bound classification tables, indexed by Sts2Solver content id.</summary>
internal static class HealingBoundTables
{
    private static readonly int[] UnsafeCards = [1, 10, 16, 17, 61, 73, 74, 79, 91, 153, 155, 160, 166, 170, 173, 175, 186, 191, 192, 211, 214, 241, 252, 257, 285, 292, 295, 302, 306, 310, 325, 329, 332, 342, 353, 393, 399, 405, 408, 448, 453, 464, 468, 470, 507, 518, 521, 545, 548, 550];

    private static readonly int[] UnsafePowers = [2, 5, 9, 11, 12, 14, 21, 26, 30, 32, 38, 43, 45, 56, 65, 68, 71, 76, 87, 92, 93, 94, 96, 97, 98, 100, 101, 104, 105, 106, 108, 121, 122, 127, 129, 138, 141, 143, 148, 150, 151, 156, 157, 160, 163, 165, 168, 170, 172, 179, 184, 186, 187, 188, 189, 190, 192, 200, 201, 207, 209, 210, 211, 215, 216, 217, 220, 222, 226, 237, 240];

    private static readonly int[] UnsafeRelics = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 34, 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 75, 76, 78, 79, 80, 81, 82, 83, 84, 85, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 115, 116, 117, 118, 119, 120, 121, 122, 124, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 171, 172, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 194, 195, 196, 197, 198, 199, 200, 201, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 237, 238, 239, 240, 241, 242, 243, 245, 246, 247, 248, 249, 250, 251, 252, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 275, 276, 277, 278, 280, 281, 282, 284, 286, 287, 289, 290, 291, 292, 293, 294, 295, 296];

    private static readonly int[] UnsafeMonsters = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 39, 40, 41, 42, 43, 44, 46, 47, 48, 49, 50, 51, 52, 53, 54, 56, 57, 58, 59, 60, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107];

    private static readonly int[] UnsafePotions = [0, 6, 14, 20, 23, 29];

    private static readonly int[] SafeEnchantments = [6, 7, 13];

    private static readonly int[] SafeAfflictions = [6];

    private static readonly int[] KnownBlockingCards = [191, 332];

    private static readonly int[] KnownBlockingRelics = [26, 27, 46, 53, 57, 65, 136];

    private static readonly int[] RegenPowers = [162];

    private static readonly int[] ReattachPowers = [159];

    private static readonly int[] SwipePowers = [211];

    private static readonly int[] RegenPotions = [51];

    private static readonly int[] DirectHealPotions = [0, 6];

    private static readonly int[] FruitJuicePotions = [29];

    private static readonly int[] FairyPotions = [23];

    private static readonly int[] NativePotions = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64];

    private static readonly int[] ReattachSafeMonsters = [23, 24, 25];

    private static readonly (int Id, int Value)[] RegenPotionAmounts = [(51, 5)];

    private static readonly (int Id, decimal Value)[] DirectHealPotionPercents = [(0, 50m), (6, 20m)];

    internal const int RegenPowerId = 162;
    internal const int ReattachPowerId = 159;
    internal const int PotionCount = 65;
    internal const int EnchantmentCount = 256;
    internal const int AfflictionCount = 256;

    internal static bool[] ComponentUnsafeCard() => Expand(UnsafeCards, Registry.CardCount);

    internal static bool[] ComponentUnsafePower() => Expand(UnsafePowers, Registry.PowerCount);

    internal static bool[] ComponentUnsafeRelic() => Expand(UnsafeRelics, Registry.RelicCount);

    internal static bool[] ComponentUnsafeMonster() => Expand(UnsafeMonsters, Registry.MonsterCount);

    internal static bool[] ComponentUnsafePotion() => Expand(UnsafePotions, Registry.PotionCount);

    internal static bool[] SafeEnchantment() => Expand(SafeEnchantments, EnchantmentCount);

    internal static bool[] SafeAffliction() => Expand(SafeAfflictions, AfflictionCount);

    internal static bool[] KnownBlockingCard() => Expand(KnownBlockingCards, Registry.CardCount);

    internal static bool[] KnownBlockingRelic() => Expand(KnownBlockingRelics, Registry.RelicCount);

    internal static bool[] RegenPower() => Expand(RegenPowers, Registry.PowerCount);

    internal static bool[] ReattachPower() => Expand(ReattachPowers, Registry.PowerCount);

    internal static bool[] SwipePower() => Expand(SwipePowers, Registry.PowerCount);

    internal static bool[] RegenPotion() => Expand(RegenPotions, Registry.PotionCount);

    internal static bool[] DirectHealPotion() => Expand(DirectHealPotions, Registry.PotionCount);

    internal static bool[] FruitJuicePotion() => Expand(FruitJuicePotions, Registry.PotionCount);

    internal static bool[] FairyPotion() => Expand(FairyPotions, Registry.PotionCount);

    internal static bool[] NativePotion() => Expand(NativePotions, Registry.PotionCount);

    internal static bool[] ReattachSafeMonster() => Expand(ReattachSafeMonsters, Registry.MonsterCount);

    internal static int[] RegenPotionAmount() => ExpandAmounts(PotionCount);

    internal static bool[] Expand(int[] trueIds, int count) => ExpandCore(trueIds, count);

    internal static int[] ExpandAmounts(int count) => ExpandAmountsCore(RegenPotionAmounts, count);

    internal static decimal[] ExpandPercents() => ExpandPercentsCore(DirectHealPotionPercents, PotionCount);

    private static bool[] ExpandCore(int[] trueIds, int count)
    {
        bool[] result = new bool[count];
        foreach (int id in trueIds)
        {
            if ((uint)id >= (uint)count)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"下界安全表 id {id} 超出容量 {count}");
            result[id] = true;
        }
        return result;
    }

    private static int[] ExpandAmountsCore((int Id, int Value)[] values, int count)
    {
        int[] result = new int[count];
        foreach ((int id, int value) in values)
        {
            if ((uint)id >= (uint)count)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"再生药水 id {id} 超出容量 {count}");
            result[id] = value;
        }
        return result;
    }

    private static decimal[] ExpandPercentsCore((int Id, decimal Value)[] values, int count)
    {
        decimal[] result = new decimal[count];
        foreach ((int id, decimal value) in values)
        {
            if ((uint)id >= (uint)count)
                throw new UnsupportedImportException(ImportReason.Unrepresentable,
                    $"直接治疗药水 id {id} 超出容量 {count}");
            result[id] = value;
        }
        return result;
    }
}
