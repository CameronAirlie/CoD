using PlutoGE.ScriptCore.Networking;

namespace CoD.Scripts;

public sealed partial class MultiplayerSession
{
    // 25 is the existing client heartbeat channel.
    private const ushort BuyChannel = 27, EconomyChannel = 28;
    private readonly Dictionary<int, EconomyAccount> _accounts = new();
    private readonly EconomyAccount _offlineAccount = new();
    private int _money = EconomyRules.StartingMoney, _ownedWeapons = 1 << 1;
    private float _buyEndsAt;
    public int Money => IsNeutralSpectator ? 0 : GrenadesOffline ? _offlineAccount.Money : _money;
    public string ShopStatus { get; private set; } = "Choose an item to buy.";
    public event Action? EconomyChanged;
    public static void RewardOfflineKill()
    {
        if (_activeSession is not { GrenadesOffline: true } session) return;
        session._offlineAccount.Credit(EconomyRules.KillReward); session.SendEconomy(0);
    }
    public bool OwnsWeapon(int slot) => GrenadesOffline ? _offlineAccount.Owns(slot) :
        slot is >= 0 and < WeaponCatalog.SlotCount && (_ownedWeapons & (1 << slot)) != 0;
    public bool CanBuy => !IsNeutralSpectator && (GrenadesOffline ? _playerHealth?.IsDead != true :
        _localPeerId >= 0 && _playerHealth?.IsDead != true &&
        (CurrentMatch?.Phase == MatchPhase.Warmup || CurrentMatch?.Phase == MatchPhase.Playing &&
            CurrentMatch.SecondsRemaining >= matchDuration - EconomyRules.BuySeconds));

    public float BuySecondsRemaining => !CanBuy ? 0 : GrenadesOffline ? float.PositiveInfinity :
        CurrentMatch?.Phase == MatchPhase.Warmup ? MathF.Max(0, CurrentMatch.SecondsRemaining) :
        MathF.Max(0, (CurrentMatch?.SecondsRemaining ?? 0) - matchDuration + EconomyRules.BuySeconds);
    public string? PurchaseUnavailableReason(ShopItem item) => EconomyRules.UnavailableReason(item,
        CanBuy && !IsUsingObjective, Money, item.WeaponSlot >= 0 && OwnsWeapon(item.WeaponSlot),
        item.Throwable is { } kind ? ThrowableCount(kind) : 0, GrenadeRules.Types.Sum(ThrowableCount));

    private EconomyAccount Account(int peer)
    {
        if (GrenadesOffline && peer == 0) return _offlineAccount;
        if (!_accounts.TryGetValue(peer, out var account)) _accounts.Add(peer, account = new());
        return account;
    }
    public void Buy(string id)
    {
        if (EconomyRules.Find(id) is not { } item) return;
        if (PurchaseUnavailableReason(item) is { } reason)
        { ShopStatus = reason; EconomyChanged?.Invoke(); return; }
        if (GrenadesOffline || _server is not null) AcceptPurchase(0, id);
        else if (_client?.IsConnected == true) _client.SendJson(BuyChannel, new PurchaseRequest(id));
    }
    private void AcceptPurchase(int peer, string? id)
    {
        if (_playerStates.TryGetValue(peer, out var observer) && !TeamIdentity.IsCombatTeam(observer.Team)) return;
        var localOffline = GrenadesOffline && peer == 0;
        var allowed = localOffline ? _playerHealth?.IsDead != true :
            _playerStates.TryGetValue(peer, out var state) && state.Health > 0 &&
            !(_defusal?.Snapshot().Operator == peer) &&
            (GrenadesOffline || _matchPhase == MatchPhase.Warmup || _matchPhase == MatchPhase.Playing && _time <= _buyEndsAt);
        if (!allowed) { SendEconomy(peer, "Buy time ended, or player cannot buy."); return; }
        var supply = localOffline ? _offlineGrenades : _playerStates[peer].Grenades;
        var account = Account(peer);
        account.TryBuy(id, supply, out var result);
        SendEconomy(peer, result); SendGrenadeSupply(peer, supply.Remaining);
    }
    private void SendEconomy(int peer, string message = "Choose an item to buy.")
    {
        if (_playerStates.TryGetValue(peer, out var observer) && !TeamIdentity.IsCombatTeam(observer.Team)) return;
        var account = Account(peer);
        var state = new EconomyState(account.Money, account.WeaponMask, message);
        if (peer == 0) ApplyEconomy(state);
        else if (!_bots.ContainsKey(peer)) _server?.SendJson(peer, EconomyChannel, state);
    }
    private void ApplyEconomy(EconomyState state)
    {
        var newlyOwned = state.WeaponMask & ~_ownedWeapons;
        _money = Math.Clamp(state.Money, 0, EconomyRules.MaximumMoney);
        _ownedWeapons = state.WeaponMask & 7;
        ShopStatus = state.Message;
        EconomyChanged?.Invoke();
        if (_playerController is { } controller && !OwnsWeapon(controller.EquippedWeaponSlot))
            controller.SelectWeaponSlot(1);
        if (_playerController is { } equipped)
            for (var slot = 0; slot < WeaponCatalog.SlotCount; slot++)
                if ((newlyOwned & (1 << slot)) != 0) equipped.SelectWeaponSlot(slot);
    }
    private bool HandleEconomyClientMessage(NetworkMessage message)
    {
        if (message.Channel != EconomyChannel) return false;
        if (message.GetJson<EconomyState>() is { } state) ApplyEconomy(state);
        return true;
    }
    private void RewardRound(PlayerTeam? winner)
    {
        foreach (var pair in _playerStates)
        {
            if (!TeamIdentity.IsCombatTeam(pair.Value.Team)) continue;
            Account(pair.Key).Credit(winner is null || pair.Value.Team == winner ? EconomyRules.WinReward : EconomyRules.LossReward);
            SendEconomy(pair.Key);
        }
    }
    private sealed record PurchaseRequest(string Id);
    private sealed record EconomyState(int Money, int WeaponMask, string Message);
}
