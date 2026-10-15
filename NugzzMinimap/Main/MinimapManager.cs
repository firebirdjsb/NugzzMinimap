using System.Collections;
using NugzzMinimap.Helpers;
using NugzzMinimap.NPCSystem;
using Il2CppInterop.Runtime.Attributes;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone.Map;
using MelonLoader;
using UnityEngine;

namespace NugzzMinimap.Main;

/// <summary>
/// Owns the persistent minimap view, waits for game singletons, and binds preferences to live UI updates.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class MinimapManager : MonoBehaviour
{
    private static MinimapManager instance;

    private MinimapView minimapView;
    private MapPreferences mapPreferences;
    private object initializationCoroutine;
    private bool eventsSubscribed;
    private bool cleanedUp;

    public static MinimapManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("MinimapManager").AddComponent<MinimapManager>();
                Object.DontDestroyOnLoad(instance.gameObject);
            }

            return instance;
        }
    }

    /// <summary>Destroys the current manager without accidentally creating one.</summary>
    public static void DestroyInstance()
    {
        if (instance == null)
        {
            return;
        }

        MinimapManager manager = instance;
        instance = null;
        manager.Cleanup();
        if (manager != null)
        {
            Object.Destroy(manager.gameObject);
        }
    }

    public void Initialize()
    {
        if (minimapView != null)
        {
            return;
        }

        cleanedUp = false;
        mapPreferences = new MapPreferences();
        mapPreferences.LoadPreferences();

        minimapView = new GameObject("MinimapView").AddComponent<MinimapView>();
        minimapView.transform.SetParent(transform, false);
        initializationCoroutine = MelonCoroutines.Start(InitializeWhenReady());
    }

    public void Cleanup()
    {
        if (cleanedUp)
        {
            return;
        }

        cleanedUp = true;
        StopInitializationCoroutine();
        UnsubscribeEvents();
        NPCButtonPatches.SetMinimapView(null);

        if (minimapView != null)
        {
            minimapView.Cleanup();
            Object.Destroy(minimapView.gameObject);
            minimapView = null;
        }

        mapPreferences = null;
        if (instance == this)
        {
            instance = null;
        }
    }

    [HideFromIl2Cpp]
    private IEnumerator InitializeWhenReady()
    {
        Player player;
        MapApp mapApp;
        while (!TryGetGameContext(out player, out mapApp))
        {
            yield return null;
        }

        if (minimapView == null || mapPreferences == null || cleanedUp)
        {
            initializationCoroutine = null;
            yield break;
        }

        minimapView.Initialize(
            player,
            mapApp,
            mapPreferences.MinimapEnabled.Value,
            mapPreferences.MinimapScaleFactor,
            mapPreferences.ShowSquareMinimap.Value,
            mapPreferences.ShowGameTime.Value,
            mapPreferences.TrackNPCs.Value,
            mapPreferences.TrackPolice.Value,
            mapPreferences.ShowDeadDropPois.Value,
            mapPreferences.ShowMapText.Value,
            mapPreferences.ShowLocationIcons.Value,
            mapPreferences.PlayerOrientedMap.Value,
            mapPreferences.ToggleControlsHotkey.Value,
            mapPreferences.MinimapZoom.Value,
            mapPreferences.MinimapZoomScale,
            mapPreferences.MinimapPositionX.Value,
            mapPreferences.MinimapPositionY.Value,
            mapPreferences.TimeDisplayPositionX.Value,
            mapPreferences.TimeDisplayPositionY.Value,
            mapPreferences.TimeDisplayTextColor.Value,
            mapPreferences.TimeDisplayBackgroundOpacity.Value / 100f,
            mapPreferences.TimeDisplayTextSize.Value,
            OnDeadDropButtonChanged,
            OnLocationIconButtonChanged,
            OnMapTextButtonChanged,
            OnZoomLevelButtonChanged,
            OnPlayerOrientationButtonChanged);

        NPCButtonPatches.SetMinimapView(minimapView);
        SubscribeEvents();
        initializationCoroutine = null;
    }

    private static bool TryGetGameContext(out Player player, out MapApp mapApp)
    {
        player = null;
        mapApp = null;

        try
        {
            player = Player.Local;
            if (player == null || !PlayerSingleton<MapApp>.InstanceExists)
            {
                return false;
            }

            mapApp = PlayerSingleton<MapApp>.Instance;
            return mapApp != null && mapApp.ContentRect != null && mapApp.PoIContainer != null;
        }
        catch
        {
            // Scene transitions can invalidate an IL2CPP wrapper between two property reads.
            player = null;
            mapApp = null;
            return false;
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void StopInitializationCoroutine()
    {
        if (initializationCoroutine == null)
        {
            return;
        }

        MelonCoroutines.Stop(initializationCoroutine);
        initializationCoroutine = null;
    }

    private void SubscribeEvents()
    {
        if (eventsSubscribed || mapPreferences == null)
        {
            return;
        }

        mapPreferences.MinimapEnabled.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnMinimapEnabledChanged));
        mapPreferences.IncreaseSize.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnIncreaseSizeChanged));
        mapPreferences.ShowSquareMinimap.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnShowSquareMinimapChanged));
        mapPreferences.ShowGameTime.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnShowGameTimeChanged));
        mapPreferences.TrackNPCs.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnTrackNPCsChanged));
        mapPreferences.TrackPolice.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnTrackPoliceChanged));
        mapPreferences.ShowDeadDropPois.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnShowDeadDropPoisChanged));
        mapPreferences.ShowMapText.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnShowMapTextChanged));
        mapPreferences.ShowLocationIcons.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnShowLocationIconsChanged));
        mapPreferences.PlayerOrientedMap.OnEntryValueChanged.Subscribe(new LemonAction<bool, bool>(OnPlayerOrientedMapChanged));
        mapPreferences.ToggleControlsHotkey.OnEntryValueChanged.Subscribe(new LemonAction<KeyCode, KeyCode>(OnToggleControlsHotkeyChanged));
        mapPreferences.MinimapZoom.OnEntryValueChanged.Subscribe(new LemonAction<int, int>(OnMinimapZoomChanged));
        mapPreferences.MinimapPositionX.OnEntryValueChanged.Subscribe(new LemonAction<float, float>(OnMinimapPositionXChanged));
        mapPreferences.MinimapPositionY.OnEntryValueChanged.Subscribe(new LemonAction<float, float>(OnMinimapPositionYChanged));
        mapPreferences.TimeDisplayPositionX.OnEntryValueChanged.Subscribe(new LemonAction<float, float>(OnTimeDisplayPositionXChanged));
        mapPreferences.TimeDisplayPositionY.OnEntryValueChanged.Subscribe(new LemonAction<float, float>(OnTimeDisplayPositionYChanged));
        mapPreferences.TimeDisplayTextColor.OnEntryValueChanged.Subscribe(new LemonAction<TimeDisplayColor, TimeDisplayColor>(OnTimeDisplayTextColorChanged));
        mapPreferences.TimeDisplayBackgroundOpacity.OnEntryValueChanged.Subscribe(new LemonAction<int, int>(OnTimeDisplayBackgroundOpacityChanged));
        mapPreferences.TimeDisplayTextSize.OnEntryValueChanged.Subscribe(new LemonAction<int, int>(OnTimeDisplayTextSizeChanged));
        eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!eventsSubscribed || mapPreferences == null)
        {
            return;
        }

        mapPreferences.MinimapEnabled.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnMinimapEnabledChanged));
        mapPreferences.IncreaseSize.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnIncreaseSizeChanged));
        mapPreferences.ShowSquareMinimap.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnShowSquareMinimapChanged));
        mapPreferences.ShowGameTime.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnShowGameTimeChanged));
        mapPreferences.TrackNPCs.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnTrackNPCsChanged));
        mapPreferences.TrackPolice.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnTrackPoliceChanged));
        mapPreferences.ShowDeadDropPois.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnShowDeadDropPoisChanged));
        mapPreferences.ShowMapText.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnShowMapTextChanged));
        mapPreferences.ShowLocationIcons.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnShowLocationIconsChanged));
        mapPreferences.PlayerOrientedMap.OnEntryValueChanged.Unsubscribe(new LemonAction<bool, bool>(OnPlayerOrientedMapChanged));
        mapPreferences.ToggleControlsHotkey.OnEntryValueChanged.Unsubscribe(new LemonAction<KeyCode, KeyCode>(OnToggleControlsHotkeyChanged));
        mapPreferences.MinimapZoom.OnEntryValueChanged.Unsubscribe(new LemonAction<int, int>(OnMinimapZoomChanged));
        mapPreferences.MinimapPositionX.OnEntryValueChanged.Unsubscribe(new LemonAction<float, float>(OnMinimapPositionXChanged));
        mapPreferences.MinimapPositionY.OnEntryValueChanged.Unsubscribe(new LemonAction<float, float>(OnMinimapPositionYChanged));
        mapPreferences.TimeDisplayPositionX.OnEntryValueChanged.Unsubscribe(new LemonAction<float, float>(OnTimeDisplayPositionXChanged));
        mapPreferences.TimeDisplayPositionY.OnEntryValueChanged.Unsubscribe(new LemonAction<float, float>(OnTimeDisplayPositionYChanged));
        mapPreferences.TimeDisplayTextColor.OnEntryValueChanged.Unsubscribe(new LemonAction<TimeDisplayColor, TimeDisplayColor>(OnTimeDisplayTextColorChanged));
        mapPreferences.TimeDisplayBackgroundOpacity.OnEntryValueChanged.Unsubscribe(new LemonAction<int, int>(OnTimeDisplayBackgroundOpacityChanged));
        mapPreferences.TimeDisplayTextSize.OnEntryValueChanged.Unsubscribe(new LemonAction<int, int>(OnTimeDisplayTextSizeChanged));
        eventsSubscribed = false;
    }

    private void OnMinimapEnabledChanged(bool oldValue, bool newValue) => minimapView?.ToggleMinimapVisibility(newValue);
    private void OnIncreaseSizeChanged(bool oldValue, bool newValue) => minimapView?.UpdateMinimapUISize(mapPreferences.MinimapScaleFactor);
    private void OnShowSquareMinimapChanged(bool oldValue, bool newValue) => minimapView?.UpdateMinimapShape(newValue);
    private void OnShowGameTimeChanged(bool oldValue, bool newValue) => minimapView?.UpdateTimeDisplayVisibility(newValue);
    private void OnTrackNPCsChanged(bool oldValue, bool newValue) => minimapView?.UpdateNPCTracking(newValue);
    private void OnTrackPoliceChanged(bool oldValue, bool newValue) => minimapView?.UpdatePoliceTracking(newValue);
    private void OnShowDeadDropPoisChanged(bool oldValue, bool newValue) => minimapView?.UpdateDeadDropVisibility(newValue);
    private void OnShowMapTextChanged(bool oldValue, bool newValue) => minimapView?.UpdateMapTextVisibility(newValue);
    private void OnShowLocationIconsChanged(bool oldValue, bool newValue) => minimapView?.UpdateLocationIconVisibility(newValue);
    private void OnPlayerOrientedMapChanged(bool oldValue, bool newValue) => minimapView?.UpdatePlayerOrientedMap(newValue);

    [HideFromIl2Cpp]
    private void OnToggleControlsHotkeyChanged(KeyCode oldValue, KeyCode newValue)
        => minimapView?.UpdateControlsHotkey(newValue);

    [HideFromIl2Cpp]
    private void OnDeadDropButtonChanged(bool isVisible)
    {
        if (mapPreferences?.ShowDeadDropPois == null || mapPreferences.ShowDeadDropPois.Value == isVisible)
        {
            return;
        }

        mapPreferences.ShowDeadDropPois.Value = isVisible;
        mapPreferences.TogglesCategory.SaveToFile(false);
    }

    [HideFromIl2Cpp]
    private void OnLocationIconButtonChanged(bool isVisible)
    {
        if (mapPreferences?.ShowLocationIcons == null || mapPreferences.ShowLocationIcons.Value == isVisible)
        {
            return;
        }

        mapPreferences.ShowLocationIcons.Value = isVisible;
        mapPreferences.TogglesCategory.SaveToFile(false);
    }

    [HideFromIl2Cpp]
    private void OnMapTextButtonChanged(bool isVisible)
    {
        if (mapPreferences?.ShowMapText == null || mapPreferences.ShowMapText.Value == isVisible)
        {
            return;
        }

        mapPreferences.ShowMapText.Value = isVisible;
        mapPreferences.TogglesCategory.SaveToFile(false);
    }

    [HideFromIl2Cpp]
    private void OnZoomLevelButtonChanged(int zoomLevel)
    {
        int clampedZoomLevel = System.Math.Max(1, System.Math.Min(10, zoomLevel));
        if (mapPreferences?.MinimapZoom == null || mapPreferences.MinimapZoom.Value == clampedZoomLevel)
        {
            return;
        }

        mapPreferences.MinimapZoom.Value = clampedZoomLevel;
        mapPreferences.TogglesCategory.SaveToFile(false);
    }

    [HideFromIl2Cpp]
    private void OnPlayerOrientationButtonChanged(bool isEnabled)
    {
        if (mapPreferences?.PlayerOrientedMap == null || mapPreferences.PlayerOrientedMap.Value == isEnabled)
        {
            return;
        }

        mapPreferences.PlayerOrientedMap.Value = isEnabled;
        mapPreferences.TogglesCategory.SaveToFile(false);
    }

    private void OnMinimapZoomChanged(int oldValue, int newValue)
        => minimapView?.UpdateZoomLevel(newValue, mapPreferences.MinimapZoomScale);

    private void OnMinimapPositionXChanged(float oldValue, float newValue)
        => minimapView?.UpdateMinimapPosition(newValue, mapPreferences.MinimapPositionY.Value);

    private void OnMinimapPositionYChanged(float oldValue, float newValue)
        => minimapView?.UpdateMinimapPosition(mapPreferences.MinimapPositionX.Value, newValue);

    private void OnTimeDisplayPositionXChanged(float oldValue, float newValue)
        => minimapView?.UpdateTimeDisplayPosition(newValue, mapPreferences.TimeDisplayPositionY.Value);

    private void OnTimeDisplayPositionYChanged(float oldValue, float newValue)
        => minimapView?.UpdateTimeDisplayPosition(mapPreferences.TimeDisplayPositionX.Value, newValue);

    [HideFromIl2Cpp]
    private void OnTimeDisplayTextColorChanged(TimeDisplayColor oldValue, TimeDisplayColor newValue)
        => minimapView?.UpdateTimeDisplayColor(newValue);

    private void OnTimeDisplayBackgroundOpacityChanged(int oldValue, int newValue)
        => minimapView?.UpdateTimeDisplayBackgroundOpacity(newValue / 100f);

    private void OnTimeDisplayTextSizeChanged(int oldValue, int newValue)
        => minimapView?.UpdateTimeDisplayTextSize(newValue);
}
