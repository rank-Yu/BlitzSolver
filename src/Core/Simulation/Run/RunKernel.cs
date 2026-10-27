using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>
/// The run kernel: one action in, one result out, mirroring the combat kernel's "action + choice"
/// shape. R2b-2 implements the map-point action; R2c adds post-combat rewards (the caller
/// generates rewards after <see cref="RunCombatWriteback"/>), the shop shelf, buying, card
/// removal and leaving, and the reward screen exit. A rejected action leaves the state untouched,
/// and a room whose content belongs to a later phase returns
/// <see cref="RunStepStatus.UnsupportedRoom"/> after advancing to the native "room built, not
/// entered" state.
/// </summary>
public sealed class RunKernel
{
    private readonly ContentTable _content;

    public RunKernel(ContentTable content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
    }

    public RunStepResult Step(ref RunState state, in RunAction action)
    {
        RunDecisionRequest request = RunDecisionRequest.Capture(in state);
        if (state.Player.CurrentHp <= 0 || request.Kind == RunDecision.GameOver)
            return new RunStepResult { Status = RunStepStatus.Ended };

        if (state.Shop.Stage != RunShopStage.None)
            return StepShop(ref state, action, request);

        if (action.Kind is RunActionKind.BuyShopEntry or RunActionKind.RemoveCard
            or RunActionKind.LeaveShop or RunActionKind.CancelCardSelection)
        {
            return Reject(request, RunStepReject.NotShopDecision, RunStepReason.NotShopDecision,
                $"当前决策是 {request.Kind}，不是商店。");
        }

        if (state.Rewards.Stage != RunRewardStage.None)
            return StepReward(ref state, action, request);

        if (action.Kind is RunActionKind.TakeReward or RunActionKind.SkipReward
            or RunActionKind.ChooseCard or RunActionKind.ChoosePotion
            or RunActionKind.LeaveRewards or RunActionKind.RerollCardReward)
        {
            return Reject(request, RunStepReject.NotRewardDecision, RunStepReason.NotRewardDecision,
                $"当前决策是 {request.Kind}，不是奖励选择。");
        }
        if (state.Rewards.Generated == 0 && HasPendingCombatRewards(in state))
        {
            return Reject(request, RunStepReject.NotRewardDecision,
                RunRewardReason.RewardNotGenerated,
                "战斗已写回，但奖励尚未生成；先调用 RunRewardGenerator.GenerateForCombat。");
        }
        if (action.Kind != RunActionKind.SelectMapPoint)
            return Reject(request, RunStepReject.WrongActionKind, RunStepReason.WrongActionKind,
                $"R2c 的地图阶段只接受地图选点，收到 {action.Kind}。");
        if (request.Kind != RunDecision.MapPoint)
            return Reject(request, RunStepReject.NotMapDecision, RunStepReason.NotMapDecision,
                $"当前决策是 {request.Kind}，不是地图选点。");
        if (!RunTravel.TryFindPoint(in state, action.Coord, out _))
            return Reject(request, RunStepReject.UnknownMapPoint, RunStepReason.UnknownMapPoint,
                $"地图没有坐标 {action.Coord}。");
        if (!RunTravel.IsTravelable(in state, action.Coord))
            return Reject(request, RunStepReject.NotTravelable, RunStepReason.NotTravelable,
                $"坐标 {action.Coord} 不是当前的合法选点。");
        if (RunTravel.HoldsFreeTravelRelic(in state, _content))
            return Reject(request, RunStepReject.FreeTravelHook, RunStepReason.FreeTravelHook,
                "持有改写自由移动的遗物，R2b-2 未实现自由移动选项。");

        RunState next = state;
        RunMapEntryResult entry;
        try
        {
            entry = RunMapEntry.Enter(in next, _content, action.Coord);
        }
        catch (ContentUnavailableException exception)
        {
            return Reject(request, RejectFor(exception.Reason), exception.Reason,
                exception.Detail);
        }

        if (entry.RoomType == RunRoomType.Shop)
        {
            RunState shop = entry.Run;
            shop.AddRoom(new RunRoom
            {
                RoomType = RunRoomType.Shop,
                Model = RunRef.None,
                ParentEvent = RunRef.None,
                GoldProportion = 1f,
            });
            try
            {
                RunShopGenerator.RequireSupportedListeners(in shop, _content);
                RunShopGenerator.BeforeRoomEntered(ref shop, _content);
                RunShopGenerator.Generate(ref shop, _content);
                RunShopGenerator.AfterRoomEntered(ref shop, _content);
                RunShopGenerator.RefreshPrices(ref shop, _content);
                shop.Canonicalize();
            }
            catch (ContentUnavailableException exception)
            {
                return Reject(request, RunStepReject.ShopContent, exception.Reason,
                    exception.Detail);
            }
            catch (CapacityException exception)
            {
                return Reject(request, RunStepReject.ShopContent, ContentReason.Capacity,
                    exception.Message);
            }
            state = shop;
            return new RunStepResult
            {
                Status = RunStepStatus.Decision,
                Request = RunDecisionRequest.Capture(in state),
                RoomType = RunRoomType.Shop,
                PointType = entry.PointType,
            };
        }

        state = entry.Run;
        if (entry.Combat != null)
        {
            return new RunStepResult
            {
                Status = RunStepStatus.Combat,
                Combat = entry.Combat,
                RoomType = entry.RoomType,
                PointType = entry.PointType,
            };
        }
        string reason = entry.RoomType switch
        {
            RunRoomType.Shop => RunStepReason.ShopContent,
            RunRoomType.Treasure => RunStepReason.TreasureContent,
            RunRoomType.RestSite => RunStepReason.RestSiteContent,
            RunRoomType.Event => entry.PointType == RunMapPointType.Ancient
                ? RunStepReason.AncientContent
                : RunStepReason.EventContent,
            _ => RunStepReason.CombatUnsupported,
        };
        return new RunStepResult
        {
            Status = RunStepStatus.UnsupportedRoom,
            Reason = reason,
            Detail = $"{entry.RoomType} 的内容生成属于后续阶段。",
            RoomType = entry.RoomType,
            PointType = entry.PointType,
        };
    }

    private RunStepResult StepShop(ref RunState state, in RunAction action,
        in RunDecisionRequest request)
    {
        RunState next = state;
        try
        {
            switch (action.Kind)
            {
                case RunActionKind.BuyShopEntry:
                {
                    if (request.Kind != RunDecision.Shop)
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.NotShopDecision, "当前不是商店界面。");
                    }
                    if ((uint)action.Option >= request.OptionCount)
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.ShopOption, $"商店选项 {action.Option} 非法。");
                    }
                    RunDecisionOption option = request.Options[action.Option];
                    if (option.Kind is not (RunDecisionOptionKind.ShopEntry
                        or RunDecisionOptionKind.CardRemoval))
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.ShopOption, $"选项 {action.Option} 不是商店条目。");
                    }
                    RunShopGenerator.BuyShopEntry(ref next, _content, option);
                    break;
                }
                case RunActionKind.RemoveCard:
                case RunActionKind.CancelCardSelection:
                {
                    if (request.Kind != RunDecision.CardSelect)
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.NotShopDecision, "当前不是删牌选卡。");
                    }
                    if (action.Kind == RunActionKind.CancelCardSelection)
                    {
                        RunShopGenerator.CancelCardRemoval(ref next);
                        break;
                    }
                    if ((uint)action.Option >= request.OptionCount)
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.ShopOption, $"选卡选项 {action.Option} 非法。");
                    }
                    RunDecisionOption option = request.Options[action.Option];
                    if (option.Kind == RunDecisionOptionKind.Skip)
                        RunShopGenerator.CancelCardRemoval(ref next);
                    else if (option.Kind == RunDecisionOptionKind.Card)
                        RunShopGenerator.RemoveCard(ref next, _content, option.Index);
                    else
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.ShopOption, $"选项 {action.Option} 不是卡牌。");
                    }
                    break;
                }
                case RunActionKind.LeaveShop:
                    if (request.Kind != RunDecision.Shop)
                    {
                        return Reject(request, RunStepReject.NotShopDecision,
                            RunStepReason.NotShopDecision, "当前不是商店界面。");
                    }
                    RunShopGenerator.Leave(ref next);
                    break;
                default:
                    return Reject(request, RunStepReject.WrongActionKind,
                        RunStepReason.WrongActionKind,
                        $"商店阶段不接受动作 {action.Kind}。");
            }
        }
        catch (ContentUnavailableException exception)
        {
            return Reject(request, RunStepReject.ShopContent, exception.Reason,
                exception.Detail);
        }
        catch (CapacityException exception)
        {
            return Reject(request, RunStepReject.ShopContent, ContentReason.Capacity,
                exception.Message);
        }

        state = next;
        RunDecisionRequest follow = RunDecisionRequest.Capture(in state);
        return new RunStepResult
        {
            Status = RunStepStatus.Decision,
            Request = follow,
            RoomType = state.Rooms.Count == 0
                ? RunRoomType.Unassigned
                : state.Rooms.Items[state.Rooms.Count - 1].RoomType,
        };
    }

    private RunStepResult StepReward(ref RunState state, in RunAction action,
        in RunDecisionRequest request)
    {
        RunState next = state;
        try
        {
            switch (action.Kind)
            {
                case RunActionKind.TakeReward:
                {
                    if (request.Kind != RunDecision.CombatReward
                        || (uint)action.Option >= request.OptionCount)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.RewardIndex,
                            $"奖励动作下标 {action.Option} 非法。");
                    }
                    RunDecisionOption option = request.Options[action.Option];
                    if (option.Kind != RunDecisionOptionKind.Reward)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.RewardIndex,
                            $"动作 {action.Option} 不是奖励选项。");
                    }
                    RunRewardGenerator.TakeReward(ref next, _content, option);
                    break;
                }
                case RunActionKind.ChooseCard:
                {
                    if (request.Kind != RunDecision.CardReward
                        || (uint)action.Option >= request.OptionCount)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.CardOption,
                            $"卡牌选项 {action.Option} 非法。");
                    }
                    RunDecisionOption option = request.Options[action.Option];
                    if (option.Kind == RunDecisionOptionKind.Reroll)
                    {
                        RunRewardGenerator.RerollCardReward(ref next, _content);
                        break;
                    }
                    if (option.Kind != RunDecisionOptionKind.Card)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.CardOption,
                            $"动作 {action.Option} 不是卡牌选项。");
                    }
                    RunRewardGenerator.ChooseCard(ref next, _content, option.Index, reroll: false);
                    break;
                }
                case RunActionKind.RerollCardReward:
                {
                    if (request.Kind != RunDecision.CardReward)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.CardRewardAlternative,
                            "当前不是卡牌奖励。");
                    }
                    RunRewardGenerator.RerollCardReward(ref next, _content);
                    break;
                }
                case RunActionKind.ChoosePotion:
                {
                    if (request.Kind != RunDecision.PotionDiscard
                        || (uint)action.Option >= request.OptionCount)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.PotionSlot,
                            $"药水槽选项 {action.Option} 非法。");
                    }
                    RunDecisionOption option = request.Options[action.Option];
                    if (option.Kind != RunDecisionOptionKind.Potion)
                    {
                        return Reject(request, RunStepReject.NotRewardDecision,
                            RunRewardReason.PotionSlot,
                            $"动作 {action.Option} 不是药水槽。");
                    }
                    RunRewardGenerator.ChoosePotion(ref next, _content, option.Index);
                    break;
                }
                case RunActionKind.SkipReward:
                    RunRewardGenerator.SkipCurrentReward(ref next);
                    break;
                case RunActionKind.LeaveRewards:
                    RunRewardGenerator.Leave(ref next);
                    break;
                default:
                    return Reject(request, RunStepReject.WrongActionKind,
                        RunStepReason.WrongActionKind,
                        $"奖励阶段不接受动作 {action.Kind}。");
            }
        }
        catch (ContentUnavailableException exception)
        {
            return Reject(request, RunStepReject.NotRewardDecision, exception.Reason,
                exception.Detail);
        }
        catch (CapacityException exception)
        {
            return Reject(request, RunStepReject.NotRewardDecision, ContentReason.Capacity,
                exception.Message);
        }

        state = next;
        RunDecisionRequest follow = RunDecisionRequest.Capture(in state);
        return new RunStepResult
        {
            Status = RunStepStatus.Decision,
            Request = follow,
            RoomType = state.Rooms.Count == 0
                ? RunRoomType.Unassigned
                : state.Rooms.Items[state.Rooms.Count - 1].RoomType,
        };
    }

    private static bool HasPendingCombatRewards(in RunState state)
    {
        if (state.Rooms.Count == 0)
            return false;
        RunRoom room = state.Rooms.Items[state.Rooms.Count - 1];
        return room.IsPreFinished != 0
            && room.RoomType is RunRoomType.Monster or RunRoomType.Elite or RunRoomType.Boss;
    }

    private static RunStepResult Reject(in RunDecisionRequest request, RunStepReject reject,
        string reason, string detail) => new()
        {
            Status = RunStepStatus.Rejected,
            Reject = reject,
            Reason = reason,
            Detail = detail,
            Request = request,
        };

    private static RunStepReject RejectFor(string reason) => reason switch
    {
        RunStepReason.RoomStackActive => RunStepReject.RoomStackActive,
        RunStepReason.UnknownPointHook => RunStepReject.UnknownMapPointHook,
        RunStepReason.EventHook => RunStepReject.EventHook,
        RunStepReason.EventAllowedCheck => RunStepReject.EventAllowedCheck,
        RunStepReason.CorruptMap => RunStepReject.CorruptMap,
        _ => RunStepReject.CombatUnsupported,
    };
}
