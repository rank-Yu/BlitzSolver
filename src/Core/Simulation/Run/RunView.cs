using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// Player-visible projection of <see cref="RunState"/>. The run decision layer
/// (<c>BlitzSolver.Ai.Run</c>) may reference this type and its nested element types and nothing else
/// from <c>Simulation.Run</c>. It deliberately contains no RNG state, no pre-generated act room
/// sequences, no grab-bag order, no unlock state and no per-model private saved properties: those
/// are not visible on the native screen and cannot be derived from the history the player has seen.
/// The hidden reward pity counters are included because the player can derive them from that
/// history. Visibility is tabulated in <c>docs/run-state-layout.md</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RunView
{
    public ushort Character;
    /// <summary>Current act id; the native map screen shows the act, not only its index.</summary>
    public ushort Act;
    /// <summary>Current act's boss encounter id.</summary>
    public ushort Boss;
    /// <summary>Current act's second boss encounter id when <see cref="HasSecondBoss"/> is set.</summary>
    public ushort SecondBoss;
    /// <summary>Current act's ancient event id when <see cref="HasAncient"/> is set.</summary>
    public ushort Ancient;
    public byte HasSecondBoss;
    public byte HasAncient;
    private ushort _padding2;
    /// <summary>Native <c>PlayerOddsSet</c> current values (card rarity and potion reward pity).</summary>
    public RunPlayerOdds PlayerOdds;
    /// <summary>Native <c>RunOddsSet.UnknownMapPoint</c> current values (four room-type pity counters).</summary>
    public RunOdds Odds;
    public int Ascension;
    public int ActIndex;
    public int ActFloor;
    public int TotalFloor;
    public RunDecision Decision;
    public byte DecisionOptionCount;
    /// <summary>Current reward-screen stage (None when no reward screen is open).</summary>
    public RunRewardStage RewardStage;
    /// <summary>Remaining rewards shown on the reward list (0 outside a reward list).</summary>
    public byte RewardCount;
    private ushort _padding1;
    /// <summary>Complete request, including every legal option (map points and reward screens).</summary>
    public RunDecisionRequest Request;
    public PlayerView Player;
    public DeckView Deck;
    public RelicViewSet Relics;
    public MapView Map;
    public RoomViewSet Rooms;

    public static RunView From(in RunState state)
    {
        RunView view = default;
        view.Character = state.Player.Character;
        if ((uint)state.ActIndex < state.Acts.Count)
        {
            RunAct act = state.Acts.Items[state.ActIndex];
            view.Act = act.Id;
            view.Boss = act.BossId;
            view.SecondBoss = act.SecondBossId;
            view.HasSecondBoss = act.HasSecondBoss;
            view.Ancient = act.AncientId;
            view.HasAncient = act.HasAncient;
        }
        view.PlayerOdds = state.PlayerOdds;
        view.Odds = state.Odds;
        view.Ascension = state.Ascension;
        view.ActIndex = state.ActIndex;
        view.ActFloor = state.ActFloor;
        view.TotalFloor = state.History.Count;
        view.Request = RunDecisionRequest.Capture(in state);
        view.Decision = view.Request.Kind;
        view.RewardStage = state.Rewards.Stage;
        view.RewardCount = state.Rewards.Count;
        view.DecisionOptionCount = view.Request.OptionCount;
        view.Player.Character = state.Player.Character;
        view.Player.CurrentHp = state.Player.CurrentHp;
        view.Player.MaxHp = state.Player.MaxHp;
        view.Player.MaxEnergy = state.Player.MaxEnergy;
        view.Player.Gold = state.Player.Gold;
        view.Player.BaseOrbSlots = state.Player.BaseOrbSlots;
        view.Player.PotionCount = state.Player.Potions.Count;
        for (int index = 0; index < view.Player.PotionCount; index++)
        {
            RunPotion potion = state.Player.Potions.Items[index];
            view.Player.Potions[index] = new PotionView
            {
                Id = potion.Present == 0 ? (ushort)0 : potion.Id,
                Present = potion.Present,
            };
        }
        view.Deck.Count = state.Deck.Count;
        for (int index = 0; index < view.Deck.Count; index++)
        {
            RunCard card = state.Deck.Cards[index];
            view.Deck.Cards[index] = new CardView
            {
                Id = card.Id,
                Upgrade = card.Upgrade,
                HasEnchantment = card.HasEnchantment,
                EnchantmentId = card.EnchantmentId,
                EnchantmentAmount = card.EnchantmentAmount,
            };
        }
        view.Relics.Count = state.Relics.Count;
        for (int index = 0; index < view.Relics.Count; index++)
        {
            RunRelic relic = state.Relics.Items[index];
            view.Relics.Items[index] = new RelicView
            {
                Id = relic.Id,
                Status = relic.Status,
                Flags = relic.Flags,
                DisplayAmount = relic.DisplayAmount,
            };
        }
        view.Map.Width = state.Map.Width;
        view.Map.Height = state.Map.Height;
        view.Map.NodeCount = state.Map.NodeCount;
        view.Map.Starting = state.Map.Starting;
        view.Map.Boss = state.Map.Boss;
        view.Map.HasSecondBoss = state.Map.HasSecondBoss;
        view.Map.SecondBoss = state.Map.SecondBoss;
        view.Map.StartCount = state.Map.StartCount;
        for (int index = 0; index < view.Map.StartCount; index++)
            view.Map.Starts[index] = state.Map.Starts[index];
        for (int index = 0; index < state.Map.NodeCount; index++)
        {
            RunMapPoint point = state.Map.Nodes[index];
            MapPointView viewPoint = default;
            viewPoint.Coord = point.Coord;
            viewPoint.Type = point.Type;
            viewPoint.ChildCount = point.ChildCount;
            for (int child = 0; child < point.ChildCount; child++)
                viewPoint.Children[child] = point.Children[child];
            viewPoint.Visited = IsVisited(state, point.Coord) ? (byte)1 : (byte)0;
            view.Map.Nodes[index] = viewPoint;
        }
        view.Rooms.Count = state.Rooms.Count;
        for (int index = 0; index < state.Rooms.Count; index++)
        {
            RunRoom room = state.Rooms.Items[index];
            view.Rooms.Items[index] = new RoomView
            {
                RoomType = room.RoomType,
                Model = room.Model,
            };
        }
        return view;
    }

    private static bool IsVisited(in RunState state, RunCoord coord)
    {
        for (int index = 0; index < state.Visited.CoordCount; index++)
        {
            if (state.Visited.Coords[index].Equals(coord))
                return true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PlayerView
    {
        public ushort Character;
        public int CurrentHp;
        public int MaxHp;
        public int MaxEnergy;
        public int Gold;
        public int BaseOrbSlots;
        public byte PotionCount;
        private byte _padding;
        public PotionViewArray Potions;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PotionView
    {
        public ushort Id;
        public byte Present;
        private byte _padding;
    }

    [InlineArray(RunLimits.PotionSlots)]
    public struct PotionViewArray
    {
        private PotionView _first;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CardView
    {
        public ushort Id;
        public byte Upgrade;
        public byte HasEnchantment;
        public ushort EnchantmentId;
        public short EnchantmentAmount;
    }

    [InlineArray(RunLimits.DeckCards)]
    public struct CardViewArray
    {
        private CardView _first;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeckView
    {
        public ushort Count;
        public CardViewArray Cards;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RelicView
    {
        public ushort Id;
        public RunRelicStatus Status;
        public RunRelicFlags Flags;
        public int DisplayAmount;
    }

    [InlineArray(RunLimits.Relics)]
    public struct RelicViewArray
    {
        private RelicView _first;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RelicViewSet
    {
        public byte Count;
        public RelicViewArray Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MapPointView
    {
        public RunCoord Coord;
        public RunMapPointType Type;
        public byte ChildCount;
        public byte Visited;
        public RunMapChildren Children;
    }

    [InlineArray(RunLimits.MapPoints)]
    public struct MapPointViewArray
    {
        private MapPointView _first;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MapView
    {
        public byte Width;
        public byte Height;
        public ushort NodeCount;
        public RunCoord Starting;
        public RunCoord Boss;
        public byte HasSecondBoss;
        public RunCoord SecondBoss;
        public byte StartCount;
        public RunMapStartArray Starts;
        public MapPointViewArray Nodes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RoomView
    {
        public RunRoomType RoomType;
        public RunRef Model;
    }

    [InlineArray(RunLimits.RoomStack)]
    public struct RoomViewArray
    {
        private RoomView _first;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RoomViewSet
    {
        public byte Count;
        public RoomViewArray Items;
    }
}
