using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("GlamourDoops###GlamourDoops Config Window")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(232, 90);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        
    }

    public override void Draw()
    {
        // Can't ref a property, so use a local copy
        var highlightVariants = configuration.HighlightVariants;
        if (ImGui.Checkbox("Highlight variants (cyan)", ref highlightVariants))
        {
            configuration.HighlightVariants = highlightVariants;
            // Can save immediately on change if you don't want to provide a "Save and Close" button
            configuration.Save();
        };
    }
}
