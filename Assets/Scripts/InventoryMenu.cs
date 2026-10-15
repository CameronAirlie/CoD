using System;
using System.Collections.Generic;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Owns the inventory contents and RML drag-and-drop interactions.</summary>
public sealed class InventoryMenu : ScriptBehaviour
{
    [SerializedField] private string documentPath = "UI/inventory.rml";
    [SerializedField] private GameObject? player = null;

    private const int SlotCount = 8;
    private readonly InventoryItem?[] _slots = new InventoryItem?[SlotCount];
    private RmlDocument? _document;
    private RmlWidgetComponent? _widget;
    private int _draggedSlot = -1;
    private readonly HashSet<int> _dragSources = [];
    private PlayerInventory? _inventory;
    private PlayerController? _controller;
    private MultiplayerSession? _session;
    private string _shopPresentation = "";
    private string _throwablePresentation = "";
    private ControllerMenu? _menu;
    private bool _wasVisible;

    public override void OnCreate()
    {
        _slots[0] = new InventoryItem("AMMUNITION", "SHARED RESERVE", ItemKind.Ammo, 0);
        _slots[1] = new InventoryItem("MED KIT", "+50 HP", ItemKind.HealthKit, 0);
        // Only show supplies backed by gameplay quantities.
        _slots[5] = new InventoryItem("ARMOUR PLATE", "DEFENCE", ItemKind.Armour, 0);
        _inventory = player?.GetComponent<PlayerInventory>();
        if (_inventory is null)
            Debug.LogError("InventoryMenu: player must reference a PlayerInventory component.");
        else
            _inventory.Changed += RenderAll;

        _widget = GameObject.GetComponent<RmlWidgetComponent>();
        if (_widget is null)
        {
            Debug.LogError("InventoryMenu requires an RmlWidgetComponent.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_widget.Source))
            _widget.Source = documentPath;
        _document = _widget.Document;
        _menu = new(_document) { Back = Back };
        _controller = player?.GetComponent<PlayerController>();
        _session = player?.GetComponent<MultiplayerSession>();
        var purchaseIndex = 0;
        foreach (var item in EconomyRules.Catalog)
        {
            var id = item.Id;
            var index = purchaseIndex++;
            _menu.Add(new($"buy-{id}", index / 4, index % 4, () => _session?.Buy(id),
                () => _session is { UsesPresetLoadouts: false } && _session.PurchaseUnavailableReason(item) is null));
        }
        for (var index = 0; index < GrenadeRules.Types.Count; index++)
        {
            var kind = GrenadeRules.Types[index];
            _menu.Add(new($"select-{kind}", 3, index, () => _session?.SelectThrowable(kind),
                () => _session?.ThrowableCount(kind) > 0));
        }
        var loadoutIndex = 0;
        foreach (var preset in HardpointLoadouts.Catalog)
        {
            var choice = preset.Id;
            _menu.Add(new($"loadout-{choice}", 0, loadoutIndex++, () => _session?.SelectLoadout(choice),
                () => _session is { UsesPresetLoadouts: true, IsNeutralSpectator: false }));
        }
        if (_controller is not null) _controller.WeaponChanged += RenderWeapons;
        for (var index = 0; index < WeaponCatalog.SlotCount; index++)
        {
            var slot = index;
            _menu.Add(new($"weapon-{slot}", 4, slot, () => _controller?.SelectWeaponSlot(slot),
                () => _session?.OwnsWeapon(slot) == true));
        }
        RenderWeapons();

        // Drop targets need one RML event each so RmlUi performs the hit test.
        // Source subscriptions are added only for occupied slots, keeping the
        // managed event count far below the previous all-events-per-slot setup.
        for (var index = 0; index < SlotCount; index++)
        {
            var destination = index;
            _document.Element(SlotId(index)).On("dragdrop", () => DropOn(destination));
            _menu.Add(new(SlotId(index), 5 + index / 4, index % 4,
                () => { if (_draggedSlot >= 0) PlaceSlot(destination); else UseSlot(destination); },
                () => _draggedSlot >= 0 || _slots[destination] is not null,
                Secondary: () => BeginDrag(destination)), false);
        }

        RenderAll();
    }

    public override void OnDestroy()
    {
        if (_controller is not null) _controller.WeaponChanged -= RenderWeapons;
        if (_inventory is not null)
            _inventory.Changed -= RenderAll;
        // The RmlWidgetComponent owns this document handle.
        _document = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        var visible = _widget?.Visible == true && !Input.CursorLocked;
        if (visible && !_wasVisible) _menu?.Open();
        if (!visible && _wasVisible) { CancelDrag(); _menu?.Close(); }
        _wasVisible = visible;
        if (visible) _menu?.Update(GameInput.ReadMenuFrame());
        if (_document is null || _session is null) return;
        // Include quantities and ownership, not only the wallet: throws and equipment loss
        // must invalidate purchase availability even when no transaction occurred.
        var presets = _session.UsesPresetLoadouts;
        var presentation = $"{_session.IsNeutralSpectator}/{presets}/{_session.ActiveLoadout}/{_session.SelectedLoadout}/{_session.Money}/{_session.CanBuy}/{_session.IsUsingObjective}/{MathF.Ceiling(_session.BuySecondsRemaining)}/{_session.ShopStatus}/" +
            string.Join(",", EconomyRules.Catalog.Select(item => $"{_session.OwnsWeapon(item.WeaponSlot)}:{(item.Throwable is { } kind ? _session.ThrowableCount(kind) : 0)}"));
        if (presentation != _shopPresentation && _document.Element("shop-balance").SetClass("ready", true))
        {
            _shopPresentation = presentation;
            _document.Element("inventory-panel").SetClass("preset-mode", presets);
            _document.Element("inventory-mode").Markup = presets ? "HARDPOINT / NEXT SPAWN" : "BUY / INVENTORY";
            _document.Element("loadout-selector").SetStyle("display", presets ? "block" : "none");
            _document.Element("buy-equipment").SetStyle("display", presets ? "none" : "block");
            if (presets)
            {
                _document.Element("loadout-state").Markup = $"CURRENT: {_session.ActiveLoadout.ToString().ToUpperInvariant()} / NEXT SPAWN: {_session.SelectedLoadout.ToString().ToUpperInvariant()}";
                foreach (var preset in HardpointLoadouts.Catalog)
                {
                    var card = _document.Element($"loadout-{preset.Id}");
                    card.SetClass("selected", _session.SelectedLoadout == preset.Id);
                    card.SetClass("current", _session.ActiveLoadout == preset.Id);
                    card["aria-disabled"] = _session.IsNeutralSpectator ? "true" : "false";
                    _document.Element($"loadout-action-{preset.Id}").Markup = _session.SelectedLoadout == preset.Id ? "SELECTED FOR NEXT SPAWN" : "SELECT FOR NEXT SPAWN";
                    card.SetStyle("pointer-events", _session.IsNeutralSpectator ? "none" : "auto");
                }
            }

            _document.Element("shop-balance").Markup = $"${_session.Money:N0}";
            _document.Element("shop-window").Markup = !_session.CanBuy ? "BUY CLOSED" : float.IsPositiveInfinity(_session.BuySecondsRemaining)
                ? "BUY OPEN" : $"BUY OPEN / {MathF.Ceiling(_session.BuySecondsRemaining):0}s LEFT";
            _document.Element("shop-status").Markup = System.Net.WebUtility.HtmlEncode(_session.ShopStatus);
            _document.Element("throwable-capacity").Markup = $"{GrenadeRules.Types.Sum(_session.ThrowableCount)}/4 CARRIED";
            foreach (var item in EconomyRules.Catalog)
            {
                var reason = _session.PurchaseUnavailableReason(item);
                var quantity = item.Throwable is { } kind ? $"CARRIED {_session.ThrowableCount(kind)} / {(kind == "flash" ? 2 : 1)}" :
                    _session.OwnsWeapon(item.WeaponSlot) ? "OWNED" : item.WeaponSlot == 1 ? "SIDEARM" : "REPLACES PRIMARY";
                var button = _document.Element($"buy-{item.Id}");
                button.Markup = $"<div class=\"shop-name\">{item.Name}</div><div class=\"shop-price\">${item.Price:N0}</div><div class=\"shop-detail\">{quantity}</div><div class=\"shop-action\">{reason ?? $"BUY / ${_session.Money - item.Price:N0} LEFT"}</div>";
                button.SetClass("unavailable", reason is not null);
                button.SetClass("available", reason is null);
                button["aria-disabled"] = reason is not null ? "true" : "false";
                button.SetStyle("pointer-events", reason is not null ? "none" : "auto");
            }
            RenderWeapons();
        }
        var throwables = _session.SelectedThrowable + string.Join(",", GrenadeRules.Types.Select(_session.ThrowableCount));
        if (_throwablePresentation == throwables || !_document.Element("throwable-grid").SetClass("ready", true)) return;
        _throwablePresentation = throwables;
        foreach (var kind in GrenadeRules.Types)
            _document.Element($"select-{kind}").Markup = $"{(_session.SelectedThrowable == kind ? "> " : "")}{GrenadeRules.Name(kind)} / {_session.ThrowableCount(kind)}";
    }

    private void Back()
    {
        if (_draggedSlot >= 0) { CancelDrag(); return; }
        GameObject.FindWithTag("UIManager")?.GetComponent<UIManager>()?.SetInventoryOpen(false);
    }
    private void CancelDrag()
    {
        if (_draggedSlot >= 0) _document?.Element(SlotId(_draggedSlot)).SetClass("dragging", false);
        _draggedSlot = -1;
    }
    private void PlaceSlot(int destination)
    {
        if (_draggedSlot == destination) CancelDrag();
        else DropOn(destination);
    }

    private void BeginDrag(int source)
    {
        if (_slots[source] is null)
            return;

        if (_draggedSlot >= 0 && _draggedSlot != source)
            _document?.Element(SlotId(_draggedSlot)).SetClass("dragging", false);
        _draggedSlot = source;
        _document?.Element(SlotId(source)).SetClass("dragging");
    }

    private void DropOn(int destination)
    {
        if (_draggedSlot < 0 || _draggedSlot == destination)
            return;

        // Swap occupied slots, or move into an empty one to make space.
        (_slots[_draggedSlot], _slots[destination]) =
            (_slots[destination], _slots[_draggedSlot]);
        RenderSlot(_draggedSlot);
        RenderSlot(destination);
        _draggedSlot = -1;
    }

    private void RenderAll()
    {
        SyncDynamicItems();
        if (_document is not null) _document.Element("inventory-capacity").Markup = $"{_inventory?.SlotsUsed ?? 0}/{InventoryRules.Capacity} CAPACITY";
        for (var index = 0; index < SlotCount; index++)
            RenderSlot(index);
    }

    private void RenderWeapons()
    {
        if (_document is null) return;
        for (var index = 0; index < WeaponCatalog.SlotCount; index++)
        {
            var weapon = WeaponCatalog.At(index);
            var owned = _session?.OwnsWeapon(index) == true;
            _document.Element($"weapon-{index}")["aria-disabled"] = owned ? "false" : "true";
            _document.Element($"weapon-{index}").SetStyle("pointer-events", owned ? "auto" : "none");
            _document.Element($"weapon-{index}").SetClass("unavailable", !owned);
            _document.Element($"weapon-{index}").SetClass("selected", _controller?.EquippedWeaponSlot == index);
            _document.Element($"weapon-stats-{index}").Markup = $"{weapon.Damage:0} DMG / {weapon.Range:0}m / {weapon.RoundsPerMinute:0} RPM / {weapon.MagazineSize} RND";
        }
    }

    private void SyncDynamicItems()
    {
        SyncDynamicItem(ItemKind.Ammo, _inventory?.ReserveAmmo ?? 0,
            new InventoryItem("AMMUNITION", "SHARED RESERVE", ItemKind.Ammo, 0), 0);
        SyncDynamicItem(ItemKind.HealthKit, _inventory?.HealthKits ?? 0,
            new InventoryItem("MED KIT", "+30% HP", ItemKind.HealthKit, 0), 1);
        SyncDynamicItem(ItemKind.Armour, _inventory?.ArmourPlates ?? 0,
            new InventoryItem("ARMOUR PLATE", "DEFENCE", ItemKind.Armour, 0), 5);
    }

    private void SyncDynamicItem(ItemKind kind, int count, InventoryItem item, int preferredSlot)
    {
        var existing = Array.FindIndex(_slots, value => value?.Kind == kind);
        if (count <= 0)
        {
            if (existing >= 0)
                _slots[existing] = null;
            return;
        }
        if (existing >= 0)
            return;
        var destination = _slots[preferredSlot] is null
            ? preferredSlot
            : Array.FindIndex(_slots, value => value is null);
        if (destination >= 0)
            _slots[destination] = item;
    }

    private void RenderSlot(int index)
    {
        var element = _document?.Element(SlotId(index));
        if (element is null)
            return;

        element.SetClass("dragging", false);
        element.SetClass("occupied", _slots[index] is not null);
        // RmlUi uses the RCSS `drag` property rather than HTML's draggable
        // attribute. `clone` produces a cursor-following copy and emits the
        // dragdrop event on the destination slot.
        element.SetStyle("drag", _slots[index] is null ? "none" : "clone");
        if (_slots[index] is not null && _dragSources.Add(index))
        {
            var source = index;
            element.On("dragstart", () => BeginDrag(source));
            element.On("dblclick", () => UseSlot(source));
        }
        element.Markup = _slots[index] is { } item
            ? $"<img class=\"item-icon\" src=\"UI/Icons/{ItemIcon(item.Kind)}.tga\"/><div class=\"item-type\">{item.Type}</div><div class=\"item-name\">{item.Name}</div><div class=\"item-count\">x{Count(item)}</div>"
            : "<div class=\"empty-label\">EMPTY</div>";
    }

    private int Count(InventoryItem item) => item.Kind switch
    {
        ItemKind.Ammo => _inventory?.ReserveAmmo ?? 0,
        ItemKind.HealthKit => _inventory?.HealthKits ?? 0,
        ItemKind.Armour => _inventory?.ArmourPlates ?? 0,
        _ => item.StaticCount,
    };

    private void UseSlot(int index)
    {
        if (_slots[index]?.Kind == ItemKind.HealthKit)
            _inventory?.BeginUseHealthKit();
        else if (_slots[index]?.Kind == ItemKind.Armour)
            _inventory?.BeginUseArmourPlate();
    }

    private static string SlotId(int index) => $"slot-{index}";
    private static string ItemIcon(ItemKind kind) => kind switch
    { ItemKind.HealthKit => "health", ItemKind.Armour => "armour", _ => "ammo" };

    private enum ItemKind { Static, Ammo, HealthKit, Armour }
    private sealed record InventoryItem(string Name, string Type, ItemKind Kind, int StaticCount);
}
