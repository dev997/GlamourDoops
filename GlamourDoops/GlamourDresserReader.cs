using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace SamplePlugin;

// Reads the contents of the glamour dresser while it is open, prints them to /xllog,
// and works out which items share the same base model.
public sealed unsafe class GlamourDresserReader : IDisposable
{
    // Internal name of the game's glamour dresser window.
    public const string DresserAddonName = "MiragePrismPrismBox";

    // The game stores HQ items as their item id plus this offset.
    private const uint HqItemIdOffset = 1_000_000;

    // Items that share a base model: same equip slot, same main and off-hand model, ignoring the variant (colour/texture version).
    private readonly record struct ModelKey(uint EquipSlotCategory, ulong BaseModelMain, ulong BaseModelSub);

    private bool dresserOpen;
    private uint[] lastItemIds = [];

    // Item ids (without the HQ offset) of every dresser item that shares its model with another dresser item.
    public IReadOnlySet<uint> DuplicateItemIds { get; private set; } = new HashSet<uint>();

    public GlamourDresserReader()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, DresserAddonName, OnDresserOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, DresserAddonName, OnDresserClosed);
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, DresserAddonName, OnDresserOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, DresserAddonName, OnDresserClosed);
    }

    public static uint StripHq(uint itemId) => itemId >= HqItemIdOffset ? itemId - HqItemIdOffset : itemId;

    private void OnDresserOpened(AddonEvent type, AddonArgs args)
    {
        dresserOpen = true;
        lastItemIds = [];
    }

    private void OnDresserClosed(AddonEvent type, AddonArgs args) => dresserOpen = false;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!dresserOpen)
            return;

        // The dresser contents arrive from the server shortly after the window opens,
        // and change whenever items are added or removed, so re-read whenever they differ.
        var mirageManager = MirageManager.Instance();
        if (mirageManager == null || !mirageManager->PrismBoxLoaded)
            return;

        var itemIds = mirageManager->PrismBoxItemIds;
        if (itemIds.SequenceEqual(lastItemIds))
            return;

        lastItemIds = itemIds.ToArray();
        ProcessDresserContents(mirageManager);
    }

    private void ProcessDresserContents(MirageManager* mirageManager)
    {
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        var stainSheet = Plugin.DataManager.GetExcelSheet<Stain>();

        var itemIds = mirageManager->PrismBoxItemIds;
        var stain0Ids = mirageManager->PrismBoxStain0Ids;
        var stain1Ids = mirageManager->PrismBoxStain1Ids;

        var modelGroups = new Dictionary<ModelKey, List<(int Slot, Item Item)>>();

        var count = 0;
        for (var slot = 0; slot < itemIds.Length; slot++)
        {
            var rawId = itemIds[slot];
            if (rawId == 0)
                continue;

            var isHq = rawId >= HqItemIdOffset;
            var itemId = StripHq(rawId);
            if (!itemSheet.TryGetRow(itemId, out var item))
            {
                Plugin.Log.Information($"[Dresser slot {slot}] <unknown item> | Item ID {itemId}");
                continue;
            }

            Plugin.Log.Information(
                $"[Dresser slot {slot}] {item.Name}{(isHq ? " (HQ)" : "")} | Item ID {itemId} | {FormatModel(item)} | " +
                $"Dye 1: {StainName(stainSheet, stain0Ids[slot])} | Dye 2: {StainName(stainSheet, stain1Ids[slot])}");
            count++;

            // Outfit sets have no model of their own, so they can't be duplicates.
            if (item.ModelMain == 0)
                continue;

            var isWeapon = IsWeapon(item);
            var key = new ModelKey(
                item.EquipSlotCategory.RowId,
                StripVariant(item.ModelMain, isWeapon),
                StripVariant(item.ModelSub, isWeapon: true));
            if (!modelGroups.TryGetValue(key, out var group))
                modelGroups[key] = group = [];
            group.Add((slot, item));
        }

        var duplicateGroups = modelGroups.Values.Where(g => g.Count > 1).ToList();
        DuplicateItemIds = duplicateGroups.SelectMany(g => g.Select(entry => entry.Item.RowId)).ToHashSet();

        Plugin.Log.Information($"Glamour dresser contains {count} item(s).");
        foreach (var group in duplicateGroups)
        {
            var names = string.Join(", ", group.Select(entry => $"{entry.Item.Name} (slot {entry.Slot})"));
            Plugin.Log.Information($"Same model: {names}");
        }

        Plugin.ChatGui.Print(
            $"Read {count} glamour dresser item(s), {duplicateGroups.Count} duplicate model group(s). Type /xllog for details.");
    }

    // ModelMain/ModelSub pack several ids into one number, laid out differently for weapons (incl. shields) and armor.
    private static string FormatModel(Item item)
    {
        if (item.ModelMain == 0)
            return "Model: none";

        var text = IsWeapon(item) ? $"Model: {FormatWeaponModel(item.ModelMain)}" : $"Model: {FormatArmorModel(item.ModelMain)}";
        if (item.ModelSub != 0)
            text += $" | Off-hand model: {FormatWeaponModel(item.ModelSub)}";
        return text;
    }

    // Weapons and shields use the weapon model layout; everything else uses the armor layout.
    private static bool IsWeapon(Item item)
    {
        var slot = item.EquipSlotCategory.Value;
        return slot.MainHand != 0 || slot.OffHand != 0;
    }

    // Weapons: keep model id (bits 0-15) and base id (bits 16-31), drop variant (bits 32-47).
    // Armor: keep set id (bits 0-15), drop variant (bits 16-23).
    private static ulong StripVariant(ulong model, bool isWeapon) =>
        isWeapon ? model & 0xFFFF_FFFF : model & 0xFFFF;

    private static string FormatWeaponModel(ulong model) =>
        $"w{(ushort)model:D4} b{(ushort)(model >> 16):D4} v{(ushort)(model >> 32)}";

    private static string FormatArmorModel(ulong model) =>
        $"e{(ushort)model:D4} v{(byte)(model >> 16)}";

    private static string StainName(ExcelSheet<Stain> stainSheet, byte stainId)
    {
        if (stainId == 0)
            return "none";

        return stainSheet.TryGetRow(stainId, out var stain) ? stain.Name.ToString() : $"#{stainId}";
    }
}
