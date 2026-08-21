using System;
using NugzzMinimap.Main;
using NugzzMinimap.NPCSystem;
using HarmonyLib;
using MelonLoader;

namespace NugzzMinimap;

/// <summary>
/// MelonLoader entry point. It owns Harmony patches and the scene-level minimap lifetime.
/// </summary>
public sealed class Core : MelonMod
{
    private HarmonyLib.Harmony harmony;

    public override void OnInitializeMelon()
    {
        base.OnInitializeMelon();

        try
        {
            harmony = new HarmonyLib.Harmony("NugzzMinimap.NPCSystem");
            // PatchAll(Type) only scans that one type; the actual patches are nested classes.
            harmony.PatchAll(typeof(Core).Assembly);
            MelonLogger.Msg("NugzzMinimap patches initialized");
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"NugzzMinimap could not install its UI patches: {exception}");
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        base.OnSceneWasLoaded(buildIndex, sceneName);

        if (sceneName == "Main")
        {
            MelonLogger.Msg("NugzzMinimap initialized");
            MinimapManager.Instance.Initialize();
            return;
        }

        // Do not access Instance here: its getter creates a new persistent object.
        MinimapManager.DestroyInstance();
    }

    public override void OnDeinitializeMelon()
    {
        MinimapManager.DestroyInstance();
        harmony?.UnpatchSelf();
        harmony = null;
        base.OnDeinitializeMelon();
    }
}
