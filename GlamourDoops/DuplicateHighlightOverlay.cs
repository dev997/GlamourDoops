using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SamplePlugin;

// Draws a box over every item slot in the open glamour dresser page whose item shares its model with another item:
// yellow for true duplicates (exact same model), cyan for variants (same base model, different variant).
public sealed unsafe class DuplicateHighlightOverlay : IDisposable
{
    private const float BoxThickness = 3f;
    // ImGui colours are packed as 0xAABBGGRR, so this is opaque yellow (R=FF, G=D8, B=00).
    private const uint DuplicateColor = 0xFF00D8FF;
    // Opaque cyan (R=00, G=FF, B=FF).
    private const uint VariantColor = 0xFFFFFF00;

    private static readonly IReadOnlySet<uint> NoItems = new HashSet<uint>();

    private readonly GlamourDresserReader reader;
    private readonly Configuration configuration;

    public DuplicateHighlightOverlay(GlamourDresserReader reader, Configuration configuration)
    {
        this.reader = reader;
        this.configuration = configuration;
        Plugin.PluginInterface.UiBuilder.Draw += Draw;
    }

    public void Dispose() => Plugin.PluginInterface.UiBuilder.Draw -= Draw;

    private void Draw()
    {
        var duplicates = reader.DuplicateItemIds;
        var variants = configuration.HighlightVariants ? reader.VariantItemIds : NoItems;
        if (duplicates.Count == 0 && variants.Count == 0)
            return;

        var addon = (AddonMiragePrismPrismBox*)Plugin.GameGui.GetAddonByName(GlamourDresserReader.DresserAddonName).Address;
        if (addon == null || !addon->AtkUnitBase.IsVisible)
            return;

        var agent = AgentMiragePrismPrismBox.Instance();
        if (agent == null || !agent->IsDataLoaded || agent->Data == null)
            return;

        // The addon shows up to 50 items per page. PageItemIndexes maps each visible slot
        // to an entry in the agent's filtered/sorted item list.
        var data = agent->Data;
        var pageItemIndexes = data->PageItemIndexes;
        var itemSlots = addon->ItemSlots;
        var drawList = ImGui.GetBackgroundDrawList();
        var viewportOffset = ImGui.GetMainViewport().Pos;

        for (var i = 0; i < itemSlots.Length && i < pageItemIndexes.Length; i++)
        {
            var itemIndex = pageItemIndexes[i];
            if (itemIndex < 0 || itemIndex >= data->ItemCount)
                continue;

            var itemId = GlamourDresserReader.StripHq(data->PrismBoxItems[itemIndex].ItemId);
            uint color;
            if (duplicates.Contains(itemId))
                color = DuplicateColor;
            else if (variants.Contains(itemId))
                color = VariantColor;
            else
                continue;

            var button = itemSlots[i].Button;
            if (button == null || button->OwnerNode == null)
                continue;

            var node = &button->OwnerNode->AtkResNode;
            if (!node->IsVisible())
                continue;

            var (min, max) = GetScreenBounds(node);
            drawList.AddRect(min + viewportOffset, max + viewportOffset, color, 4f, ImDrawFlags.None, BoxThickness);
        }
    }

    // ScreenX/ScreenY are already absolute, but Width/Height are unscaled,
    // so multiply by the scale of the node and all of its parents (including the window's scale).
    private static (Vector2 Min, Vector2 Max) GetScreenBounds(AtkResNode* node)
    {
        var scale = Vector2.One;
        for (var current = node; current != null; current = current->ParentNode)
            scale *= new Vector2(current->ScaleX, current->ScaleY);

        var min = new Vector2(node->ScreenX, node->ScreenY);
        var max = min + new Vector2(node->Width, node->Height) * scale;
        return (min, max);
    }
}
