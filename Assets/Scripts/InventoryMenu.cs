using System;
using System.Collections.Generic;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Owns the inventory contents and RML drag-and-drop interactions.</summary>
public sealed class InventoryMenu : ScriptBehaviour
{
    [SerializedField] private string documentPath = "UI/inventory.rml";
    [SerializedField] private GameObject? player = null;

    private const int SlotCount = 20;
    private readonly InventoryItem?[] _slots = new InventoryItem?[SlotCount];
    private RmlDocument? _document;
    private RmlWidgetComponent? _widget;
    private int _draggedSlot = -1;
    private readonly HashSet<int> _dragSources = [];
    private PlayerInventory? _inventory;

    public override void OnCreate()
    {
        _slots[0] = new InventoryItem("RIFLE AMMO", "5.56", ItemKind.Ammo, 0);
        _slots[1] = new InventoryItem("MED KIT", "+50 HP", ItemKind.HealthKit, 0);
        _slots[2] = new InventoryItem("FRAG GRENADE", "LETHAL", ItemKind.Static, 3);
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

        // Drop targets need one RML event each so RmlUi performs the hit test.
        // Source subscriptions are added only for occupied slots, keeping the
        // managed event count far below the previous all-events-per-slot setup.
        for (var index = 0; index < SlotCount; index++)
        {
            var destination = index;
            _document.Element(SlotId(index)).On("dragdrop", () => DropOn(destination));
        }

        RenderAll();
    }

    public override void OnDestroy()
    {
        if (_inventory is not null)
            _inventory.Changed -= RenderAll;
        // The RmlWidgetComponent owns this document handle.
        _document = null;
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
        for (var index = 0; index < SlotCount; index++)
            RenderSlot(index);
    }

    private void SyncDynamicItems()
    {
        SyncDynamicItem(ItemKind.Ammo, _inventory?.ReserveAmmo ?? 0,
            new InventoryItem("RIFLE AMMO", "5.56", ItemKind.Ammo, 0), 0);
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
            ? $"<div class=\"item-type\">{item.Type}</div><div class=\"item-name\">{item.Name}</div><div class=\"item-count\">x{Count(item)}</div>"
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

    private enum ItemKind { Static, Ammo, HealthKit, Armour }
    private sealed record InventoryItem(string Name, string Type, ItemKind Kind, int StaticCount);
}
