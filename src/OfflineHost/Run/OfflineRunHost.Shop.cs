// Native command order follows sts2 v0.111.0 MerchantEntry/NMerchantInventory.
// Binding adapted from NativeDecisionTestSession.RewardsShop.cs at f410988 (BlitzSolver, MIT).
using BlitzSolver.Import;
using BlitzSolver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace BlitzSolver.OfflineHost.Run;

public sealed partial class OfflineRunHost
{
    private RunExecutor.Pending ShopPending(OfflineRunBoundary boundary, RunDecisionToken token)
    {
        var room = boundary.Shop ?? throw new InvalidDataException("host-shop-required");
        var active = boundary.ShopActive ?? throw new InvalidDataException("host-shop-readiness-required");
        var close = boundary.CloseShop ?? throw new InvalidDataException("host-shop-close-required");
        return RunExecutor.Bind(RunDecisionImporter.Shop(room, _player, active, active, token), async (selected, _) =>
        {
            if (selected[0] is MerchantInventory) { close(); return; }
            if (selected[0] is MerchantCardRemovalEntry removal)
            {
                _shopRemoval = true;
                try
                {
                    if (await removal.OnTryPurchaseWrapper(room.GetLocalInventory(), cancelable: true))
                        removal.SetUsed(); // NMerchantInventory.OnCardRemovalUsed's model responsibility.
                }
                finally { _shopRemoval = false; }
            }
            else await ((MerchantEntry)selected[0]).OnTryPurchaseWrapper(room.GetLocalInventory());
        });
    }
}
