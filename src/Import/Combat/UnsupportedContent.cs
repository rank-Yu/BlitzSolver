// Adapted from CombatSolver src/Flash/Bridge/FlashUnsupportedContent.cs and UnsupportedCategories.cs
// at commit a62f1850 (MIT, Copyright (c) 2026 Torch1230 and contributors).
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Sts2Solver.Import;

/// <summary>Stable categories for content deliberately outside the single-player vanilla scope.</summary>
internal static class UnsupportedContent
{
    private static readonly string[] MultiplayerOnlyPowers =
    [
        "COORDINATE_POWER", "FADE_POWER", "HAMMER_TIME_POWER",
        "LEADERSHIP_POWER", "ONE_FOR_ALL_POWER", "TANK_POWER",
    ];

    public static UnsupportedImportException Card(CardModel card)
    {
        string id = card.Id.Entry;
        if (card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly)
            return new UnsupportedImportException(ImportReason.MultiPlayer, $"{id} 是多人专用内容");
        if (card.Pool is MockCardPool)
            return new UnsupportedImportException(ImportReason.TestContent, $"{id} 是测试/Mock 内容");
        if (card.Pool is DeprecatedCardPool)
            return new UnsupportedImportException(ImportReason.DeprecatedContent, $"{id} 是已废弃内容");
        return new UnsupportedImportException(ImportReason.UnsupportedCard, id);
    }

    public static UnsupportedImportException Power(PowerModel power)
    {
        string id = power.Id.Entry;
        if (Array.IndexOf(MultiplayerOnlyPowers, id) >= 0)
            return new UnsupportedImportException(ImportReason.MultiPlayer, $"{id} 是多人专用 Power");
        if (id.StartsWith("MOCK_", StringComparison.Ordinal))
            return new UnsupportedImportException(ImportReason.TestContent, $"{id} 是测试/Mock Power");
        return new UnsupportedImportException(ImportReason.UnsupportedPower, id);
    }

    public static UnsupportedImportException Relic(RelicModel relic)
        => new(ImportReason.UnsupportedRelic, relic.Id.Entry);

    public static UnsupportedImportException Potion(PotionModel potion)
        => new(ImportReason.UnsupportedPotion, potion.Id.Entry);

    public static UnsupportedImportException Orb(OrbModel orb)
        => new(ImportReason.UnsupportedOrb, orb.Id.Entry);

    public static UnsupportedImportException Monster(MonsterModel monster)
        => new(ImportReason.UnsupportedMonster, monster.Id.Entry);
}
