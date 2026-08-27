using System;
using MelonLoader;

namespace NugzzMinimap.Helpers;

/// <summary>
/// Defines and loads all user-facing NugzzMinimap settings.
/// Missing categories or entries are repaired individually so upgrades cannot leave null settings.
/// </summary>
public sealed class MapPreferences
{
    private const string TogglesCategoryId = "NugzzMinimap_1_Toggles";
    private const string TextDisplayCategoryId = "NugzzMinimap_2_TextDisplay";
    private const string PositionCategoryId = "NugzzMinimap_3_Position";
    private const string PreferencesPath = "UserData/NugzzMinimap.cfg";

    private const float DefaultScaleFactor = 1.5f;
    private const float IncreasedScaleFactor = 2.2f;

    public MelonPreferences_Category TogglesCategory { get; private set; }
    public MelonPreferences_Category TextDisplayCategory { get; private set; }
    public MelonPreferences_Category PositionCategory { get; private set; }

    public MelonPreferences_Entry<bool> MinimapEnabled { get; private set; }
    public MelonPreferences_Entry<bool> ShowGameTime { get; private set; }
    public MelonPreferences_Entry<bool> IncreaseSize { get; private set; }
    public MelonPreferences_Entry<bool> ShowSquareMinimap { get; private set; }
    public MelonPreferences_Entry<bool> TrackNPCs { get; private set; }
    public MelonPreferences_Entry<bool> TrackPolice { get; private set; }
    public MelonPreferences_Entry<bool> ShowDeadDropPois { get; private set; }
    public MelonPreferences_Entry<int> MinimapZoom { get; private set; }
    public MelonPreferences_Entry<bool> ShowMapText { get; private set; }
    public MelonPreferences_Entry<bool> ShowLocationIcons { get; private set; }
    public MelonPreferences_Entry<bool> PlayerOrientedMap { get; private set; }

    public MelonPreferences_Entry<float> MinimapPositionX { get; private set; }
    public MelonPreferences_Entry<float> MinimapPositionY { get; private set; }
    public MelonPreferences_Entry<float> TimeDisplayPositionX { get; private set; }
    public MelonPreferences_Entry<float> TimeDisplayPositionY { get; private set; }

    public MelonPreferences_Entry<TimeDisplayColor> TimeDisplayTextColor { get; private set; }
    public MelonPreferences_Entry<int> TimeDisplayBackgroundOpacity { get; private set; }
    public MelonPreferences_Entry<int> TimeDisplayTextSize { get; private set; }

    public float MinimapScaleFactor => IncreaseSize.Value ? IncreasedScaleFactor : DefaultScaleFactor;

    public float MinimapZoomScale => GetMinimapZoomScale(MinimapZoom.Value);

    public static float GetMinimapZoomScale(int zoomLevel)
    {
        int level = Math.Max(1, Math.Min(10, zoomLevel));
        return 0.4f + (level - 1) * 0.0667f;
    }

    public void LoadPreferences()
    {
        TogglesCategory = GetOrCreateCategory(TogglesCategoryId, "Minimap Toggles & Settings");
        TextDisplayCategory = GetOrCreateCategory(TextDisplayCategoryId, "Time Display Appearance");
        PositionCategory = GetOrCreateCategory(PositionCategoryId, "Position Settings");

        MinimapEnabled = GetOrCreateEntry(TogglesCategory, "A_MinimapEnabled", true,
            "Enable Minimap", "Enable or disable the entire minimap overlay");
        ShowGameTime = GetOrCreateEntry(TogglesCategory, "B_ShowGameTime", true,
            "Show Current Date & Time", "Display the in-game date and time on screen");
        IncreaseSize = GetOrCreateEntry(TogglesCategory, "C_IncreaseMapSize", false,
            "Enlarge Minimap", "Increase the size of the minimap for better visibility");
        ShowSquareMinimap = GetOrCreateEntry(TogglesCategory, "D_ShowSquareMinimap", false,
            "Enable Square Minimap", "Use a square minimap instead of circular");
        TrackNPCs = GetOrCreateEntry(TogglesCategory, "E_TrackNPCs", true,
            "Track NPCs on Minimap", "Show live NPC and potential-customer positions on the minimap");
        TrackPolice = GetOrCreateEntry(TogglesCategory, "F_TrackPolice", true,
            "Track Police on Minimap", "Show police positions and pursuit state on the minimap");
        ShowDeadDropPois = GetOrCreateEntry(TogglesCategory, "G_ShowDeadDropPOIs", true,
            "Show Dead Drop POIs", "Show or hide dead-drop names and icons on the minimap");
        MinimapZoom = GetOrCreateEntry(TogglesCategory, "H_MinimapZoom", 7,
            "Minimap Zoom Level", "Zoom level from 1 (zoomed out) to 10 (zoomed in)");
        ShowMapText = GetOrCreateEntry(TogglesCategory, "I_ShowMapText", true,
            "Show Minimap Text", "Show player names, building names, and location labels on the minimap");
        ShowLocationIcons = GetOrCreateEntry(TogglesCategory, "J_ShowLocationIcons", true,
            "Show House & Business Icons", "Show house, business, and other location icons on the minimap");
        PlayerOrientedMap = GetOrCreateEntry(TogglesCategory, "K_PlayerOrientedMap", false,
            "Player-Oriented Minimap", "Rotate the minimap so the local player's direction stays at the top");

        TimeDisplayTextColor = GetOrCreateEntry(TextDisplayCategory, "A_TimeDisplayTextColor",
            TimeDisplayColor.White, "Time Display Text Color", "Color of the time/date display text");
        TimeDisplayBackgroundOpacity = GetOrCreateEntry(TextDisplayCategory,
            "B_TimeDisplayBackgroundOpacity", 65, "Time Display Background Opacity",
            "Background opacity from 0 (transparent) to 100 (opaque)");
        TimeDisplayTextSize = GetOrCreateEntry(TextDisplayCategory, "C_TimeDisplayTextSize", 18,
            "Time Display Text Size", "Text size from 10 (small) to 30 (large)");

        MinimapPositionX = GetOrCreateEntry(PositionCategory, "A_MinimapPositionX", -2f,
            "Minimap Position: X Coordinate", "Horizontal offset from the top-right anchor");
        MinimapPositionY = GetOrCreateEntry(PositionCategory, "B_MinimapPositionY", -2f,
            "Minimap Position: Y Coordinate", "Vertical offset from the top-right anchor");
        TimeDisplayPositionX = GetOrCreateEntry(PositionCategory, "C_TimeDisplayPositionX", 0f,
            "Time Display Position: X Coordinate", "Horizontal offset relative to the minimap");
        TimeDisplayPositionY = GetOrCreateEntry(PositionCategory, "D_TimeDisplayPositionY", -5f,
            "Time Display Position: Y Coordinate", "Vertical offset relative to the minimap");

        // Assign the shared file after all entries exist; SetFilePath then loads saved values into them.
        TogglesCategory.SetFilePath(PreferencesPath);
        TextDisplayCategory.SetFilePath(PreferencesPath);
        PositionCategory.SetFilePath(PreferencesPath);
    }

    private static MelonPreferences_Category GetOrCreateCategory(string id, string displayName)
    {
        return MelonPreferences.GetCategory(id) ?? MelonPreferences.CreateCategory(id, displayName);
    }

    private static MelonPreferences_Entry<T> GetOrCreateEntry<T>(
        MelonPreferences_Category category,
        string id,
        T defaultValue,
        string displayName,
        string description)
    {
        return category.GetEntry<T>(id)
            ?? category.CreateEntry(id, defaultValue, displayName, description, false, false, null, null);
    }
}
