using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NugzzMinimap.Helpers;
using Il2CppInterop.Runtime.Attributes;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Map;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using HintDisplay = Il2CppScheduleOne.UI.HintDisplay;

namespace NugzzMinimap.Main;

/// <summary>
/// Builds the HUD and renders live location, NPC, police, and multiplayer markers from game objects.
/// All Unity and game-object access happens on MelonLoader's main-thread coroutine runner.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class MinimapView : MonoBehaviour
{
    private const float VisualStateSyncInterval = 0.1f;
    private const float MapAppStateSyncInterval = 0.1f;
    private const float SewerStateSyncInterval = 0.25f;
    private const float ActorRegistrySyncInterval = 2f;
    private const float PoliceStateSyncInterval = 0.3f;
    private const float PlayerNameSyncInterval = 1f;
    private const int ActorRegistryItemsPerFrame = 1;
    private const int ActorScanPhaseIdle = 0;
    private const int ActorScanPhaseNpcs = 1;
    private const int ActorScanPhaseOfficers = 2;
    private const float NativeLabelReferenceZoom = 1.5f;
    private const float HintScreenMargin = 24f;
    private const float ToggleButtonWidth = 70f;
    private const float ToggleButtonHeight = 22f;
    private const float ToggleButtonSpacing = 73f;
    private const float PrimaryButtonRowY = -48f;
    private const float SecondaryButtonRowY = -73f;
    private const float ZoomButtonWidth = 40f;
    private const float OrientationButtonWidth = 124f;
    private const string SewerMapResourceName = "NugzzMinimap.Assets.SewerMap.png";

    // The supplied sewer graphic is a crop of the game's 4096x4096 Map_Full texture. These
    // normalized values were calibrated from the unchanged street/building pixels, so it stays
    // registered to MapPositionUtility at every HUD size and zoom level.
    private const float SewerMapNormalizedWidth = 0.62997734f;
    private const float SewerMapNormalizedHeight = 0.4116576f;
    private const float SewerMapNormalizedCenterX = 0.008428379f;
    private const float SewerMapNormalizedCenterY = 0.12910239f;
    private const float SewerMapRotation = -0.02771966f;

    private static readonly Color PoliceBlue = new(0.2f, 0.4f, 1f, 1f);
    private static readonly Color PoliceRed = new(1f, 0.15f, 0.15f, 1f);
    private static readonly Color ToggleEnabledColor = new(0.12f, 0.48f, 0.24f, 0.95f);
    private static readonly Color ToggleDisabledColor = new(0.5f, 0.14f, 0.14f, 0.95f);
    private static readonly Color ControlButtonColor = new(0.17f, 0.2f, 0.25f, 0.95f);
    private static readonly Color DisabledControlButtonColor = new(0.12f, 0.12f, 0.12f, 0.75f);

    private readonly Dictionary<int, GameObject> npcPois = new();
    private readonly Dictionary<int, GameObject> policePois = new();
    private readonly Dictionary<IntPtr, PoliceOfficer> poiOfficerMap = new();
    private readonly Dictionary<IntPtr, Image[]> policeImageCache = new();
    private readonly Dictionary<int, GameObject> mapMirrorObjects = new();
    private readonly HashSet<int> liveMapSourceIds = new();
    private readonly List<int> staleMapSourceIds = new();
    private readonly Dictionary<int, GameObject> playerMarkers = new();
    private readonly Dictionary<int, RectTransform> playerMarkerRects = new();
    private readonly Dictionary<int, Image> playerMarkerImages = new();
    private readonly Dictionary<int, Text> playerMarkerLabels = new();
    private readonly Dictionary<int, string> playerMarkerSourceNames = new();
    private readonly HashSet<int> livePlayerIds = new();
    private readonly List<int> stalePlayerIds = new();
    private readonly Dictionary<int, POI> livePoiSources = new();
    private readonly Dictionary<int, GameObject> livePoiMarkers = new();
    private readonly Dictionary<int, RectTransform> livePoiMarkerRects = new();
    private readonly Dictionary<int, Image> livePoiMarkerBackgrounds = new();
    private readonly Dictionary<int, Image> livePoiMarkerIcons = new();
    private readonly Dictionary<int, Text> livePoiMarkerLabels = new();
    private readonly Dictionary<int, GameObject> liveBuildingLabelObjects = new();
    private readonly Dictionary<int, Text> liveBuildingLabelSources = new();
    private readonly Dictionary<int, RectTransform> liveBuildingLabelRects = new();
    private readonly Dictionary<int, Quaternion> liveBuildingLabelBaseRotations = new();
    private readonly Dictionary<int, Text> liveBuildingLabels = new();
    private readonly Dictionary<int, Shadow> liveBuildingLabelShadows = new();
    private readonly Dictionary<int, NPC> liveNpcSources = new();
    private readonly Dictionary<int, Customer> liveCustomerSources = new();
    private readonly Dictionary<int, GameObject> liveNpcMarkers = new();
    private readonly Dictionary<int, RectTransform> liveNpcMarkerRects = new();
    private readonly Dictionary<int, Image> liveNpcPotentialCustomerCircles = new();
    private readonly Dictionary<int, Image> liveNpcMarkerBackgrounds = new();
    private readonly Dictionary<int, Image> liveNpcMarkerPortraits = new();
    private readonly Dictionary<int, PoliceOfficer> livePoliceSources = new();
    private readonly Dictionary<int, GameObject> livePoliceMarkers = new();
    private readonly Dictionary<int, RectTransform> livePoliceMarkerRects = new();
    private readonly Dictionary<int, Image> livePoliceMarkerBackgrounds = new();
    private readonly Dictionary<int, Image> livePoliceMarkerPortraits = new();
    private readonly HashSet<int> scannedPoiIds = new();
    private readonly HashSet<int> scannedBuildingLabelIds = new();
    private readonly HashSet<int> scannedNpcIds = new();
    private readonly HashSet<int> scannedPoliceIds = new();
    private readonly List<int> staleLiveMarkerIds = new();
    private readonly Vector3[] minimapWorldCorners = new Vector3[4];

    // Registry discovery is deliberately incremental. A synchronous pass across every NPC and
    // officer caused a single long frame at each refresh interval, which was especially visible
    // while the player was moving quickly in a vehicle or on a skateboard.
    private Il2CppSystem.Collections.Generic.List<NPC> npcRegistryScanList;
    private Il2CppSystem.Collections.Generic.List<PoliceOfficer> officerRegistryScanList;
    private int npcRegistryScanIndex;
    private int officerRegistryScanIndex;
    private int actorScanPhase = ActorScanPhaseIdle;
    private bool npcRegistryReadOk;
    private bool officerRegistryReadOk;

    private GameObject canvasGo;
    private GameObject mapContentContainer;
    private Image maskImage;
    private Image borderImage;
    private GameObject blackCircleBackground;
    private Image blackCircleImage;
    private TimeDisplayView timeDisplayView;
    private Button deadDropToggleButton;
    private Image deadDropToggleButtonImage;
    private Text deadDropToggleButtonText;
    private Button locationIconToggleButton;
    private Image locationIconToggleButtonImage;
    private Text locationIconToggleButtonText;
    private Button mapTextToggleButton;
    private Image mapTextToggleButtonImage;
    private Text mapTextToggleButtonText;
    private Button zoomOutButton;
    private Image zoomOutButtonImage;
    private Button zoomInButton;
    private Image zoomInButtonImage;
    private Button playerOrientationToggleButton;
    private Image playerOrientationToggleButtonImage;
    private Text playerOrientationToggleButtonText;
    private Action<bool> deadDropPreferenceCallback;
    private Action<bool> locationIconPreferenceCallback;
    private Action<bool> mapTextPreferenceCallback;
    private Action<int> zoomLevelPreferenceCallback;
    private Action<bool> playerOrientationPreferenceCallback;
    private UnityAction deadDropToggleAction;
    private UnityAction locationIconToggleAction;
    private UnityAction mapTextToggleAction;
    private UnityAction zoomOutAction;
    private UnityAction zoomInAction;
    private UnityAction playerOrientationToggleAction;

    private Transform playerTransform;
    private RectTransform mapContentRT;
    private RectTransform containerRT;
    private MapApp mapAppInstance;
    private RectTransform mapSourceRoot;
    private GameObject mapMirrorRoot;
    private GameObject mapBackgroundMirror;
    private Image mapBackgroundImage;
    private GameObject sewerMapLayer;
    private Image sewerMapImage;
    private Transform mapLabelSourceRoot;
    private GameObject mapLabelMirrorRoot;
    private GameObject liveBuildingLabelLayer;
    private GameObject livePoiMarkerLayer;
    private GameObject liveNpcMarkerLayer;
    private GameObject livePoliceMarkerLayer;
    private GameObject playerMarkerLayer;

    private Sprite circleSprite;
    private Sprite squareSprite;
    private Sprite solidCircleSprite;
    private Sprite playerArrowSprite;
    private Sprite deadDropButtonSprite;
    private Sprite sewerMapSprite;

    private GameObject selectedNpcPoi;
    private GameObject benjiPoITemplate;
    private bool npcTrackingEnabled = true;
    private bool policeTrackingEnabled = true;
    private bool deadDropPoisVisible = true;
    private bool mapTextVisible = true;
    private bool locationIconsVisible = true;
    private bool playerOrientedMap;
    private bool clickableControlsVisible = true;
    private bool hotkeyInputAvailable = true;
    private KeyCode toggleControlsHotkey = KeyCode.Home;
    private bool npcPoisInitialized;
    private bool policePoisInitialized;
    private bool policeFlashState;
    private bool isMinimapEnabled = true;
    private bool mapAppOpen;
    private bool isSewerMapActive;
    private bool sewerStateInitialized;
    private bool cleanedUp;
    private float zoomScale = 0.7f;
    private float currentMapRotation;
    private float nextVisualStateSyncTime;
    private float nextMapAppStateSyncTime;
    private float nextLiveMarkerScanTime;
    private float nextPoliceStateSyncTime;
    private float nextPlayerNameSyncTime;
    private float nextSewerStateSyncTime;
    private int zoomLevel = 7;
    private int lastLoggedPoiCount = -1;
    private int lastLoggedBuildingLabelCount = -1;
    private int lastLoggedNpcCount = -1;
    private int lastLoggedPoliceCount = -1;
    private int lastLoggedPotentialCustomerCount = -1;
    private bool liveMarkerSummaryLogged;
    private bool hintPlacementSummaryLogged;
    private bool hintPlacementShifted;
    private RectTransform shiftedHintContainer;
    private HintDisplay hintDisplayInstance;
    private HintDisplay shiftedHintDisplay;
    private Vector2 hintNativeAnchorMin;
    private Vector2 hintNativeAnchorMax;
    private Vector2 hintNativePivot;
    private Vector2 hintNativeAnchoredPosition;
    private Vector2 hintNativeOffset;
    private Vector2 hintAppliedAnchorMin;
    private Vector2 hintAppliedAnchorMax;
    private Vector2 hintAppliedPivot;
    private Vector2 hintAppliedAnchoredPosition;
    private Vector2 hintAppliedOffset;

    private object npcInitializationCoroutine;
    private object customerMonitorCoroutine;
    private object policeInitializationCoroutine;
    private object officerMonitorCoroutine;
    private object policeColorCoroutine;

    [HideFromIl2Cpp]
    public void Initialize(
        Player player,
        MapApp mapApp,
        bool minimapEnabled,
        float minimapScaleFactor,
        bool showSquareMinimap,
        bool showGameTime,
        bool trackNPCs,
        bool trackPolice,
        bool showDeadDropPois,
        bool showMapText,
        bool showLocationIcons,
        bool usePlayerOrientedMap,
        KeyCode controlsHotkey,
        int minimapZoomLevel,
        float minimapZoomScale,
        float positionX,
        float positionY,
        float timeDisplayPositionX,
        float timeDisplayPositionY,
        TimeDisplayColor timeDisplayColor,
        float timeDisplayBackgroundOpacity,
        int timeDisplayTextSize,
        Action<bool> onDeadDropVisibilityChanged,
        Action<bool> onLocationIconVisibilityChanged,
        Action<bool> onMapTextVisibilityChanged,
        Action<int> onZoomLevelChanged,
        Action<bool> onPlayerOrientationChanged)
    {
        if (player == null || mapApp == null)
        {
            MelonLogger.Error("NugzzMinimap initialization was attempted before the player/map was ready.");
            return;
        }

        cleanedUp = false;
        liveMarkerSummaryLogged = false;
        hintPlacementSummaryLogged = false;
        playerTransform = player.transform;
        mapAppInstance = mapApp;
        npcTrackingEnabled = trackNPCs;
        policeTrackingEnabled = trackPolice;
        deadDropPoisVisible = showDeadDropPois;
        mapTextVisible = showMapText;
        locationIconsVisible = showLocationIcons;
        playerOrientedMap = usePlayerOrientedMap;
        clickableControlsVisible = true;
        hotkeyInputAvailable = true;
        toggleControlsHotkey = controlsHotkey;
        deadDropPreferenceCallback = onDeadDropVisibilityChanged;
        locationIconPreferenceCallback = onLocationIconVisibilityChanged;
        mapTextPreferenceCallback = onMapTextVisibilityChanged;
        zoomLevelPreferenceCallback = onZoomLevelChanged;
        playerOrientationPreferenceCallback = onPlayerOrientationChanged;
        zoomLevel = Mathf.Clamp(minimapZoomLevel, 1, 10);
        zoomScale = Mathf.Clamp(minimapZoomScale, 0.4f, 1f);
        isMinimapEnabled = minimapEnabled;

        MinimapState.UpdateState(!showSquareMinimap, minimapScaleFactor);
        CreateGeneratedSprites();

        // The UI is always created, even when initially disabled, so the live toggle can enable it later.
        CreateMinimapUI(
            minimapScaleFactor,
            showGameTime,
            !showSquareMinimap,
            positionX,
            positionY,
            timeDisplayPositionX,
            timeDisplayPositionY,
            timeDisplayColor,
            timeDisplayBackgroundOpacity,
            timeDisplayTextSize);

        LoadActualGameMap();
        SetStyle(!showSquareMinimap);
        nextMapAppStateSyncTime = 0f;
        nextLiveMarkerScanTime = 0f;
        nextPoliceStateSyncTime = 0f;
        nextPlayerNameSyncTime = 0f;
        nextSewerStateSyncTime = 0f;
        RefreshAllMarkerSources();
        ToggleMinimapVisibility(minimapEnabled);
    }

    public void Cleanup()
    {
        if (cleanedUp)
        {
            return;
        }

        cleanedUp = true;
        RestoreHintPlacement();
        StopMelonCoroutine(ref npcInitializationCoroutine);
        StopMelonCoroutine(ref customerMonitorCoroutine);
        StopMelonCoroutine(ref policeInitializationCoroutine);
        StopMelonCoroutine(ref officerMonitorCoroutine);
        StopMelonCoroutine(ref policeColorCoroutine);

        if (canvasGo != null)
        {
            if (deadDropToggleButton != null && deadDropToggleAction != null)
            {
                deadDropToggleButton.onClick.RemoveListener(deadDropToggleAction);
            }

            if (locationIconToggleButton != null && locationIconToggleAction != null)
            {
                locationIconToggleButton.onClick.RemoveListener(locationIconToggleAction);
            }

            if (mapTextToggleButton != null && mapTextToggleAction != null)
            {
                mapTextToggleButton.onClick.RemoveListener(mapTextToggleAction);
            }

            if (zoomOutButton != null && zoomOutAction != null)
            {
                zoomOutButton.onClick.RemoveListener(zoomOutAction);
            }

            if (zoomInButton != null && zoomInAction != null)
            {
                zoomInButton.onClick.RemoveListener(zoomInAction);
            }

            if (playerOrientationToggleButton != null && playerOrientationToggleAction != null)
            {
                playerOrientationToggleButton.onClick.RemoveListener(playerOrientationToggleAction);
            }

            UnityEngine.Object.Destroy(canvasGo);
            canvasGo = null;
        }

        DestroyGeneratedSprite(ref circleSprite);
        DestroyGeneratedSprite(ref squareSprite);
        DestroyGeneratedSprite(ref solidCircleSprite);
        DestroyGeneratedSprite(ref playerArrowSprite);
        DestroyGeneratedSprite(ref deadDropButtonSprite);
        DestroyGeneratedSprite(ref sewerMapSprite);

        npcPois.Clear();
        policePois.Clear();
        poiOfficerMap.Clear();
        policeImageCache.Clear();
        mapMirrorObjects.Clear();
        liveMapSourceIds.Clear();
        staleMapSourceIds.Clear();
        playerMarkers.Clear();
        playerMarkerRects.Clear();
        playerMarkerImages.Clear();
        playerMarkerLabels.Clear();
        playerMarkerSourceNames.Clear();
        livePlayerIds.Clear();
        stalePlayerIds.Clear();
        livePoiSources.Clear();
        livePoiMarkers.Clear();
        livePoiMarkerRects.Clear();
        livePoiMarkerBackgrounds.Clear();
        livePoiMarkerIcons.Clear();
        livePoiMarkerLabels.Clear();
        liveBuildingLabelObjects.Clear();
        liveBuildingLabelSources.Clear();
        liveBuildingLabelRects.Clear();
        liveBuildingLabelBaseRotations.Clear();
        liveBuildingLabels.Clear();
        liveBuildingLabelShadows.Clear();
        liveNpcSources.Clear();
        liveCustomerSources.Clear();
        liveNpcMarkers.Clear();
        liveNpcMarkerRects.Clear();
        liveNpcPotentialCustomerCircles.Clear();
        liveNpcMarkerBackgrounds.Clear();
        liveNpcMarkerPortraits.Clear();
        livePoliceSources.Clear();
        livePoliceMarkers.Clear();
        livePoliceMarkerRects.Clear();
        livePoliceMarkerBackgrounds.Clear();
        livePoliceMarkerPortraits.Clear();
        scannedPoiIds.Clear();
        scannedBuildingLabelIds.Clear();
        scannedNpcIds.Clear();
        scannedPoliceIds.Clear();
        staleLiveMarkerIds.Clear();
        npcRegistryScanList = null;
        officerRegistryScanList = null;
        npcRegistryScanIndex = 0;
        officerRegistryScanIndex = 0;
        actorScanPhase = ActorScanPhaseIdle;
        npcRegistryReadOk = false;
        officerRegistryReadOk = false;
        selectedNpcPoi = null;
        benjiPoITemplate = null;
        mapSourceRoot = null;
        mapMirrorRoot = null;
        mapBackgroundMirror = null;
        mapBackgroundImage = null;
        sewerMapLayer = null;
        sewerMapImage = null;
        mapLabelSourceRoot = null;
        mapLabelMirrorRoot = null;
        liveBuildingLabelLayer = null;
        livePoiMarkerLayer = null;
        liveNpcMarkerLayer = null;
        livePoliceMarkerLayer = null;
        playerMarkerLayer = null;
        deadDropToggleButton = null;
        deadDropToggleButtonImage = null;
        deadDropToggleButtonText = null;
        locationIconToggleButton = null;
        locationIconToggleButtonImage = null;
        locationIconToggleButtonText = null;
        mapTextToggleButton = null;
        mapTextToggleButtonImage = null;
        mapTextToggleButtonText = null;
        zoomOutButton = null;
        zoomOutButtonImage = null;
        zoomInButton = null;
        zoomInButtonImage = null;
        playerOrientationToggleButton = null;
        playerOrientationToggleButtonImage = null;
        playerOrientationToggleButtonText = null;
        deadDropPreferenceCallback = null;
        locationIconPreferenceCallback = null;
        mapTextPreferenceCallback = null;
        zoomLevelPreferenceCallback = null;
        playerOrientationPreferenceCallback = null;
        deadDropToggleAction = null;
        locationIconToggleAction = null;
        mapTextToggleAction = null;
        zoomOutAction = null;
        zoomInAction = null;
        playerOrientationToggleAction = null;
        hintDisplayInstance = null;
        mapAppInstance = null;
        isSewerMapActive = false;
        sewerStateInitialized = false;
    }

    public void ToggleMinimapVisibility(bool isVisible)
    {
        isMinimapEnabled = isVisible;
        UpdateCanvasVisibility();
    }

    public void UpdateMinimapUISize(float newScaleFactor)
    {
        if (containerRT == null)
        {
            return;
        }

        float scale = Mathf.Max(0.1f, newScaleFactor);
        containerRT.sizeDelta = new Vector2(150f * scale, 150f * scale);
        MinimapState.UpdateState(MinimapState.IsCircleMode, scale);
    }

    public void UpdateMinimapShape(bool isSquare)
    {
        bool useCircle = !isSquare;
        MinimapState.UpdateState(useCircle, MinimapState.ScaleFactor);
        SetStyle(useCircle);
    }

    public void UpdateTimeDisplayVisibility(bool isVisible) => timeDisplayView?.ToggleVisibility(isVisible);
    public void UpdateTimeDisplayPosition(float positionX, float positionY) => timeDisplayView?.UpdatePosition(positionX, positionY);
    [HideFromIl2Cpp]
    public void UpdateTimeDisplayColor(TimeDisplayColor color) => timeDisplayView?.UpdateColor(color);
    public void UpdateTimeDisplayBackgroundOpacity(float opacity) => timeDisplayView?.UpdateBackgroundOpacity(opacity);
    public void UpdateTimeDisplayTextSize(int textSize) => timeDisplayView?.UpdateTextSize(textSize);

    public void UpdateMapTextVisibility(bool isVisible)
    {
        mapTextVisible = isVisible;
        UpdateMapTextButtonVisual();
        foreach (Text label in playerMarkerLabels.Values)
        {
            if (label != null)
            {
                label.enabled = isVisible;
            }
        }

        foreach (Text label in liveBuildingLabels.Values)
        {
            if (label != null)
            {
                label.enabled = isVisible;
            }
        }

        foreach (Shadow shadow in liveBuildingLabelShadows.Values)
        {
            if (shadow != null)
            {
                shadow.enabled = isVisible;
            }
        }

        foreach (Text label in livePoiMarkerLabels.Values)
        {
            if (label != null)
            {
                label.enabled = isVisible && !string.IsNullOrWhiteSpace(label.text);
            }
        }

        if (mapMirrorRoot != null)
        {
            SynchronizeMapMirror();
        }
    }

    public void UpdateLocationIconVisibility(bool isVisible)
    {
        locationIconsVisible = isVisible;
        UpdateLocationIconButtonVisual();
        foreach (KeyValuePair<int, POI> pair in livePoiSources)
        {
            try
            {
                UpdateLocationMarkerVisual(pair.Key, pair.Value);
            }
            catch
            {
                // A location may be destroyed while its preference is being changed.
            }
        }
    }

    public void UpdateNPCTracking(bool isVisible)
    {
        npcTrackingEnabled = isVisible;
        if (liveNpcMarkerLayer != null)
        {
            liveNpcMarkerLayer.SetActive(isVisible);
        }

        foreach (GameObject poi in npcPois.Values)
        {
            Utils.SafeSetActive(poi, isVisible);
        }
    }

    public void UpdatePoliceTracking(bool isVisible)
    {
        policeTrackingEnabled = isVisible;
        if (livePoliceMarkerLayer != null)
        {
            livePoliceMarkerLayer.SetActive(isVisible);
        }

        if (isVisible)
        {
            policeImageCache.Clear();
        }

        foreach (GameObject poi in policePois.Values)
        {
            Utils.SafeSetActive(poi, isVisible);
        }
    }

    public void UpdateDeadDropVisibility(bool isVisible)
    {
        deadDropPoisVisible = isVisible;
        UpdateDeadDropButtonVisual();
        if (mapContentContainer != null)
        {
            ScanLiveLocations();
            LogLiveMarkerCountsIfChanged();
        }
    }

    public void UpdateZoomLevel(int newZoomLevel, float newZoomScale)
    {
        zoomLevel = Mathf.Clamp(newZoomLevel, 1, 10);
        zoomScale = Mathf.Clamp(newZoomScale, 0.4f, 1f);
        if (mapContentRT != null)
        {
            mapContentRT.localScale = new Vector3(zoomScale, zoomScale, 1f);
        }

        UpdateZoomButtonVisuals();
        RefreshCachedBuildingLabels();
        CenterMapOnPlayer();
    }

    public void UpdatePlayerOrientedMap(bool isEnabled)
    {
        playerOrientedMap = isEnabled;
        UpdatePlayerOrientationButtonVisual();
        CenterMapOnPlayer();
        UpdateUprightMarkerRotations();
    }

    [HideFromIl2Cpp]
    public void UpdateControlsHotkey(KeyCode hotkey)
    {
        toggleControlsHotkey = hotkey;
        hotkeyInputAvailable = true;
    }

    public void UpdateMinimapPosition(float positionX, float positionY)
    {
        if (containerRT != null)
        {
            containerRT.anchoredPosition = new Vector2(positionX, positionY);
        }
    }

    public void ShowSelectedNPC(GameObject npcPoi)
    {
        if (!CheckNpcPoi(npcPoi))
        {
            return;
        }

        selectedNpcPoi = npcPoi;
        Utils.SafeSetActive(npcPoi, true);
    }

    public void OnContactDetailOpened(NPC npc)
    {
        if (selectedNpcPoi != null && !npcTrackingEnabled)
        {
            Utils.SafeSetActive(selectedNpcPoi, false);
        }

        selectedNpcPoi = null;
        if (npc == null)
        {
            return;
        }

        Transform poiTransform = npc.gameObject.transform.Find("NPCPoI(Clone)");
        if (poiTransform != null && CheckNpcPoi(poiTransform.gameObject))
        {
            selectedNpcPoi = poiTransform.gameObject;
        }
    }

    public void OnMapClosed()
    {
        selectedNpcPoi = null;
        foreach (GameObject poi in npcPois.Values)
        {
            Utils.SafeSetActive(poi, npcTrackingEnabled);
        }
    }

    public void OnMapAppOpened()
    {
        mapAppOpen = true;
        UpdateCanvasVisibility();
    }

    [HideFromIl2Cpp]
    public void OnHintDisplayUpdated(HintDisplay hintDisplay)
    {
        if (!cleanedUp && hintDisplay != null)
        {
            hintDisplayInstance = hintDisplay;
        }
    }

    public void OnMapAppClosed()
    {
        CompleteMapAppClose();
    }

    private void CompleteMapAppClose()
    {
        mapAppOpen = false;
        nextVisualStateSyncTime = 0f;
        SynchronizeBackgroundMirror();
        nextLiveMarkerScanTime = 0f;
        RefreshCachedBuildingLabels();
        RefreshLiveMarkerSources();
        UpdateCanvasVisibility();
        SetStyle(MinimapState.IsCircleMode);
    }

    public void SetStyle(bool useCircle)
    {
        if (maskImage == null || borderImage == null)
        {
            return;
        }

        Sprite sprite = useCircle ? circleSprite : squareSprite;
        maskImage.sprite = sprite;
        borderImage.sprite = sprite;

        if (blackCircleBackground != null && blackCircleImage != null)
        {
            blackCircleImage.sprite = solidCircleSprite;
            blackCircleBackground.SetActive(useCircle);
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextMapAppStateSyncTime)
        {
            nextMapAppStateSyncTime = Time.unscaledTime + MapAppStateSyncInterval;
            SynchronizeMapAppState();
        }

        UpdateCanvasVisibility();
        HandleControlsHotkey();
        UpdateHintPlacementSafely();
        Player player = Player.Local;
        if (player != null)
        {
            SynchronizeSewerMapState(player);
        }

        if (!isMinimapEnabled || mapAppOpen || Time.timeScale == 0f || mapContentContainer == null)
        {
            return;
        }

        if (player == null)
        {
            return;
        }

        if (player.IsInVehicle && player.CurrentVehicle != null)
        {
            playerTransform = player.CurrentVehicle.transform;
        }
        else
        {
            playerTransform = player.transform;
        }

        CenterMapOnPlayer();
        UpdateLiveMapMarkers();
        UpdatePlayerMarkers();
        if (playerOrientedMap)
        {
            UpdateUprightMarkerRotations();
        }
    }

    [HideFromIl2Cpp]
    private void HandleControlsHotkey()
    {
        if (!hotkeyInputAvailable || toggleControlsHotkey == KeyCode.None
            || canvasGo == null || !canvasGo.activeInHierarchy)
        {
            return;
        }

        try
        {
            if (Input.GetKeyDown(toggleControlsHotkey))
            {
                SetClickableControlsVisible(!clickableControlsVisible);
            }
        }
        catch (Exception exception)
        {
            hotkeyInputAvailable = false;
            MelonLogger.Warning(
                $"NugzzMinimap disabled its controls hotkey because keyboard input was unavailable: {exception.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void SetClickableControlsVisible(bool isVisible)
    {
        clickableControlsVisible = isVisible;
        SetButtonVisible(deadDropToggleButton, isVisible);
        SetButtonVisible(locationIconToggleButton, isVisible);
        SetButtonVisible(mapTextToggleButton, isVisible);
        SetButtonVisible(zoomOutButton, isVisible);
        SetButtonVisible(zoomInButton, isVisible);
        SetButtonVisible(playerOrientationToggleButton, isVisible);
    }

    private static void SetButtonVisible(Button button, bool isVisible)
    {
        if (button != null && button.gameObject.activeSelf != isVisible)
        {
            button.gameObject.SetActive(isVisible);
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus || cleanedUp)
        {
            return;
        }

        // A focus-loss phone transition can bypass the Harmony close callback. Re-read the native
        // MapApp state immediately on return; LateUpdate keeps reconciling it afterward.
        SynchronizeMapAppState();
        UpdateCanvasVisibility();
    }

    private void UpdateHintPlacementSafely()
    {
        try
        {
            UpdateHintPlacement();
        }
        catch
        {
            // HUD objects are rebuilt during scene transitions. Never let a transient or
            // unsupported IL2CPP UI call escape LateUpdate and flood the MelonLoader log.
            hintDisplayInstance = null;
            try
            {
                RestoreHintPlacement();
            }
            catch
            {
                hintPlacementShifted = false;
                shiftedHintContainer = null;
            }
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void SynchronizeMapAppState()
    {
        try
        {
            if (mapAppInstance == null)
            {
                return;
            }

            bool nativeMapOpen = mapAppInstance.opened;
            if (nativeMapOpen && PlayerSingleton<Phone>.InstanceExists)
            {
                Phone phone = PlayerSingleton<Phone>.Instance;
                nativeMapOpen = phone != null && phone.IsOpen;
            }
            if (nativeMapOpen == mapAppOpen)
            {
                return;
            }

            if (nativeMapOpen)
            {
                mapAppOpen = true;
                return;
            }

            CompleteMapAppClose();
        }
        catch
        {
            // Native wrappers can be replaced during a scene or network transition. Keep the last
            // confirmed state until the next frame instead of forcing the HUD into the wrong state.
        }
    }

    private void UpdateCanvasVisibility()
    {
        if (canvasGo == null)
        {
            return;
        }

        bool shouldBeActive = isMinimapEnabled && !mapAppOpen && Time.timeScale != 0f;
        if (canvasGo.activeSelf != shouldBeActive)
        {
            canvasGo.SetActive(shouldBeActive);
        }
    }

    private void UpdateHintPlacement()
    {
        HintDisplay hintDisplay = hintDisplayInstance;
        if (hintDisplay == null)
        {
            try
            {
                hintDisplay = Singleton<HintDisplay>.Instance;
                hintDisplayInstance = hintDisplay;
            }
            catch
            {
                // The HUD singleton may not exist until the playable scene is fully initialized.
            }
        }

        if (hintDisplay == null)
        {
            RestoreHintPlacement();
            return;
        }

        RectTransform currentHintContainer;
        try
        {
            currentHintContainer = hintDisplay?.Container;
        }
        catch
        {
            hintDisplayInstance = null;
            RestoreHintPlacement();
            return;
        }
        if (currentHintContainer != shiftedHintContainer || hintDisplay != shiftedHintDisplay)
        {
            RestoreHintPlacement();
            shiftedHintContainer = currentHintContainer;
            shiftedHintDisplay = hintDisplay;
        }

        if (currentHintContainer == null)
        {
            return;
        }

        bool minimapVisible = isMinimapEnabled && !mapAppOpen && Time.timeScale != 0f
            && canvasGo != null && canvasGo.activeInHierarchy && containerRT != null;
        if (!minimapVisible)
        {
            RestoreHintPlacement();
            return;
        }

        if (!hintPlacementShifted)
        {
            hintNativeAnchorMin = currentHintContainer.anchorMin;
            hintNativeAnchorMax = currentHintContainer.anchorMax;
            hintNativePivot = currentHintContainer.pivot;
            hintNativeAnchoredPosition = currentHintContainer.anchoredPosition;
            hintNativeOffset = hintDisplay.Offset;
        }

        containerRT.GetWorldCorners(minimapWorldCorners);
        Rect minimapRect = GetWorldRect(minimapWorldCorners);
        float localToScreenScale = Mathf.Max(0.01f, Mathf.Abs(currentHintContainer.lossyScale.x));
        bool minimapIsOnRight = minimapRect.center.x >= Screen.width * 0.5f;
        float oppositeAnchorX = minimapIsOnRight ? 0f : 1f;
        float oppositePivotX = minimapIsOnRight ? 0f : 1f;
        float horizontalMargin = (minimapIsOnRight ? HintScreenMargin : -HintScreenMargin)
            / localToScreenScale;

        hintAppliedAnchorMin = new Vector2(oppositeAnchorX, hintNativeAnchorMin.y);
        hintAppliedAnchorMax = new Vector2(oppositeAnchorX, hintNativeAnchorMax.y);
        hintAppliedPivot = new Vector2(oppositePivotX, hintNativePivot.y);
        hintAppliedAnchoredPosition = new Vector2(horizontalMargin, currentHintContainer.anchoredPosition.y);
        hintAppliedOffset = new Vector2(horizontalMargin, hintNativeOffset.y);

        // HintDisplay.Update uses its Offset field while animations/layout can also write directly
        // to Container. Force both inputs, and anchor the panel to the opposite screen edge so a
        // late layout rebuild cannot put it back over the minimap.
        if (!Approximately(hintDisplay.Offset, hintAppliedOffset))
        {
            hintDisplay.Offset = hintAppliedOffset;
        }

        if (!Approximately(currentHintContainer.anchorMin, hintAppliedAnchorMin))
        {
            currentHintContainer.anchorMin = hintAppliedAnchorMin;
        }

        if (!Approximately(currentHintContainer.anchorMax, hintAppliedAnchorMax))
        {
            currentHintContainer.anchorMax = hintAppliedAnchorMax;
        }

        if (!Approximately(currentHintContainer.pivot, hintAppliedPivot))
        {
            currentHintContainer.pivot = hintAppliedPivot;
        }

        if (!Approximately(currentHintContainer.anchoredPosition, hintAppliedAnchoredPosition))
        {
            currentHintContainer.anchoredPosition = hintAppliedAnchoredPosition;
        }

        hintPlacementShifted = true;
        if (!hintPlacementSummaryLogged)
        {
            hintPlacementSummaryLogged = true;
            string side = minimapIsOnRight ? "left" : "right";
            MelonLogger.Msg($"NugzzMinimap notification panel anchored to the screen's {side} side.");
        }
    }

    private void RestoreHintPlacement()
    {
        if (hintPlacementShifted)
        {
            try
            {
                if (shiftedHintDisplay != null)
                {
                    shiftedHintDisplay.Offset = hintNativeOffset;
                }

                if (shiftedHintContainer != null)
                {
                    shiftedHintContainer.anchorMin = hintNativeAnchorMin;
                    shiftedHintContainer.anchorMax = hintNativeAnchorMax;
                    shiftedHintContainer.pivot = hintNativePivot;
                    shiftedHintContainer.anchoredPosition = hintNativeAnchoredPosition;
                }
            }
            catch
            {
                // The native HUD may be destroyed before the mod's scene cleanup runs.
            }
        }

        hintPlacementShifted = false;
        shiftedHintContainer = null;
        shiftedHintDisplay = null;
    }

    private static Rect GetWorldRect(Vector3[] corners)
    {
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static bool Approximately(Vector2 left, Vector2 right)
    {
        return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
    }

    private void CreateGeneratedSprites()
    {
        circleSprite ??= Utils.CreateCircleSprite(256, Color.grey, 2, 2);
        squareSprite ??= Utils.CreateRoundedSquareSprite(150, 3f, Color.grey);
        solidCircleSprite ??= Utils.CreateCircleSprite(256, Color.white, 2);
        playerArrowSprite ??= Utils.CreateTriangleSprite(24, Color.white, 2);
    }

    [HideFromIl2Cpp]
    private void CreateMinimapUI(
        float minimapScaleFactor,
        bool showGameTime,
        bool isCircle,
        float positionX,
        float positionY,
        float timeDisplayPositionX,
        float timeDisplayPositionY,
        TimeDisplayColor timeDisplayColor,
        float timeDisplayBackgroundOpacity,
        int timeDisplayTextSize)
    {
        canvasGo = new GameObject("MinimapCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;

        CanvasScaler canvasScaler = canvasGo.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

        GraphicRaycaster graphicRaycaster = canvasGo.AddComponent<GraphicRaycaster>();
        graphicRaycaster.enabled = true;
        UnityEngine.Object.DontDestroyOnLoad(canvasGo);

        GameObject container = new("MinimapContainer");
        container.transform.SetParent(canvasGo.transform, false);
        containerRT = container.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.one;
        containerRT.anchorMax = Vector2.one;
        containerRT.pivot = Vector2.one;
        containerRT.anchoredPosition = new Vector2(positionX, positionY);
        containerRT.sizeDelta = new Vector2(150f * minimapScaleFactor, 150f * minimapScaleFactor);

        blackCircleBackground = new GameObject("BlackCircleBackground");
        blackCircleBackground.transform.SetParent(containerRT, false);
        RectTransform blackCircleRect = blackCircleBackground.AddComponent<RectTransform>();
        StretchToParent(blackCircleRect, -4f, 4f);
        blackCircleImage = blackCircleBackground.AddComponent<Image>();
        blackCircleImage.color = Color.black;
        blackCircleImage.raycastTarget = false;
        blackCircleBackground.SetActive(isCircle);

        GameObject border = new("MinimapBorder");
        border.transform.SetParent(containerRT, false);
        RectTransform borderRect = border.AddComponent<RectTransform>();
        StretchToParent(borderRect, -4f, 4f);
        borderImage = border.AddComponent<Image>();
        borderImage.color = Color.black;
        borderImage.raycastTarget = false;

        GameObject maskObject = new("MinimapMask");
        maskObject.transform.SetParent(containerRT, false);
        RectTransform maskRect = maskObject.AddComponent<RectTransform>();
        StretchToParent(maskRect, 0f, 0f);
        maskImage = maskObject.AddComponent<Image>();
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;
        Mask mask = maskObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        mapContentContainer = new GameObject("GameMapContent");
        mapContentContainer.transform.SetParent(maskRect, false);
        mapContentRT = mapContentContainer.AddComponent<RectTransform>();
        mapContentRT.anchorMin = new Vector2(0.5f, 0.5f);
        mapContentRT.anchorMax = new Vector2(0.5f, 0.5f);
        mapContentRT.pivot = new Vector2(0.5f, 0.5f);
        mapContentRT.sizeDelta = new Vector2(500f, 500f);
        mapContentRT.localScale = new Vector3(zoomScale, zoomScale, 1f);
        nextVisualStateSyncTime = 0f;

        CreateSewerMapLayer();

        liveBuildingLabelLayer = CreateMapLayer("NugzzMinimapBuildingLabels", mapContentContainer.transform);
        livePoiMarkerLayer = CreateMapLayer("NugzzMinimapLocations", mapContentContainer.transform);
        liveNpcMarkerLayer = CreateMapLayer("NugzzMinimapNPCs", mapContentContainer.transform);
        liveNpcMarkerLayer.SetActive(npcTrackingEnabled);
        livePoliceMarkerLayer = CreateMapLayer("NugzzMinimapPolice", mapContentContainer.transform);
        livePoliceMarkerLayer.SetActive(policeTrackingEnabled);

        playerMarkerLayer = new GameObject("NugzzMinimapPlayerMarkers");
        playerMarkerLayer.transform.SetParent(mapContentContainer.transform, false);
        RectTransform playerLayerRect = playerMarkerLayer.AddComponent<RectTransform>();
        StretchToParent(playerLayerRect, 0f, 0f);

        timeDisplayView = new GameObject("TimeDisplayView").AddComponent<TimeDisplayView>();
        timeDisplayView.transform.SetParent(transform, false);
        timeDisplayView.Initialize(
            containerRT,
            showGameTime,
            timeDisplayPositionX,
            timeDisplayPositionY,
            timeDisplayColor,
            timeDisplayBackgroundOpacity,
            timeDisplayTextSize);

        CreateToggleButtons();
    }

    [HideFromIl2Cpp]
    private void CreateToggleButtons()
    {
        if (containerRT == null)
        {
            return;
        }

        CreateCompactToggleButton(
            "DeadDropToggleButton",
            -ToggleButtonSpacing,
            PrimaryButtonRowY,
            ToggleButtonWidth,
            9,
            new Action(ToggleDeadDropsFromButton),
            out deadDropToggleButton,
            out deadDropToggleButtonImage,
            out deadDropToggleButtonText,
            out deadDropToggleAction);
        CreateCompactToggleButton(
            "LocationIconToggleButton",
            0f,
            PrimaryButtonRowY,
            ToggleButtonWidth,
            9,
            new Action(ToggleLocationIconsFromButton),
            out locationIconToggleButton,
            out locationIconToggleButtonImage,
            out locationIconToggleButtonText,
            out locationIconToggleAction);
        CreateCompactToggleButton(
            "MapTextToggleButton",
            ToggleButtonSpacing,
            PrimaryButtonRowY,
            ToggleButtonWidth,
            9,
            new Action(ToggleMapTextFromButton),
            out mapTextToggleButton,
            out mapTextToggleButtonImage,
            out mapTextToggleButtonText,
            out mapTextToggleAction);
        CreateCompactToggleButton(
            "ZoomOutButton",
            -85f,
            SecondaryButtonRowY,
            ZoomButtonWidth,
            16,
            new Action(ZoomOutFromButton),
            out zoomOutButton,
            out zoomOutButtonImage,
            out _,
            out zoomOutAction);
        CreateCompactToggleButton(
            "ZoomInButton",
            -42f,
            SecondaryButtonRowY,
            ZoomButtonWidth,
            16,
            new Action(ZoomInFromButton),
            out zoomInButton,
            out zoomInButtonImage,
            out _,
            out zoomInAction);
        CreateCompactToggleButton(
            "PlayerOrientationToggleButton",
            0f,
            SecondaryButtonRowY,
            OrientationButtonWidth,
            9,
            new Action(TogglePlayerOrientationFromButton),
            out playerOrientationToggleButton,
            out playerOrientationToggleButtonImage,
            out playerOrientationToggleButtonText,
            out playerOrientationToggleAction);

        SetButtonText(zoomOutButton, "-");
        SetButtonText(zoomInButton, "+");
        PositionZoomButton(zoomOutButton, new Vector2(0.18f, 0.12f));
        PositionZoomButton(zoomInButton, new Vector2(0.82f, 0.12f));

        UpdateDeadDropButtonVisual();
        UpdateLocationIconButtonVisual();
        UpdateMapTextButtonVisual();
        UpdateZoomButtonVisuals();
        UpdatePlayerOrientationButtonVisual();
        SetClickableControlsVisible(clickableControlsVisible);
    }

    [HideFromIl2Cpp]
    private void CreateCompactToggleButton(
        string objectName,
        float positionX,
        float positionY,
        float buttonWidth,
        int fontSize,
        Action callback,
        out Button button,
        out Image buttonImage,
        out Text buttonText,
        out UnityAction toggleAction)
    {
        GameObject buttonObject = new(objectName);
        buttonObject.transform.SetParent(containerRT, false);
        RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = new Vector2(positionX, positionY);
        buttonRect.sizeDelta = new Vector2(buttonWidth, ToggleButtonHeight);

        buttonImage = buttonObject.AddComponent<Image>();
        deadDropButtonSprite ??= Utils.CreateRoundedRectSprite(
            (int)ToggleButtonWidth,
            (int)ToggleButtonHeight,
            5f,
            Color.white);
        buttonImage.sprite = deadDropButtonSprite;
        buttonImage.color = ControlButtonColor;
        buttonImage.raycastTarget = true;

        button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.transition = Selectable.Transition.None;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        GameObject textObject = new("Label");
        textObject.transform.SetParent(buttonRect, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        StretchToParent(textRect, 1f, -1f);
        buttonText = textObject.AddComponent<Text>();
        buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        buttonText.fontSize = fontSize;
        buttonText.fontStyle = FontStyle.Bold;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.white;
        buttonText.horizontalOverflow = HorizontalWrapMode.Overflow;
        buttonText.raycastTarget = false;
        Outline outline = textObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);

        toggleAction = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityAction>(callback);
        button.onClick.AddListener(toggleAction);
        buttonObject.transform.SetAsLastSibling();
    }

    private static void SetButtonText(Button button, string value)
    {
        Text label = button?.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = value;
        }
    }

    private static void PositionZoomButton(Button button, Vector2 anchor)
    {
        RectTransform buttonRect = button?.GetComponent<RectTransform>();
        if (buttonRect == null)
        {
            return;
        }

        buttonRect.anchorMin = anchor;
        buttonRect.anchorMax = anchor;
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
    }

    [HideFromIl2Cpp]
    private void ToggleDeadDropsFromButton()
    {
        bool newVisibility = !deadDropPoisVisible;
        UpdateDeadDropVisibility(newVisibility);
        deadDropPreferenceCallback?.Invoke(newVisibility);
    }

    [HideFromIl2Cpp]
    private void ToggleLocationIconsFromButton()
    {
        bool newVisibility = !locationIconsVisible;
        UpdateLocationIconVisibility(newVisibility);
        locationIconPreferenceCallback?.Invoke(newVisibility);
    }

    [HideFromIl2Cpp]
    private void ToggleMapTextFromButton()
    {
        bool newVisibility = !mapTextVisible;
        UpdateMapTextVisibility(newVisibility);
        mapTextPreferenceCallback?.Invoke(newVisibility);
    }

    [HideFromIl2Cpp]
    private void ZoomOutFromButton()
    {
        AdjustZoom(-1);
    }

    [HideFromIl2Cpp]
    private void ZoomInFromButton()
    {
        AdjustZoom(1);
    }

    private void AdjustZoom(int change)
    {
        int newZoomLevel = Mathf.Clamp(zoomLevel + change, 1, 10);
        if (newZoomLevel == zoomLevel)
        {
            return;
        }

        UpdateZoomLevel(newZoomLevel, MapPreferences.GetMinimapZoomScale(newZoomLevel));
        zoomLevelPreferenceCallback?.Invoke(newZoomLevel);
    }

    [HideFromIl2Cpp]
    private void TogglePlayerOrientationFromButton()
    {
        bool newValue = !playerOrientedMap;
        UpdatePlayerOrientedMap(newValue);
        playerOrientationPreferenceCallback?.Invoke(newValue);
    }

    private void UpdateDeadDropButtonVisual()
    {
        UpdateToggleButtonVisual(
            deadDropToggleButtonImage,
            deadDropToggleButtonText,
            "DROPS",
            deadDropPoisVisible);
    }

    private void UpdateLocationIconButtonVisual()
    {
        UpdateToggleButtonVisual(
            locationIconToggleButtonImage,
            locationIconToggleButtonText,
            "ICONS",
            locationIconsVisible);
    }

    private void UpdateMapTextButtonVisual()
    {
        UpdateToggleButtonVisual(
            mapTextToggleButtonImage,
            mapTextToggleButtonText,
            "TEXT",
            mapTextVisible);
    }

    private void UpdateZoomButtonVisuals()
    {
        bool canZoomOut = zoomLevel > 1;
        bool canZoomIn = zoomLevel < 10;
        if (zoomOutButton != null)
        {
            zoomOutButton.interactable = canZoomOut;
        }

        if (zoomInButton != null)
        {
            zoomInButton.interactable = canZoomIn;
        }

        if (zoomOutButtonImage != null)
        {
            zoomOutButtonImage.color = canZoomOut ? ControlButtonColor : DisabledControlButtonColor;
        }

        if (zoomInButtonImage != null)
        {
            zoomInButtonImage.color = canZoomIn ? ControlButtonColor : DisabledControlButtonColor;
        }
    }

    private void UpdatePlayerOrientationButtonVisual()
    {
        UpdateToggleButtonVisual(
            playerOrientationToggleButtonImage,
            playerOrientationToggleButtonText,
            "ROTATE",
            playerOrientedMap);
    }

    private static void UpdateToggleButtonVisual(Image buttonImage, Text buttonText, string label, bool isEnabled)
    {
        if (buttonText != null)
        {
            buttonText.text = $"{label} {(isEnabled ? "ON" : "OFF")}";
        }

        if (buttonImage != null)
        {
            buttonImage.color = isEnabled ? ToggleEnabledColor : ToggleDisabledColor;
        }
    }

    private void LoadActualGameMap()
    {
        try
        {
            if (mapAppInstance == null || mapAppInstance.ContentRect == null || mapAppInstance.BackgroundImage == null)
            {
                throw new InvalidOperationException("The phone map is not initialized.");
            }

            CreateBackgroundMirror();
            mapContentRT.sizeDelta = GetMapDimensions();
            mapContentRT.localScale = new Vector3(zoomScale, zoomScale, 1f);
            liveBuildingLabelLayer?.transform.SetAsLastSibling();
            livePoiMarkerLayer?.transform.SetAsLastSibling();
            liveNpcMarkerLayer?.transform.SetAsLastSibling();
            livePoliceMarkerLayer?.transform.SetAsLastSibling();
            ApplyMapLayerOrder();
            MelonLogger.Msg(
                $"NugzzMinimap live map background ready (map sprite: " +
                $"{(mapBackgroundImage?.sprite != null ? "loaded" : "missing")}).");
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"NugzzMinimap could not load the phone map: {exception}");
        }
    }

    private void CreateMapMirror(RectTransform sourceRoot)
    {
        if (sourceRoot == null || mapContentContainer == null)
        {
            return;
        }

        if (mapMirrorRoot != null)
        {
            UnityEngine.Object.Destroy(mapMirrorRoot);
        }

        if (mapBackgroundMirror != null)
        {
            UnityEngine.Object.Destroy(mapBackgroundMirror);
        }

        if (mapLabelMirrorRoot != null)
        {
            UnityEngine.Object.Destroy(mapLabelMirrorRoot);
        }

        mapMirrorObjects.Clear();
        liveMapSourceIds.Clear();
        staleMapSourceIds.Clear();
        mapSourceRoot = sourceRoot;
        mapLabelSourceRoot = null;
        mapLabelMirrorRoot = null;
        CreateBackgroundMirror();
        mapMirrorRoot = CreateVisualMirrorHierarchy(
            sourceRoot,
            mapContentContainer.transform,
            "NugzzMinimapPoIMirror");

        RectTransform mirrorRect = mapMirrorRoot.GetComponent<RectTransform>();
        if (mirrorRect == null)
        {
            throw new InvalidOperationException("The reconstructed phone map does not have a RectTransform.");
        }

        mirrorRect.anchorMin = new Vector2(0.5f, 0.5f);
        mirrorRect.anchorMax = new Vector2(0.5f, 0.5f);
        mirrorRect.pivot = sourceRoot.pivot;
        mirrorRect.anchoredPosition = Vector2.zero;
        mirrorRect.localScale = Vector3.one;
        mirrorRect.localRotation = Quaternion.identity;
        mirrorRect.sizeDelta = sourceRoot.sizeDelta;
        CreateLabelMirror(sourceRoot);
        mapMirrorRoot.transform.SetAsLastSibling();
        ApplyMapLayerOrder();
        mapContentRT.sizeDelta = GetMapDimensions();
        mapContentRT.localScale = new Vector3(zoomScale, zoomScale, 1f);
        nextVisualStateSyncTime = 0f;
        int mirroredImageCount = mapMirrorRoot.GetComponentsInChildren<Image>(true).Length;
        int mirroredTextCount = mapMirrorRoot.GetComponentsInChildren<Text>(true).Length;
        if (mapLabelMirrorRoot != null)
        {
            mirroredImageCount += mapLabelMirrorRoot.GetComponentsInChildren<Image>(true).Length;
            mirroredTextCount += mapLabelMirrorRoot.GetComponentsInChildren<Text>(true).Length;
        }

        MelonLogger.Msg(
            $"NugzzMinimap live map mirror ready ({mapMirrorObjects.Count} visual nodes; " +
            $"{mirroredImageCount} images; {mirroredTextCount} text labels; " +
            $"building labels: {(mapLabelMirrorRoot != null ? "loaded" : "included with POIs")}; " +
            $"map sprite: {(mapBackgroundImage?.sprite != null ? "loaded" : "missing")}).");
    }

    private void CreateLabelMirror(RectTransform poiSourceRoot)
    {
        Transform labelSource = GetIndependentLabelSource(poiSourceRoot);
        if (labelSource == null)
        {
            return;
        }

        mapLabelSourceRoot = labelSource;
        mapLabelMirrorRoot = CreateVisualMirrorHierarchy(
            labelSource,
            mapContentContainer.transform,
            "NugzzMinimapBuildingLabels");

        RectTransform sourceRect = labelSource as RectTransform;
        RectTransform mirrorRect = mapLabelMirrorRoot.GetComponent<RectTransform>();
        if (sourceRect != null && mirrorRect != null)
        {
            mirrorRect.anchorMin = new Vector2(0.5f, 0.5f);
            mirrorRect.anchorMax = new Vector2(0.5f, 0.5f);
            mirrorRect.pivot = sourceRect.pivot;
            mirrorRect.anchoredPosition = Vector2.zero;
            mirrorRect.localScale = Vector3.one;
            mirrorRect.localRotation = Quaternion.identity;
            mirrorRect.sizeDelta = sourceRect.sizeDelta;
        }

        mapLabelMirrorRoot.SetActive(true);
    }

    private Transform GetIndependentLabelSource(RectTransform poiSourceRoot)
    {
        Transform labelSource = mapAppInstance?.LabelGroup?.transform;
        if (labelSource == null || poiSourceRoot == null || labelSource == poiSourceRoot)
        {
            return null;
        }

        if (labelSource.IsChildOf(poiSourceRoot) || poiSourceRoot.IsChildOf(labelSource))
        {
            return null;
        }

        return labelSource;
    }

    private void CreateBackgroundMirror()
    {
        mapBackgroundMirror = new GameObject("NugzzMinimapMapBackground");
        mapBackgroundMirror.transform.SetParent(mapContentContainer.transform, false);
        RectTransform backgroundRect = mapBackgroundMirror.AddComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
        backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
        backgroundRect.pivot = new Vector2(0.5f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.sizeDelta = GetMapDimensions();

        mapBackgroundImage = mapBackgroundMirror.AddComponent<Image>();
        mapBackgroundImage.preserveAspect = true;
        mapBackgroundImage.raycastTarget = false;
        mapBackgroundMirror.transform.SetAsFirstSibling();
        SynchronizeBackgroundMirror();
    }

    private void CreateSewerMapLayer()
    {
        if (mapContentContainer == null || sewerMapLayer != null)
        {
            return;
        }

        try
        {
            using Stream stream = typeof(MinimapView).Assembly.GetManifestResourceStream(SewerMapResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded resource '{SewerMapResourceName}' was not found.");
            }

            using BinaryReader reader = new(stream);
            byte[] pngBytes = reader.ReadBytes(checked((int)stream.Length));
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false)
            {
                name = "NugzzMinimapSewerMapTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Il2CppStructArray<byte> nativeBytes = new(pngBytes);
            if (!texture.LoadImage(nativeBytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidOperationException("Unity could not decode the embedded sewer map PNG.");
            }

            sewerMapSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sewerMapSprite.name = "NugzzMinimapSewerMapSprite";

            sewerMapLayer = new GameObject("NugzzMinimapSewerLayer");
            sewerMapLayer.transform.SetParent(mapContentContainer.transform, false);
            RectTransform sewerRect = sewerMapLayer.AddComponent<RectTransform>();
            sewerRect.anchorMin = new Vector2(0.5f, 0.5f);
            sewerRect.anchorMax = new Vector2(0.5f, 0.5f);
            sewerRect.pivot = new Vector2(0.5f, 0.5f);

            sewerMapImage = sewerMapLayer.AddComponent<Image>();
            sewerMapImage.sprite = sewerMapSprite;
            sewerMapImage.color = Color.white;
            sewerMapImage.material = null;
            sewerMapImage.preserveAspect = false;
            sewerMapImage.raycastTarget = false;
            UpdateSewerMapRect();
            sewerMapLayer.SetActive(false);
            MelonLogger.Msg("NugzzMinimap calibrated sewer map layer loaded.");
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"NugzzMinimap could not load the sewer map layer: {exception}");
            if (sewerMapLayer != null)
            {
                UnityEngine.Object.Destroy(sewerMapLayer);
                sewerMapLayer = null;
                sewerMapImage = null;
            }

            DestroyGeneratedSprite(ref sewerMapSprite);
        }
    }

    private void UpdateSewerMapRect()
    {
        RectTransform sewerRect = sewerMapLayer?.GetComponent<RectTransform>();
        if (sewerRect == null)
        {
            return;
        }

        Vector2 mapDimensions = GetMapDimensions();
        Vector2 targetSize = new(
            mapDimensions.x * SewerMapNormalizedWidth,
            mapDimensions.y * SewerMapNormalizedHeight);
        Vector2 targetPosition = new(
            mapDimensions.x * SewerMapNormalizedCenterX,
            mapDimensions.y * SewerMapNormalizedCenterY);
        Quaternion targetRotation = Quaternion.Euler(0f, 0f, SewerMapRotation);
        if (sewerRect.sizeDelta != targetSize)
        {
            sewerRect.sizeDelta = targetSize;
        }

        if (sewerRect.anchoredPosition != targetPosition)
        {
            sewerRect.anchoredPosition = targetPosition;
        }

        if (sewerRect.localScale != Vector3.one)
        {
            sewerRect.localScale = Vector3.one;
        }

        if (sewerRect.localRotation != targetRotation)
        {
            sewerRect.localRotation = targetRotation;
        }
    }

    private void ApplyMapLayerOrder()
    {
        mapBackgroundMirror?.transform.SetAsFirstSibling();
        // While active, the sewer crop covers every surface-only label/icon. Multiplayer/player
        // arrows remain above it, preserving live tracking without mixing in surface POIs.
        sewerMapLayer?.transform.SetAsLastSibling();
        playerMarkerLayer?.transform.SetAsLastSibling();
    }

    private void SynchronizeSewerMapState(Player player)
    {
        if (player == null || Time.unscaledTime < nextSewerStateSyncTime)
        {
            return;
        }

        nextSewerStateSyncTime = Time.unscaledTime + SewerStateSyncInterval;
        try
        {
            SewerCameraPresense sewerPresence = Singleton<SewerCameraPresense>.Instance;
            if (sewerPresence == null)
            {
                return;
            }

            // The game's sewer controller already performs its volume tests in LateUpdate. Read its
            // cached result instead of running the complete collider/volume query a second time.
            SetSewerMapActive(sewerPresence._CameraPresenceInSewerArea_k__BackingField >= 0.5f);
        }
        catch
        {
            // Retain the last confirmed layer during transient scene/network wrapper replacement.
        }
    }

    private void SetSewerMapActive(bool isActive)
    {
        bool changed = !sewerStateInitialized || isSewerMapActive != isActive;
        sewerStateInitialized = true;
        isSewerMapActive = isActive;
        if (sewerMapLayer != null && sewerMapLayer.activeSelf != isActive)
        {
            sewerMapLayer.SetActive(isActive);
        }

        if (!changed)
        {
            return;
        }

        ApplyMapLayerOrder();
        if (isActive)
        {
            MelonLogger.Msg("NugzzMinimap switched to the sewer map layer.");
        }
    }

    private void SynchronizeBackgroundMirror()
    {
        Image sourceImage = mapAppInstance?.BackgroundImage;
        if (sourceImage == null || mapBackgroundImage == null)
        {
            return;
        }

        Sprite sourceSprite = sourceImage.sprite ?? mapAppInstance.MainMapSprite;
        if (!mapBackgroundImage.enabled)
        {
            mapBackgroundImage.enabled = true;
        }

        if (mapBackgroundImage.sprite != sourceSprite)
        {
            mapBackgroundImage.sprite = sourceSprite;
        }

        if (mapBackgroundImage.overrideSprite != sourceImage.overrideSprite)
        {
            mapBackgroundImage.overrideSprite = sourceImage.overrideSprite;
        }

        // Use the default UI material and an opaque white tint so closed-phone fade/stencil state
        // cannot turn the standalone HUD copy black or transparent.
        if (mapBackgroundImage.material != null)
        {
            mapBackgroundImage.material = null;
        }

        if (mapBackgroundImage.type != sourceImage.type)
        {
            mapBackgroundImage.type = sourceImage.type;
        }

        if (!mapBackgroundImage.preserveAspect)
        {
            mapBackgroundImage.preserveAspect = true;
        }

        if (mapBackgroundImage.color != Color.white)
        {
            mapBackgroundImage.color = Color.white;
        }

        if (mapBackgroundImage.raycastTarget)
        {
            mapBackgroundImage.raycastTarget = false;
        }

        RectTransform backgroundRect = mapBackgroundMirror.GetComponent<RectTransform>();
        if (backgroundRect != null)
        {
            Vector2 targetSize = GetMapDimensions();
            if (backgroundRect.sizeDelta != targetSize)
            {
                backgroundRect.sizeDelta = targetSize;
            }
        }

        UpdateSewerMapRect();
    }

    private Vector2 GetMapDimensions()
    {
        if (mapAppInstance?.BackgroundImage?.rectTransform != null)
        {
            Vector2 backgroundSize = mapAppInstance.BackgroundImage.rectTransform.sizeDelta;
            if (backgroundSize.x > 0f && backgroundSize.y > 0f)
            {
                return backgroundSize;
            }
        }

        if (mapAppInstance?.ContentRect != null)
        {
            return mapAppInstance.ContentRect.sizeDelta;
        }

        return new Vector2(500f, 500f);
    }

    private void SynchronizeMapMirror()
    {
        RectTransform currentSourceRoot = mapAppInstance?.PoIContainer;
        if (currentSourceRoot == null || mapContentRT == null)
        {
            return;
        }

        Transform currentLabelSource = GetIndependentLabelSource(currentSourceRoot);
        if (mapMirrorRoot == null || mapSourceRoot != currentSourceRoot || mapLabelSourceRoot != currentLabelSource)
        {
            CreateMapMirror(currentSourceRoot);
        }

        if (mapMirrorRoot == null)
        {
            return;
        }

        liveMapSourceIds.Clear();
        bool synchronizeVisualState = Time.unscaledTime >= nextVisualStateSyncTime;
        if (synchronizeVisualState)
        {
            nextVisualStateSyncTime = Time.unscaledTime + VisualStateSyncInterval;
            SynchronizeBackgroundMirror();
        }

        SynchronizeMirrorNode(currentSourceRoot, mapMirrorRoot.transform, true, synchronizeVisualState);
        if (mapLabelSourceRoot != null && mapLabelMirrorRoot != null)
        {
            SynchronizeMirrorNode(mapLabelSourceRoot, mapLabelMirrorRoot.transform, true, synchronizeVisualState);
        }

        staleMapSourceIds.Clear();
        foreach (KeyValuePair<int, GameObject> pair in mapMirrorObjects)
        {
            if (!liveMapSourceIds.Contains(pair.Key))
            {
                staleMapSourceIds.Add(pair.Key);
            }
        }

        foreach (int sourceId in staleMapSourceIds)
        {
            if (mapMirrorObjects.TryGetValue(sourceId, out GameObject staleMirror) && staleMirror != null)
            {
                UnityEngine.Object.Destroy(staleMirror);
            }

            mapMirrorObjects.Remove(sourceId);
        }

        mapContentRT.sizeDelta = GetMapDimensions();
    }

    private void SynchronizeMirrorNode(Transform source, Transform mirror, bool isRoot, bool synchronizeVisualState)
    {
        if (source == null || mirror == null)
        {
            return;
        }

        int sourceId = source.gameObject.GetInstanceID();
        liveMapSourceIds.Add(sourceId);
        mapMirrorObjects[sourceId] = mirror.gameObject;

        if (!isRoot)
        {
            CopyLocalTransform(source, mirror);
            int sourceSiblingIndex = source.GetSiblingIndex();
            if (mirror.GetSiblingIndex() != sourceSiblingIndex)
            {
                mirror.SetSiblingIndex(sourceSiblingIndex);
            }
        }

        Transform labelGroupTransform = mapAppInstance?.LabelGroup?.transform;
        bool isBuildingLabel = labelGroupTransform != null
            && (source == labelGroupTransform || source.IsChildOf(labelGroupTransform));
        bool forceVisible = isRoot || isBuildingLabel;
        bool shouldBeActive = forceVisible || source.gameObject.activeSelf;
        if (mirror.gameObject.activeSelf != shouldBeActive)
        {
            // The phone app may deactivate its content root while closed; the HUD mirror stays visible.
            mirror.gameObject.SetActive(shouldBeActive);
        }

        if (synchronizeVisualState)
        {
            CopyMutableVisualState(source.gameObject, mirror.gameObject, forceVisible);
        }

        int childCount = source.childCount;
        for (int index = 0; index < childCount; index++)
        {
            Transform sourceChild = source.GetChild(index);
            int childSourceId = sourceChild.gameObject.GetInstanceID();
            if (!mapMirrorObjects.TryGetValue(childSourceId, out GameObject childMirrorObject) || childMirrorObject == null)
            {
                childMirrorObject = CreateVisualMirrorHierarchy(sourceChild, mirror, sourceChild.gameObject.name);
            }
            else if (childMirrorObject.transform.parent != mirror)
            {
                childMirrorObject.transform.SetParent(mirror, false);
            }

            SynchronizeMirrorNode(sourceChild, childMirrorObject.transform, false, synchronizeVisualState);
        }
    }

    private GameObject CreateVisualMirrorHierarchy(Transform source, Transform mirrorParent, string mirrorName)
    {
        if (source == null || mirrorParent == null)
        {
            return null;
        }

        GameObject mirror = new(string.IsNullOrWhiteSpace(mirrorName) ? source.gameObject.name : mirrorName);
        if (source is RectTransform)
        {
            mirror.AddComponent<RectTransform>();
        }

        mirror.transform.SetParent(mirrorParent, false);
        CopyLocalTransform(source, mirror.transform);
        CreateVisualComponents(source.gameObject, mirror);
        mirror.SetActive(source.gameObject.activeSelf);
        mapMirrorObjects[source.gameObject.GetInstanceID()] = mirror;

        int childCount = source.childCount;
        for (int index = 0; index < childCount; index++)
        {
            Transform sourceChild = source.GetChild(index);
            CreateVisualMirrorHierarchy(sourceChild, mirror.transform, sourceChild.gameObject.name);
        }

        return mirror;
    }

    private void CreateVisualComponents(GameObject source, GameObject mirror)
    {
        if (source.GetComponent<Image>() != null)
        {
            mirror.AddComponent<Image>();
        }
        else if (source.GetComponent<RawImage>() != null)
        {
            mirror.AddComponent<RawImage>();
        }
        else if (source.GetComponent<Text>() != null)
        {
            mirror.AddComponent<Text>();
        }

        if (source.GetComponent<CanvasGroup>() != null)
        {
            mirror.AddComponent<CanvasGroup>();
        }

        Mask sourceMask = source.GetComponent<Mask>();
        if (sourceMask != null && mirror.GetComponent<Graphic>() != null)
        {
            Mask mirrorMask = mirror.AddComponent<Mask>();
            mirrorMask.showMaskGraphic = sourceMask.showMaskGraphic;
        }

        if (source.GetComponent<RectMask2D>() != null)
        {
            mirror.AddComponent<RectMask2D>();
        }

        Outline sourceOutline = source.GetComponent<Outline>();
        if (sourceOutline != null && mirror.GetComponent<Graphic>() != null)
        {
            Outline mirrorOutline = mirror.AddComponent<Outline>();
            mirrorOutline.effectColor = sourceOutline.effectColor;
            mirrorOutline.effectDistance = sourceOutline.effectDistance;
            mirrorOutline.useGraphicAlpha = sourceOutline.useGraphicAlpha;
        }
        else
        {
            Shadow sourceShadow = source.GetComponent<Shadow>();
            if (sourceShadow != null && mirror.GetComponent<Graphic>() != null)
            {
                Shadow mirrorShadow = mirror.AddComponent<Shadow>();
                mirrorShadow.effectColor = sourceShadow.effectColor;
                mirrorShadow.effectDistance = sourceShadow.effectDistance;
                mirrorShadow.useGraphicAlpha = sourceShadow.useGraphicAlpha;
            }
        }

        CopyMutableVisualState(source, mirror, false);
    }

    private static void CopyLocalTransform(Transform source, Transform mirror)
    {
        RectTransform sourceRect = source as RectTransform;
        RectTransform mirrorRect = mirror as RectTransform;
        if (sourceRect != null && mirrorRect != null)
        {
            if (mirrorRect.anchorMin != sourceRect.anchorMin)
            {
                mirrorRect.anchorMin = sourceRect.anchorMin;
            }

            if (mirrorRect.anchorMax != sourceRect.anchorMax)
            {
                mirrorRect.anchorMax = sourceRect.anchorMax;
            }

            if (mirrorRect.pivot != sourceRect.pivot)
            {
                mirrorRect.pivot = sourceRect.pivot;
            }

            if (mirrorRect.anchoredPosition != sourceRect.anchoredPosition)
            {
                mirrorRect.anchoredPosition = sourceRect.anchoredPosition;
            }

            if (mirrorRect.sizeDelta != sourceRect.sizeDelta)
            {
                mirrorRect.sizeDelta = sourceRect.sizeDelta;
            }

            if (mirrorRect.offsetMin != sourceRect.offsetMin)
            {
                mirrorRect.offsetMin = sourceRect.offsetMin;
            }

            if (mirrorRect.offsetMax != sourceRect.offsetMax)
            {
                mirrorRect.offsetMax = sourceRect.offsetMax;
            }
        }
        else if (mirror.localPosition != source.localPosition)
        {
            mirror.localPosition = source.localPosition;
        }

        if (mirror.localRotation != source.localRotation)
        {
            mirror.localRotation = source.localRotation;
        }

        if (mirror.localScale != source.localScale)
        {
            mirror.localScale = source.localScale;
        }
    }

    private void CopyMutableVisualState(GameObject source, GameObject mirror, bool forceVisible)
    {
        Image sourceImage = source.GetComponent<Image>();
        Image mirrorImage = mirror.GetComponent<Image>();
        if (sourceImage != null && mirrorImage != null)
        {
            mirrorImage.enabled = forceVisible || sourceImage.enabled;
            mirrorImage.sprite = sourceImage.sprite;
            mirrorImage.overrideSprite = sourceImage.overrideSprite;
            mirrorImage.color = sourceImage.color;
            mirrorImage.material = null;
            mirrorImage.type = sourceImage.type;
            mirrorImage.preserveAspect = sourceImage.preserveAspect;
            mirrorImage.fillCenter = sourceImage.fillCenter;
            mirrorImage.fillMethod = sourceImage.fillMethod;
            mirrorImage.fillAmount = sourceImage.fillAmount;
            mirrorImage.fillClockwise = sourceImage.fillClockwise;
            mirrorImage.fillOrigin = sourceImage.fillOrigin;
            mirrorImage.raycastTarget = false;
            ForceCanvasRendererVisible(mirrorImage.canvasRenderer);
        }

        RawImage sourceRawImage = source.GetComponent<RawImage>();
        RawImage mirrorRawImage = mirror.GetComponent<RawImage>();
        if (sourceRawImage != null && mirrorRawImage != null)
        {
            mirrorRawImage.enabled = forceVisible || sourceRawImage.enabled;
            mirrorRawImage.texture = sourceRawImage.texture;
            mirrorRawImage.uvRect = sourceRawImage.uvRect;
            mirrorRawImage.color = sourceRawImage.color;
            mirrorRawImage.material = null;
            mirrorRawImage.raycastTarget = false;
            ForceCanvasRendererVisible(mirrorRawImage.canvasRenderer);
        }

        Text sourceText = source.GetComponent<Text>();
        Text mirrorText = mirror.GetComponent<Text>();
        if (sourceText != null && mirrorText != null)
        {
            mirrorText.enabled = mapTextVisible && (forceVisible || sourceText.enabled);
            mirrorText.text = sourceText.text;
            mirrorText.font = sourceText.font;
            mirrorText.fontSize = sourceText.fontSize;
            mirrorText.fontStyle = sourceText.fontStyle;
            mirrorText.alignment = sourceText.alignment;
            mirrorText.color = sourceText.color;
            mirrorText.material = null;
            mirrorText.horizontalOverflow = sourceText.horizontalOverflow;
            mirrorText.verticalOverflow = sourceText.verticalOverflow;
            mirrorText.lineSpacing = sourceText.lineSpacing;
            mirrorText.supportRichText = sourceText.supportRichText;
            mirrorText.raycastTarget = false;
            ForceCanvasRendererVisible(mirrorText.canvasRenderer);
        }

        CanvasGroup sourceCanvasGroup = source.GetComponent<CanvasGroup>();
        CanvasGroup mirrorCanvasGroup = mirror.GetComponent<CanvasGroup>();
        if (sourceCanvasGroup != null && mirrorCanvasGroup != null)
        {
            mirrorCanvasGroup.alpha = 1f;
            mirrorCanvasGroup.interactable = false;
            mirrorCanvasGroup.blocksRaycasts = false;
            mirrorCanvasGroup.ignoreParentGroups = sourceCanvasGroup.ignoreParentGroups;
        }
    }

    private static void ForceCanvasRendererVisible(CanvasRenderer canvasRenderer)
    {
        if (canvasRenderer == null)
        {
            return;
        }

        canvasRenderer.cull = false;
        canvasRenderer.SetAlpha(1f);
    }

    private void CenterMapOnPlayer()
    {
        try
        {
            if (playerTransform == null || mapContentRT == null)
            {
                return;
            }

            MapPositionUtility mapUtility = Singleton<MapPositionUtility>.Instance;
            if (mapUtility != null)
            {
                Vector2 mapPosition = mapUtility.GetMapPosition(playerTransform.position);
                currentMapRotation = playerOrientedMap ? playerTransform.eulerAngles.y : 0f;
                Quaternion contentRotation = Quaternion.Euler(0f, 0f, currentMapRotation);
                if (mapContentRT.localRotation != contentRotation)
                {
                    mapContentRT.localRotation = contentRotation;
                }

                // Rotation changes the direction of the scaled player offset. Translate by the
                // rotated value so the tracked player remains exactly at the mask center.
                Vector3 centeredOffset = contentRotation
                    * new Vector3(mapPosition.x * zoomScale, mapPosition.y * zoomScale, 0f);
                Vector2 targetPosition = new(-centeredOffset.x, -centeredOffset.y);
                if (mapContentRT.anchoredPosition != targetPosition)
                {
                    mapContentRT.anchoredPosition = targetPosition;
                }
            }
        }
        catch
        {
            // Game objects can be collected between the Unity null check and the IL2CPP call during scene changes.
        }
    }

    private void UpdateUprightMarkerRotations()
    {
        Quaternion uprightRotation = Quaternion.Euler(0f, 0f, -currentMapRotation);
        foreach (KeyValuePair<int, RectTransform> pair in liveBuildingLabelRects)
        {
            RectTransform labelRect = pair.Value;
            if (labelRect != null)
            {
                Quaternion baseRotation = liveBuildingLabelBaseRotations.TryGetValue(pair.Key, out Quaternion rotation)
                    ? rotation
                    : Quaternion.identity;
                Quaternion targetRotation = uprightRotation * baseRotation;
                if (labelRect.localRotation != targetRotation)
                {
                    labelRect.localRotation = targetRotation;
                }
            }
        }

        foreach (RectTransform markerRect in livePoiMarkerRects.Values)
        {
            if (markerRect != null)
            {
                if (markerRect.localRotation != uprightRotation)
                {
                    markerRect.localRotation = uprightRotation;
                }
            }
        }

        foreach (RectTransform markerRect in liveNpcMarkerRects.Values)
        {
            if (markerRect != null)
            {
                if (markerRect.localRotation != uprightRotation)
                {
                    markerRect.localRotation = uprightRotation;
                }
            }
        }

        foreach (RectTransform markerRect in livePoliceMarkerRects.Values)
        {
            if (markerRect != null)
            {
                if (markerRect.localRotation != uprightRotation)
                {
                    markerRect.localRotation = uprightRotation;
                }
            }
        }

        foreach (Text playerLabel in playerMarkerLabels.Values)
        {
            if (playerLabel != null)
            {
                RectTransform labelRect = playerLabel.rectTransform;
                if (labelRect.localRotation != uprightRotation)
                {
                    labelRect.localRotation = uprightRotation;
                }
            }
        }
    }

    private void UpdatePlayerMarkers()
    {
        if (playerMarkerLayer == null)
        {
            return;
        }

        MapPositionUtility mapUtility = Singleton<MapPositionUtility>.Instance;
        Il2CppSystem.Collections.Generic.List<Player> players = Player.PlayerList;
        if (mapUtility == null || players == null)
        {
            return;
        }

        livePlayerIds.Clear();
        bool updatePlayerNames = Time.unscaledTime >= nextPlayerNameSyncTime;
        if (updatePlayerNames)
        {
            nextPlayerNameSyncTime = Time.unscaledTime + PlayerNameSyncInterval;
        }

        try
        {
            for (int index = 0; index < players.Count; index++)
            {
                Player lobbyPlayer = players[index];
                if (lobbyPlayer == null || lobbyPlayer.gameObject == null || !lobbyPlayer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int playerId = lobbyPlayer.gameObject.GetInstanceID();
                livePlayerIds.Add(playerId);
                EnsurePlayerMarker(playerId, lobbyPlayer);

                Transform trackedTransform = lobbyPlayer.IsInVehicle && lobbyPlayer.CurrentVehicle != null
                    ? lobbyPlayer.CurrentVehicle.transform
                    : lobbyPlayer.transform;
                if (trackedTransform == null || !playerMarkerRects.TryGetValue(playerId, out RectTransform markerRect))
                {
                    continue;
                }

                Vector2 markerPosition = mapUtility.GetMapPosition(trackedTransform.position);
                if (markerRect.anchoredPosition != markerPosition)
                {
                    markerRect.anchoredPosition = markerPosition;
                }

                if (playerMarkerImages.TryGetValue(playerId, out Image markerImage) && markerImage != null)
                {
                    Quaternion markerRotation = Quaternion.Euler(0f, 0f, -trackedTransform.eulerAngles.y);
                    if (markerImage.rectTransform.localRotation != markerRotation)
                    {
                        markerImage.rectTransform.localRotation = markerRotation;
                    }
                }

                if (playerMarkerLabels.TryGetValue(playerId, out Text markerLabel) && markerLabel != null)
                {
                    if (markerLabel.enabled != mapTextVisible)
                    {
                        markerLabel.enabled = mapTextVisible;
                    }

                    if (updatePlayerNames || !playerMarkerSourceNames.ContainsKey(playerId))
                    {
                        string sourceName = lobbyPlayer.PlayerName;
                        string playerName = string.IsNullOrWhiteSpace(sourceName) ? "Player" : sourceName;
                        if (!playerMarkerSourceNames.TryGetValue(playerId, out string cachedPlayerName)
                            || !string.Equals(cachedPlayerName, playerName, StringComparison.Ordinal))
                        {
                            markerLabel.text = lobbyPlayer == Player.Local
                                ? string.Concat(playerName, " (You)")
                                : playerName;
                            playerMarkerSourceNames[playerId] = playerName;
                        }
                    }
                }
            }
        }
        catch
        {
            // The lobby player list may change when a client joins, leaves, or switches scenes.
        }

        stalePlayerIds.Clear();
        foreach (int playerId in playerMarkers.Keys)
        {
            if (!livePlayerIds.Contains(playerId))
            {
                stalePlayerIds.Add(playerId);
            }
        }

        foreach (int playerId in stalePlayerIds)
        {
            if (playerMarkers.TryGetValue(playerId, out GameObject marker) && marker != null)
            {
                UnityEngine.Object.Destroy(marker);
            }

            playerMarkers.Remove(playerId);
            playerMarkerRects.Remove(playerId);
            playerMarkerImages.Remove(playerId);
            playerMarkerLabels.Remove(playerId);
            playerMarkerSourceNames.Remove(playerId);
        }
    }

    private void EnsurePlayerMarker(int playerId, Player lobbyPlayer)
    {
        if (playerMarkers.TryGetValue(playerId, out GameObject existingMarker) && existingMarker != null)
        {
            return;
        }

        playerMarkers.Remove(playerId);
        playerMarkerRects.Remove(playerId);
        playerMarkerImages.Remove(playerId);
        playerMarkerLabels.Remove(playerId);
        playerMarkerSourceNames.Remove(playerId);

        GameObject marker = new($"LobbyPlayerMarker_{playerId}");
        marker.transform.SetParent(playerMarkerLayer.transform, false);
        RectTransform markerRect = marker.AddComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = new Vector2(24f, 24f);

        GameObject arrow = new("Direction");
        arrow.transform.SetParent(markerRect, false);
        RectTransform arrowRect = arrow.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = Vector2.zero;
        arrowRect.sizeDelta = new Vector2(18f, 22f);
        Image arrowImage = arrow.AddComponent<Image>();
        arrowImage.sprite = playerArrowSprite;
        arrowImage.color = GetPlayerMarkerColor(playerId, lobbyPlayer == Player.Local);
        arrowImage.raycastTarget = false;
        Outline arrowOutline = arrow.AddComponent<Outline>();
        arrowOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        arrowOutline.effectDistance = new Vector2(1f, -1f);

        GameObject labelObject = new("Name");
        labelObject.transform.SetParent(markerRect, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -13f);
        labelRect.sizeDelta = new Vector2(120f, 18f);
        Text label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 11;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.enabled = mapTextVisible;
        label.raycastTarget = false;
        Outline labelOutline = labelObject.AddComponent<Outline>();
        labelOutline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        labelOutline.effectDistance = new Vector2(1f, -1f);

        playerMarkers[playerId] = marker;
        playerMarkerRects[playerId] = markerRect;
        playerMarkerImages[playerId] = arrowImage;
        playerMarkerLabels[playerId] = label;
    }

    [HideFromIl2Cpp]
    private void UpdateLiveMapMarkers()
    {
        if (liveBuildingLabelLayer == null || livePoiMarkerLayer == null
            || liveNpcMarkerLayer == null || livePoliceMarkerLayer == null)
        {
            return;
        }

        if (actorScanPhase == ActorScanPhaseIdle && Time.unscaledTime >= nextLiveMarkerScanTime)
        {
            BeginActorRegistryScan();
        }

        AdvanceActorRegistryScan();

        MapPositionUtility mapUtility = Singleton<MapPositionUtility>.Instance;
        if (mapUtility == null)
        {
            return;
        }

        foreach (KeyValuePair<int, NPC> pair in liveNpcSources)
        {
            try
            {
                NPC source = pair.Value;
                if (source != null && source.transform != null
                    && liveNpcMarkerRects.TryGetValue(pair.Key, out RectTransform markerRect))
                {
                    Vector2 markerPosition = mapUtility.GetMapPosition(source.transform.position);
                    if (markerRect.anchoredPosition != markerPosition)
                    {
                        markerRect.anchoredPosition = markerPosition;
                    }
                }
            }
            catch
            {
                // NPCs can be spawned, despawned, or moved between network scenes at any time.
            }
        }

        bool updatePoliceState = Time.unscaledTime >= nextPoliceStateSyncTime;
        if (updatePoliceState)
        {
            nextPoliceStateSyncTime = Time.unscaledTime + PoliceStateSyncInterval;
            policeFlashState = !policeFlashState;
        }

        foreach (KeyValuePair<int, PoliceOfficer> pair in livePoliceSources)
        {
            try
            {
                PoliceOfficer officer = pair.Value;
                if (officer == null || officer.transform == null)
                {
                    continue;
                }

                if (livePoliceMarkerRects.TryGetValue(pair.Key, out RectTransform markerRect))
                {
                    Vector2 markerPosition = mapUtility.GetMapPosition(officer.transform.position);
                    if (markerRect.anchoredPosition != markerPosition)
                    {
                        markerRect.anchoredPosition = markerPosition;
                    }
                }

                if (updatePoliceState
                    && livePoliceMarkerBackgrounds.TryGetValue(pair.Key, out Image background)
                    && background != null)
                {
                    Color markerColor = GetPoliceMarkerColor(officer);
                    if (background.color != markerColor)
                    {
                        background.color = markerColor;
                    }
                }
            }
            catch
            {
                // Native officer state can be collected between the Unity null check and property access.
            }
        }

    }

    [HideFromIl2Cpp]
    private void RefreshAllMarkerSources()
    {
        ScanNativeBuildingLabels();
        ScanLiveLocations();
        RefreshLiveMarkerSources();
    }

    [HideFromIl2Cpp]
    private void RefreshLiveMarkerSources()
    {
        nextLiveMarkerScanTime = 0f;
        BeginActorRegistryScan();
    }

    [HideFromIl2Cpp]
    private void LogLiveMarkerCountsIfChanged()
    {
        if (liveMarkerSummaryLogged)
        {
            return;
        }

        liveMarkerSummaryLogged = true;
        int activePotentialCustomerAreas = 0;
        foreach (Image potentialCircle in liveNpcPotentialCustomerCircles.Values)
        {
            if (potentialCircle != null && potentialCircle.enabled)
            {
                activePotentialCustomerAreas++;
            }
        }

        if (lastLoggedBuildingLabelCount != liveBuildingLabels.Count
            || lastLoggedPoiCount != livePoiSources.Count
            || lastLoggedNpcCount != liveNpcSources.Count
            || lastLoggedPoliceCount != livePoliceSources.Count
            || lastLoggedPotentialCustomerCount != activePotentialCustomerAreas)
        {
            int visibleLocationIcons = 0;
            int visibleLocationLabels = 0;
            int visibleNpcPortraits = 0;
            int visiblePolicePortraits = 0;
            int renderableBuildingLabels = 0;
            int positionedBuildingLabels = 0;
            Vector2 mapDimensions = GetMapDimensions();
            foreach (KeyValuePair<int, Text> pair in liveBuildingLabels)
            {
                Text buildingLabel = pair.Value;
                if (buildingLabel != null && buildingLabel.enabled && buildingLabel.gameObject.activeInHierarchy
                    && !string.IsNullOrWhiteSpace(buildingLabel.text)
                    && buildingLabel.rectTransform.localScale.sqrMagnitude > 0.001f)
                {
                    renderableBuildingLabels++;
                }

                if (buildingLabel != null
                    && Mathf.Abs(buildingLabel.rectTransform.anchoredPosition.x) <= mapDimensions.x * 0.5f
                    && Mathf.Abs(buildingLabel.rectTransform.anchoredPosition.y) <= mapDimensions.y * 0.5f)
                {
                    positionedBuildingLabels++;
                }
            }

            foreach (Image icon in livePoiMarkerIcons.Values)
            {
                if (icon != null && icon.sprite != null)
                {
                    visibleLocationIcons++;
                }
            }

            foreach (Text label in livePoiMarkerLabels.Values)
            {
                if (label != null && !string.IsNullOrWhiteSpace(label.text))
                {
                    visibleLocationLabels++;
                }
            }

            foreach (Image portrait in liveNpcMarkerPortraits.Values)
            {
                if (portrait != null && portrait.sprite != null)
                {
                    visibleNpcPortraits++;
                }
            }

            foreach (Image portrait in livePoliceMarkerPortraits.Values)
            {
                if (portrait != null && portrait.sprite != null)
                {
                    visiblePolicePortraits++;
                }
            }

            lastLoggedBuildingLabelCount = liveBuildingLabels.Count;
            lastLoggedPoiCount = livePoiSources.Count;
            lastLoggedNpcCount = liveNpcSources.Count;
            lastLoggedPoliceCount = livePoliceSources.Count;
            lastLoggedPotentialCustomerCount = activePotentialCustomerAreas;
            MelonLogger.Msg(
                $"NugzzMinimap live markers: {lastLoggedBuildingLabelCount} native building labels " +
                $"({renderableBuildingLabels} renderable, {positionedBuildingLabels} on-map), " +
                $"{lastLoggedPoiCount} locations " +
                $"({visibleLocationIcons} icons, {visibleLocationLabels} labels), " +
                $"{lastLoggedNpcCount} NPCs ({visibleNpcPortraits} portraits, " +
                $"{lastLoggedPotentialCustomerCount} potential-customer areas), " +
                $"{lastLoggedPoliceCount} police ({visiblePolicePortraits} portraits).");
        }
    }

    [HideFromIl2Cpp]
    private void ScanNativeBuildingLabels()
    {
        scannedBuildingLabelIds.Clear();
        try
        {
            CanvasGroup labelGroup = mapAppInstance?.LabelGroup;
            RectTransform labelCoordinateRoot = labelGroup?.GetComponent<RectTransform>();
            if (labelGroup != null && labelCoordinateRoot != null)
            {
                Text[] sourceLabels = labelGroup.GetComponentsInChildren<Text>(true);
                if (sourceLabels != null)
                {
                    foreach (Text sourceLabel in sourceLabels)
                    {
                        if (sourceLabel == null || sourceLabel.gameObject == null
                            || !IsMeaningfulMapLabel(sourceLabel.text))
                        {
                            continue;
                        }

                        int labelId = sourceLabel.gameObject.GetInstanceID();
                        scannedBuildingLabelIds.Add(labelId);
                        liveBuildingLabelSources[labelId] = sourceLabel;
                        EnsureNativeBuildingLabel(labelId);
                        UpdateNativeBuildingLabel(labelId, sourceLabel, labelCoordinateRoot);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"NugzzMinimap could not refresh native building labels: {exception.Message}");
        }

        staleLiveMarkerIds.Clear();
        foreach (int labelId in liveBuildingLabelObjects.Keys)
        {
            if (!scannedBuildingLabelIds.Contains(labelId))
            {
                staleLiveMarkerIds.Add(labelId);
            }
        }

        foreach (int labelId in staleLiveMarkerIds)
        {
            DestroyMarker(liveBuildingLabelObjects, labelId);
            liveBuildingLabelSources.Remove(labelId);
            liveBuildingLabelRects.Remove(labelId);
            liveBuildingLabelBaseRotations.Remove(labelId);
            liveBuildingLabels.Remove(labelId);
            liveBuildingLabelShadows.Remove(labelId);
        }
    }

    [HideFromIl2Cpp]
    private void RefreshCachedBuildingLabels()
    {
        RectTransform labelCoordinateRoot = mapAppInstance?.LabelGroup?.GetComponent<RectTransform>();
        if (labelCoordinateRoot == null)
        {
            return;
        }

        staleLiveMarkerIds.Clear();
        foreach (KeyValuePair<int, Text> pair in liveBuildingLabelSources)
        {
            Text sourceLabel = pair.Value;
            if (sourceLabel == null || sourceLabel.gameObject == null)
            {
                staleLiveMarkerIds.Add(pair.Key);
                continue;
            }

            UpdateNativeBuildingLabel(pair.Key, sourceLabel, labelCoordinateRoot);
        }

        foreach (int labelId in staleLiveMarkerIds)
        {
            DestroyMarker(liveBuildingLabelObjects, labelId);
            liveBuildingLabelSources.Remove(labelId);
            liveBuildingLabelRects.Remove(labelId);
            liveBuildingLabelBaseRotations.Remove(labelId);
            liveBuildingLabels.Remove(labelId);
            liveBuildingLabelShadows.Remove(labelId);
        }
    }

    [HideFromIl2Cpp]
    private void EnsureNativeBuildingLabel(int labelId)
    {
        if (liveBuildingLabelObjects.TryGetValue(labelId, out GameObject existing) && existing != null)
        {
            return;
        }

        GameObject labelObject = new($"NativeBuildingLabel_{labelId}");
        labelObject.transform.SetParent(liveBuildingLabelLayer.transform, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(labelRect, new Vector2(90f, 28f));

        Text label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 11;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.78f, 0.78f, 0.78f, 1f);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.enabled = mapTextVisible;
        label.raycastTarget = false;
        Shadow labelShadow = labelObject.AddComponent<Shadow>();
        labelShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        labelShadow.effectDistance = new Vector2(1f, -1f);
        labelShadow.useGraphicAlpha = true;

        liveBuildingLabelObjects[labelId] = labelObject;
        liveBuildingLabelRects[labelId] = labelRect;
        liveBuildingLabels[labelId] = label;
        liveBuildingLabelShadows[labelId] = labelShadow;
    }

    [HideFromIl2Cpp]
    private void UpdateNativeBuildingLabel(int labelId, Text sourceLabel, RectTransform labelCoordinateRoot)
    {
        if (sourceLabel == null || sourceLabel.rectTransform == null
            || !liveBuildingLabelRects.TryGetValue(labelId, out RectTransform targetRect)
            || !liveBuildingLabels.TryGetValue(labelId, out Text targetLabel))
        {
            return;
        }

        RectTransform sourceRect = sourceLabel.rectTransform;
        // LabelGroup is centered below the map background, while ContentRect uses a top-left pivot.
        // Compose only local transforms below LabelGroup: phone UI ancestors can have zero world
        // scale while the app is closed, which makes lossyScale/InverseTransformPoint unusable.
        Vector3 mapLocalPosition = Vector3.zero;
        Vector3 sourceRelativeScale = Vector3.one;
        Quaternion sourceRelativeRotation = Quaternion.identity;
        Transform cursor = sourceRect;
        int hierarchyDepth = 0;
        while (cursor != null && cursor != labelCoordinateRoot && hierarchyDepth++ < 16)
        {
            Vector3 cursorScale = cursor.localScale;
            mapLocalPosition = cursor.localRotation * Vector3.Scale(cursorScale, mapLocalPosition)
                + cursor.localPosition;
            sourceRelativeScale = Vector3.Scale(cursorScale, sourceRelativeScale);
            sourceRelativeRotation = cursor.localRotation * sourceRelativeRotation;
            cursor = cursor.parent;
        }

        if (cursor != labelCoordinateRoot)
        {
            mapLocalPosition = sourceRect.anchoredPosition;
            sourceRelativeScale = sourceRect.localScale;
            sourceRelativeRotation = sourceRect.localRotation;
        }

        Vector2 mapDimensions = GetMapDimensions();
        if (Mathf.Abs(mapLocalPosition.x) > mapDimensions.x * 2f
            || Mathf.Abs(mapLocalPosition.y) > mapDimensions.y * 2f)
        {
            mapLocalPosition = sourceRect.anchoredPosition;
        }

        Vector2 sourceSize = sourceRect.sizeDelta;
        if (sourceSize.x <= 1f || sourceSize.y <= 1f)
        {
            sourceSize = sourceRect.rect.size;
        }

        targetRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.pivot = sourceRect.pivot;
        targetRect.anchoredPosition = new Vector2(mapLocalPosition.x, mapLocalPosition.y);
        targetRect.sizeDelta = sourceSize;
        float zoomCompensation = NativeLabelReferenceZoom / Mathf.Max(zoomScale, 0.01f);
        targetRect.localScale = new Vector3(
            sourceRelativeScale.x * zoomCompensation,
            sourceRelativeScale.y * zoomCompensation,
            1f);
        liveBuildingLabelBaseRotations[labelId] = sourceRelativeRotation;
        Quaternion uprightRotation = Quaternion.Euler(0f, 0f, -currentMapRotation);
        targetRect.localRotation = uprightRotation * sourceRelativeRotation;

        targetLabel.text = sourceLabel.text.Trim();
        // The same built-in font used by the working POI labels avoids inheriting a phone-canvas
        // font material or stencil state from the inactive app.
        targetLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        targetLabel.fontSize = sourceLabel.fontSize;
        targetLabel.fontStyle = sourceLabel.fontStyle;
        targetLabel.alignment = sourceLabel.alignment;
        targetLabel.lineSpacing = sourceLabel.lineSpacing;
        targetLabel.supportRichText = sourceLabel.supportRichText;
        targetLabel.horizontalOverflow = sourceLabel.horizontalOverflow;
        targetLabel.verticalOverflow = sourceLabel.verticalOverflow;
        targetLabel.resizeTextForBestFit = sourceLabel.resizeTextForBestFit;
        targetLabel.resizeTextMinSize = sourceLabel.resizeTextMinSize;
        targetLabel.resizeTextMaxSize = sourceLabel.resizeTextMaxSize;
        targetLabel.color = EnsureReadableMapLabelColor(sourceLabel.color);
        targetLabel.material = null;
        targetLabel.maskable = true;
        targetLabel.enabled = mapTextVisible;
        targetLabel.raycastTarget = false;
        ForceCanvasRendererVisible(targetLabel.canvasRenderer);
        targetLabel.SetAllDirty();

        if (liveBuildingLabelShadows.TryGetValue(labelId, out Shadow targetShadow) && targetShadow != null)
        {
            Shadow sourceShadow = sourceLabel.GetComponent<Shadow>();
            targetShadow.effectColor = sourceShadow != null
                ? EnsureVisibleShadowColor(sourceShadow.effectColor)
                : new Color(0f, 0f, 0f, 0.8f);
            targetShadow.effectDistance = sourceShadow != null ? sourceShadow.effectDistance : new Vector2(1f, -1f);
            targetShadow.useGraphicAlpha = sourceShadow == null || sourceShadow.useGraphicAlpha;
            targetShadow.enabled = mapTextVisible;
        }
    }

    private static bool IsMeaningfulMapLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !string.Equals(value.Trim(), "PoI Main Text", StringComparison.OrdinalIgnoreCase);
    }

    private static Color EnsureReadableMapLabelColor(Color color)
    {
        float brightness = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        if (brightness < 0.78f)
        {
            float multiplier = brightness > 0.01f ? 0.78f / brightness : 1f;
            color.r = brightness > 0.01f ? Mathf.Clamp01(color.r * multiplier) : 0.78f;
            color.g = brightness > 0.01f ? Mathf.Clamp01(color.g * multiplier) : 0.78f;
            color.b = brightness > 0.01f ? Mathf.Clamp01(color.b * multiplier) : 0.78f;
        }

        color.a = Mathf.Max(color.a, 0.9f);
        return color;
    }

    private static Color EnsureVisibleShadowColor(Color color)
    {
        color.a = Mathf.Max(color.a, 0.75f);
        return color;
    }

    [HideFromIl2Cpp]
    private void ScanLiveLocations()
    {
        scannedPoiIds.Clear();
        try
        {
            POI[] locations = UnityEngine.Object.FindObjectsOfType<POI>();
            if (locations != null)
            {
                foreach (POI location in locations)
                {
                    if (!TryRegisterLiveLocation(location))
                    {
                        continue;
                    }

                    int locationId = location.gameObject.GetInstanceID();
                    scannedPoiIds.Add(locationId);
                }
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"NugzzMinimap could not refresh map locations: {exception.Message}");
        }

        staleLiveMarkerIds.Clear();
        foreach (int locationId in livePoiSources.Keys)
        {
            if (!scannedPoiIds.Contains(locationId))
            {
                staleLiveMarkerIds.Add(locationId);
            }
        }

        foreach (int locationId in staleLiveMarkerIds)
        {
            RemoveLiveLocation(locationId);
        }
    }

    [HideFromIl2Cpp]
    public void OnLiveLocationEnabled(POI location)
    {
        if (cleanedUp || livePoiMarkerLayer == null)
        {
            return;
        }

        TryRegisterLiveLocation(location);
        LogLiveMarkerCountsIfChanged();
    }

    [HideFromIl2Cpp]
    public void OnLiveLocationDisabled(POI location)
    {
        if (cleanedUp || location == null || location.gameObject == null)
        {
            return;
        }

        RemoveLiveLocation(location.gameObject.GetInstanceID());
        LogLiveMarkerCountsIfChanged();
    }

    [HideFromIl2Cpp]
    public void OnLiveLocationChanged(POI location)
    {
        if (cleanedUp || location == null || location.gameObject == null)
        {
            return;
        }

        int locationId = location.gameObject.GetInstanceID();
        if (!livePoiSources.ContainsKey(locationId))
        {
            TryRegisterLiveLocation(location);
            return;
        }

        UpdateLocationMarkerVisual(locationId, location);
        UpdateLocationMarkerPosition(locationId, location);
    }

    [HideFromIl2Cpp]
    private bool TryRegisterLiveLocation(POI location)
    {
        if (location == null || location.gameObject == null
            || !location.gameObject.activeInHierarchy
            || location.gameObject.GetComponent<NPCPoI>() != null
            || IsPlayerPoi(location)
            || (!deadDropPoisVisible && IsDeadDropPoi(location)))
        {
            return false;
        }

        int locationId = location.gameObject.GetInstanceID();
        livePoiSources[locationId] = location;
        EnsureLocationMarker(locationId);
        UpdateLocationMarkerVisual(locationId, location);
        UpdateLocationMarkerPosition(locationId, location);
        return true;
    }

    private void UpdateLocationMarkerPosition(int locationId, POI location)
    {
        try
        {
            MapPositionUtility mapUtility = Singleton<MapPositionUtility>.Instance;
            if (mapUtility == null || location == null || location.transform == null
                || !livePoiMarkerRects.TryGetValue(locationId, out RectTransform markerRect)
                || markerRect == null)
            {
                return;
            }

            Vector2 markerPosition = mapUtility.GetMapPosition(location.transform.position);
            if (markerRect.anchoredPosition != markerPosition)
            {
                markerRect.anchoredPosition = markerPosition;
            }
        }
        catch
        {
            // A location can be unloaded while its static map position is being sampled.
        }
    }

    private void RemoveLiveLocation(int locationId)
    {
        DestroyMarker(livePoiMarkers, locationId);
        livePoiSources.Remove(locationId);
        livePoiMarkerRects.Remove(locationId);
        livePoiMarkerBackgrounds.Remove(locationId);
        livePoiMarkerIcons.Remove(locationId);
        livePoiMarkerLabels.Remove(locationId);
    }

    private static bool IsPlayerPoi(POI location)
    {
        if (location == null)
        {
            return false;
        }

        try
        {
            Il2CppSystem.Collections.Generic.List<Player> players = Player.PlayerList;
            if (players == null)
            {
                return false;
            }

            for (int index = 0; index < players.Count; index++)
            {
                Player player = players[index];
                if (player != null && player.PoI != null && player.PoI.Pointer == location.Pointer)
                {
                    return true;
                }
            }
        }
        catch
        {
            // The lobby player list can change while locations are being scanned.
        }

        return false;
    }

    private static bool IsDeadDropPoi(POI location)
    {
        try
        {
            return location != null && location.GetComponentInParent<DeadDrop>() != null;
        }
        catch
        {
            return false;
        }
    }

    [HideFromIl2Cpp]
    private void BeginActorRegistryScan()
    {
        scannedNpcIds.Clear();
        scannedPoliceIds.Clear();
        npcRegistryScanList = null;
        officerRegistryScanList = null;
        npcRegistryReadOk = false;
        officerRegistryReadOk = false;

        try
        {
            npcRegistryScanList = NPCManager.NPCRegistry;
            npcRegistryReadOk = npcRegistryScanList != null;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"NugzzMinimap could not read the NPC registry: {exception.Message}");
        }

        try
        {
            officerRegistryScanList = PoliceOfficer.Officers;
            officerRegistryReadOk = officerRegistryScanList != null;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"NugzzMinimap could not read the police registry: {exception.Message}");
        }

        npcRegistryScanIndex = 0;
        officerRegistryScanIndex = 0;
        actorScanPhase = ActorScanPhaseNpcs;
    }

    [HideFromIl2Cpp]
    private void AdvanceActorRegistryScan()
    {
        if (actorScanPhase == ActorScanPhaseNpcs)
        {
            AdvanceNpcRegistryScan();
        }
        else if (actorScanPhase == ActorScanPhaseOfficers)
        {
            AdvanceOfficerRegistryScan();
        }
    }

    [HideFromIl2Cpp]
    private void AdvanceNpcRegistryScan()
    {
        Il2CppSystem.Collections.Generic.List<NPC> npcs = npcRegistryScanList;
        if (npcs == null)
        {
            actorScanPhase = ActorScanPhaseOfficers;
            return;
        }

        int processed = 0;
        while (npcRegistryScanIndex < npcs.Count && processed < ActorRegistryItemsPerFrame)
        {
            try
            {
                NPC npc = npcs[npcRegistryScanIndex];
                if (npc != null && npc.gameObject != null && npc.gameObject.activeInHierarchy
                    && !npc.IsLocalPlayer)
                {
                    int npcId = npc.gameObject.GetInstanceID();
                    livePoliceSources.TryGetValue(npcId, out PoliceOfficer officer);
                    if (officer == null && !liveNpcSources.ContainsKey(npcId))
                    {
                        officer = npc.gameObject.GetComponent<PoliceOfficer>();
                    }

                    if (officer != null)
                    {
                        RegisterLivePoliceOfficer(officer);
                    }
                    else
                    {
                        RegisterLiveNpc(npc);
                    }
                }
            }
            catch
            {
                // An actor can despawn while its native registry is being walked across frames.
            }

            npcRegistryScanIndex++;
            processed++;
        }

        if (npcRegistryScanIndex >= npcs.Count)
        {
            if (npcRegistryReadOk)
            {
                RemoveStaleNpcMarkers();
            }

            actorScanPhase = ActorScanPhaseOfficers;
        }
    }

    [HideFromIl2Cpp]
    private void AdvanceOfficerRegistryScan()
    {
        Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers = officerRegistryScanList;
        if (officers == null)
        {
            FinishActorRegistryScan();
            return;
        }

        int processed = 0;
        while (officerRegistryScanIndex < officers.Count && processed < ActorRegistryItemsPerFrame)
        {
            try
            {
                RegisterLivePoliceOfficer(officers[officerRegistryScanIndex]);
            }
            catch
            {
                // An officer can despawn while its native registry is being walked across frames.
            }

            officerRegistryScanIndex++;
            processed++;
        }

        if (officerRegistryScanIndex >= officers.Count)
        {
            FinishActorRegistryScan();
        }
    }

    [HideFromIl2Cpp]
    private void FinishActorRegistryScan()
    {
        if (officerRegistryReadOk)
        {
            RemoveStalePoliceMarkers();
        }

        LogLiveMarkerCountsIfChanged();
        npcRegistryScanList = null;
        officerRegistryScanList = null;
        actorScanPhase = ActorScanPhaseIdle;
        nextLiveMarkerScanTime = Time.unscaledTime + ActorRegistrySyncInterval;
    }

    [HideFromIl2Cpp]
    private void RegisterLiveNpc(NPC npc)
    {
        if (npc == null || npc.gameObject == null || !npc.gameObject.activeInHierarchy || npc.IsLocalPlayer)
        {
            return;
        }

        int npcId = npc.gameObject.GetInstanceID();
        scannedNpcIds.Add(npcId);
        liveNpcSources[npcId] = npc;
        EnsureNpcMarker(npcId);

        if (!liveCustomerSources.TryGetValue(npcId, out Customer customer))
        {
            customer = npc.gameObject.GetComponent<Customer>();
            liveCustomerSources[npcId] = customer;
        }

        if (liveNpcPotentialCustomerCircles.TryGetValue(npcId, out Image potentialCircle)
            && potentialCircle != null)
        {
            bool shouldShowPotentialArea = IsPotentialCustomer(customer);
            if (potentialCircle.enabled != shouldShowPotentialArea)
            {
                potentialCircle.enabled = shouldShowPotentialArea;
            }
        }

        if (liveNpcMarkerPortraits.TryGetValue(npcId, out Image npcPortrait) && npcPortrait != null)
        {
            if (!npcPortrait.enabled || npcPortrait.sprite == null)
            {
                npcPortrait.sprite = npc.MugshotSprite;
                npcPortrait.enabled = npcPortrait.sprite != null;
            }

            if (liveNpcMarkerBackgrounds.TryGetValue(npcId, out Image npcBackground) && npcBackground != null)
            {
                if (npcBackground.enabled != npcPortrait.enabled)
                {
                    npcBackground.enabled = npcPortrait.enabled;
                }
            }
        }
    }

    [HideFromIl2Cpp]
    private void RegisterLivePoliceOfficer(PoliceOfficer officer)
    {
        if (officer == null || officer.gameObject == null || !officer.gameObject.activeInHierarchy
            || IsOfficerDead(officer))
        {
            return;
        }

        int officerId = officer.gameObject.GetInstanceID();
        scannedPoliceIds.Add(officerId);
        livePoliceSources[officerId] = officer;
        EnsurePoliceMarker(officerId);
        if (livePoliceMarkerPortraits.TryGetValue(officerId, out Image policePortrait)
            && policePortrait != null)
        {
            if (!policePortrait.enabled || policePortrait.sprite == null)
            {
                policePortrait.sprite = officer.MugshotSprite;
                policePortrait.enabled = policePortrait.sprite != null;
            }
        }
    }

    [HideFromIl2Cpp]
    public void OnPotentialCustomerStateChanged(Customer customer)
    {
        if (cleanedUp || customer == null || customer.gameObject == null)
        {
            return;
        }

        int npcId = customer.gameObject.GetInstanceID();
        liveCustomerSources[npcId] = customer;
        if (liveNpcPotentialCustomerCircles.TryGetValue(npcId, out Image potentialCircle)
            && potentialCircle != null)
        {
            bool shouldShowPotentialArea = IsPotentialCustomer(customer);
            if (potentialCircle.enabled != shouldShowPotentialArea)
            {
                potentialCircle.enabled = shouldShowPotentialArea;
            }
        }
    }

    private static bool IsPotentialCustomer(Customer customer)
    {
        try
        {
            NPCPoI potentialCustomerPoi = customer?.potentialCustomerPoI;
            return potentialCustomerPoi != null && potentialCustomerPoi.enabled;
        }
        catch
        {
            return false;
        }
    }

    [HideFromIl2Cpp]
    private void EnsureLocationMarker(int locationId)
    {
        if (livePoiMarkers.TryGetValue(locationId, out GameObject existing) && existing != null)
        {
            return;
        }

        GameObject marker = new($"LiveLocation_{locationId}");
        marker.transform.SetParent(livePoiMarkerLayer.transform, false);
        RectTransform markerRect = marker.AddComponent<RectTransform>();
        ConfigureCenteredRect(markerRect, new Vector2(150f, 46f));

        GameObject backgroundObject = new("IconBackground");
        backgroundObject.transform.SetParent(markerRect, false);
        RectTransform backgroundRect = backgroundObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(backgroundRect, new Vector2(30f, 30f));
        Image background = backgroundObject.AddComponent<Image>();
        background.sprite = solidCircleSprite;
        background.color = new Color(0.18f, 0.5f, 0.95f, 1f);
        background.raycastTarget = false;

        GameObject iconObject = new("Icon");
        iconObject.transform.SetParent(backgroundRect, false);
        RectTransform iconRect = iconObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(iconRect, new Vector2(19f, 19f));
        Image icon = iconObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject labelObject = new("BuildingName");
        labelObject.transform.SetParent(markerRect, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(labelRect, new Vector2(150f, 22f));
        labelRect.anchoredPosition = new Vector2(0f, -20f);
        Text label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 11;
        label.fontStyle = FontStyle.Normal;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.enabled = mapTextVisible;
        label.raycastTarget = false;
        Outline labelOutline = labelObject.AddComponent<Outline>();
        labelOutline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        labelOutline.effectDistance = new Vector2(1f, -1f);

        livePoiMarkers[locationId] = marker;
        livePoiMarkerRects[locationId] = markerRect;
        livePoiMarkerBackgrounds[locationId] = background;
        livePoiMarkerIcons[locationId] = icon;
        livePoiMarkerLabels[locationId] = label;
    }

    [HideFromIl2Cpp]
    private void UpdateLocationMarkerVisual(int locationId, POI location)
    {
        if (location == null)
        {
            return;
        }

        string locationName = IsMeaningfulMapLabel(location.MainText)
            ? location.MainText.Trim()
            : IsMeaningfulMapLabel(location.DefaultMainText)
                ? location.DefaultMainText.Trim()
                : string.Empty;
        if (livePoiMarkerLabels.TryGetValue(locationId, out Text label) && label != null)
        {
            label.text = locationName ?? string.Empty;
            label.enabled = mapTextVisible && !string.IsNullOrWhiteSpace(label.text);
        }

        Sprite iconSprite = GetLocationIcon(location, out Color iconColor);
        bool hasIcon = iconSprite != null;
        bool showIcon = hasIcon && (IsDeadDropPoi(location) || locationIconsVisible);
        if (livePoiMarkerIcons.TryGetValue(locationId, out Image icon) && icon != null)
        {
            icon.sprite = iconSprite;
            icon.color = iconColor;
            icon.enabled = showIcon;
        }

        if (livePoiMarkerBackgrounds.TryGetValue(locationId, out Image background) && background != null)
        {
            background.enabled = showIcon;
        }
    }

    private static Sprite GetLocationIcon(POI location, out Color iconColor)
    {
        iconColor = Color.white;
        if (location == null)
        {
            return null;
        }

        Image[] images = null;
        if (location.IconContainer != null)
        {
            images = location.IconContainer.GetComponentsInChildren<Image>(true);
        }

        if ((images == null || images.Length == 0) && location.UI != null)
        {
            images = location.UI.GetComponentsInChildren<Image>(true);
        }

        Image fallback = null;
        if (images != null)
        {
            foreach (Image image in images)
            {
                if (image == null || image.sprite == null)
                {
                    continue;
                }

                fallback ??= image;
                if (string.Equals(image.gameObject.name, "Icon", StringComparison.OrdinalIgnoreCase)
                    || image.gameObject.name.EndsWith("Icon", StringComparison.OrdinalIgnoreCase))
                {
                    iconColor = EnsureVisibleColor(image.color);
                    return image.sprite;
                }
            }
        }

        if (fallback != null)
        {
            iconColor = EnsureVisibleColor(fallback.color);
            return fallback.sprite;
        }

        return null;
    }

    [HideFromIl2Cpp]
    private void EnsureNpcMarker(int npcId)
    {
        if (liveNpcMarkers.TryGetValue(npcId, out GameObject existing) && existing != null)
        {
            return;
        }

        GameObject marker = CreatePortraitMarker($"LiveNPC_{npcId}", liveNpcMarkerLayer.transform, Color.white);
        liveNpcMarkers[npcId] = marker;
        liveNpcMarkerRects[npcId] = marker.GetComponent<RectTransform>();
        liveNpcPotentialCustomerCircles[npcId] = marker.transform.Find("PotentialCustomerArea").GetComponent<Image>();
        Image background = marker.transform.Find("Border").GetComponent<Image>();
        background.enabled = false;
        liveNpcMarkerBackgrounds[npcId] = background;
        liveNpcMarkerPortraits[npcId] = marker.transform.Find("Portrait").GetComponent<Image>();
    }

    [HideFromIl2Cpp]
    private void EnsurePoliceMarker(int officerId)
    {
        if (livePoliceMarkers.TryGetValue(officerId, out GameObject existing) && existing != null)
        {
            return;
        }

        GameObject marker = CreatePortraitMarker($"LivePolice_{officerId}", livePoliceMarkerLayer.transform, PoliceBlue);
        livePoliceMarkers[officerId] = marker;
        livePoliceMarkerRects[officerId] = marker.GetComponent<RectTransform>();
        livePoliceMarkerBackgrounds[officerId] = marker.transform.Find("Border").GetComponent<Image>();
        livePoliceMarkerPortraits[officerId] = marker.transform.Find("Portrait").GetComponent<Image>();
    }

    [HideFromIl2Cpp]
    private GameObject CreatePortraitMarker(string markerName, Transform parent, Color borderColor)
    {
        GameObject marker = new(markerName);
        marker.transform.SetParent(parent, false);
        RectTransform markerRect = marker.AddComponent<RectTransform>();
        ConfigureCenteredRect(markerRect, new Vector2(34f, 34f));

        GameObject potentialAreaObject = new("PotentialCustomerArea");
        potentialAreaObject.transform.SetParent(markerRect, false);
        RectTransform potentialAreaRect = potentialAreaObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(potentialAreaRect, new Vector2(118f, 118f));
        Image potentialArea = potentialAreaObject.AddComponent<Image>();
        potentialArea.sprite = solidCircleSprite;
        potentialArea.color = new Color(0.55f, 0.34f, 0.68f, 0.32f);
        potentialArea.raycastTarget = false;
        potentialArea.enabled = false;

        GameObject borderObject = new("Border");
        borderObject.transform.SetParent(markerRect, false);
        RectTransform borderRect = borderObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(borderRect, new Vector2(34f, 34f));
        Image background = borderObject.AddComponent<Image>();
        background.sprite = solidCircleSprite;
        background.color = borderColor;
        background.raycastTarget = false;

        GameObject portraitObject = new("Portrait");
        portraitObject.transform.SetParent(markerRect, false);
        RectTransform portraitRect = portraitObject.AddComponent<RectTransform>();
        ConfigureCenteredRect(portraitRect, new Vector2(28f, 28f));
        Image portrait = portraitObject.AddComponent<Image>();
        portrait.preserveAspect = true;
        portrait.color = Color.white;
        portrait.raycastTarget = false;
        portrait.enabled = false;
        return marker;
    }

    [HideFromIl2Cpp]
    private void RemoveStaleNpcMarkers()
    {
        staleLiveMarkerIds.Clear();
        foreach (int npcId in liveNpcSources.Keys)
        {
            if (!scannedNpcIds.Contains(npcId))
            {
                staleLiveMarkerIds.Add(npcId);
            }
        }

        foreach (int npcId in staleLiveMarkerIds)
        {
            DestroyMarker(liveNpcMarkers, npcId);
            liveNpcSources.Remove(npcId);
            liveCustomerSources.Remove(npcId);
            liveNpcMarkerRects.Remove(npcId);
            liveNpcPotentialCustomerCircles.Remove(npcId);
            liveNpcMarkerBackgrounds.Remove(npcId);
            liveNpcMarkerPortraits.Remove(npcId);
        }
    }

    [HideFromIl2Cpp]
    private void RemoveStalePoliceMarkers()
    {
        staleLiveMarkerIds.Clear();
        foreach (int officerId in livePoliceSources.Keys)
        {
            if (!scannedPoliceIds.Contains(officerId))
            {
                staleLiveMarkerIds.Add(officerId);
            }
        }

        foreach (int officerId in staleLiveMarkerIds)
        {
            DestroyMarker(livePoliceMarkers, officerId);
            livePoliceSources.Remove(officerId);
            livePoliceMarkerRects.Remove(officerId);
            livePoliceMarkerBackgrounds.Remove(officerId);
            livePoliceMarkerPortraits.Remove(officerId);
        }
    }

    [HideFromIl2Cpp]
    private Color GetPoliceMarkerColor(PoliceOfficer officer)
    {
        if (officer == null)
        {
            return PoliceBlue;
        }

        if (officer.PursuitTarget != null)
        {
            return policeFlashState ? PoliceRed : PoliceBlue;
        }

        return officer.currentBodySearchInvestigation != null ? PoliceRed : PoliceBlue;
    }

    private static void DestroyMarker(Dictionary<int, GameObject> markers, int markerId)
    {
        if (markers.TryGetValue(markerId, out GameObject marker) && marker != null)
        {
            UnityEngine.Object.Destroy(marker);
        }

        markers.Remove(markerId);
    }

    private static Color EnsureVisibleColor(Color color)
    {
        if (color.a <= 0.05f)
        {
            color.a = 1f;
        }

        return color;
    }

    private static void ConfigureCenteredRect(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Color GetPlayerMarkerColor(int playerId, bool isLocalPlayer)
    {
        if (isLocalPlayer)
        {
            return new Color(0.2f, 1f, 0.35f, 1f);
        }

        float hue = Mathf.Abs(playerId % 360) / 360f;
        return Color.HSVToRGB(hue, 0.72f, 1f);
    }

    [HideFromIl2Cpp]
    private IEnumerator InitializeNPCTracking()
    {
        WaitForSeconds retryDelay = new(1f);
        while (benjiPoITemplate == null)
        {
            GameObject benji = GameObject.Find("Benji");
            Transform template = benji?.transform.Find("NPCPoI(Clone)");
            if (template != null)
            {
                benjiPoITemplate = template.gameObject;
                break;
            }

            yield return retryDelay;
        }

        ScanForPotentialCustomers();
        npcPoisInitialized = true;
        customerMonitorCoroutine = MelonCoroutines.Start(MonitorForNewCustomers());
        npcInitializationCoroutine = null;
    }

    [HideFromIl2Cpp]
    private IEnumerator MonitorForNewCustomers()
    {
        WaitForSeconds scanDelay = new(2f);
        while (true)
        {
            yield return scanDelay;
            if (npcPoisInitialized && benjiPoITemplate != null)
            {
                ScanForPotentialCustomers();
                RemoveDestroyedNpcPois();
            }
        }
    }

    private void ScanForPotentialCustomers()
    {
        Il2CppSystem.Collections.Generic.List<NPC> npcs = NPCManager.NPCRegistry;
        if (npcs == null)
        {
            return;
        }

        for (int index = 0; index < npcs.Count; index++)
        {
            try
            {
                NPC npc = npcs[index];
                Customer customer = npc?.gameObject?.GetComponent<Customer>();
                if (customer == null || customer.gameObject == null)
                {
                    continue;
                }

                int customerId = customer.gameObject.GetInstanceID();
                if (npcPois.TryGetValue(customerId, out GameObject existingPoi) && existingPoi != null)
                {
                    continue;
                }

                npcPois.Remove(customerId);
                GameObject poi = CreateNPCPOI(customer, npc);
                if (poi != null)
                {
                    npcPois[customerId] = poi;
                }
            }
            catch
            {
                // A customer can despawn while the IL2CPP collection is being traversed.
            }
        }
    }

    private GameObject CreateNPCPOI(Customer customer, NPC npc)
    {
        if (benjiPoITemplate == null || customer == null || npc == null)
        {
            return null;
        }

        GameObject poiObject = UnityEngine.Object.Instantiate(benjiPoITemplate);
        poiObject.name = "NPCPoI(Clone)";
        NPCPoI poi = poiObject.GetComponent<NPCPoI>();
        if (poi == null)
        {
            UnityEngine.Object.Destroy(poiObject);
            return null;
        }

        poi.SetNPC(npc);
        poi.enabled = true;
        poi.SetMainText(npc.FullName);
        poiObject.transform.SetParent(customer.transform, false);
        poiObject.SetActive(npcTrackingEnabled);
        return poiObject;
    }

    private void RemoveDestroyedNpcPois()
    {
        List<int> deadKeys = null;
        foreach (KeyValuePair<int, GameObject> pair in npcPois)
        {
            if (pair.Value != null && pair.Value.transform.parent != null)
            {
                continue;
            }

            deadKeys ??= new List<int>();
            deadKeys.Add(pair.Key);
        }

        if (deadKeys == null)
        {
            return;
        }

        foreach (int key in deadKeys)
        {
            npcPois.Remove(key);
        }
    }

    [HideFromIl2Cpp]
    private IEnumerator InitializePoliceTracking()
    {
        WaitForSeconds retryDelay = new(1f);
        while (benjiPoITemplate == null)
        {
            yield return retryDelay;
        }

        policePoisInitialized = true;
        officerMonitorCoroutine = MelonCoroutines.Start(MonitorPoliceOfficers());
        policeColorCoroutine = MelonCoroutines.Start(UpdatePoliceColors());
        policeInitializationCoroutine = null;
    }

    [HideFromIl2Cpp]
    private IEnumerator MonitorPoliceOfficers()
    {
        WaitForSeconds scanDelay = new(2f);
        while (true)
        {
            yield return scanDelay;
            if (!policePoisInitialized || benjiPoITemplate == null)
            {
                continue;
            }

            try
            {
                Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers = PoliceOfficer.Officers;
                if (officers != null)
                {
                    for (int index = 0; index < officers.Count; index++)
                    {
                        PoliceOfficer officer = officers[index];
                        if (officer == null || officer.gameObject == null || !officer.gameObject.activeInHierarchy || IsOfficerDead(officer))
                        {
                            continue;
                        }

                        int officerId = officer.gameObject.GetInstanceID();
                        if (policePois.TryGetValue(officerId, out GameObject existingPoi) && existingPoi != null)
                        {
                            continue;
                        }

                        policePois.Remove(officerId);
                        GameObject poi = CreatePolicePOI(officer);
                        if (poi != null)
                        {
                            policePois[officerId] = poi;
                        }
                    }
                }

                RemoveInactivePolicePois();
            }
            catch
            {
                // Officer collections can change while police are spawned/despawned.
            }
        }
    }

    [HideFromIl2Cpp]
    private IEnumerator UpdatePoliceColors()
    {
        WaitForSeconds updateDelay = new(0.3f);
        while (true)
        {
            yield return updateDelay;
            if (!policeTrackingEnabled || policePois.Count == 0)
            {
                continue;
            }

            policeFlashState = !policeFlashState;
            foreach (GameObject poiObject in policePois.Values)
            {
                UpdatePolicePoiColor(poiObject);
            }
        }
    }

    private GameObject CreatePolicePOI(PoliceOfficer officer)
    {
        if (benjiPoITemplate == null || officer == null)
        {
            return null;
        }

        NPC npc = officer.gameObject.GetComponent<NPC>() ?? officer.TryCast<NPC>();
        if (npc == null)
        {
            return null;
        }

        GameObject poiObject = UnityEngine.Object.Instantiate(benjiPoITemplate);
        poiObject.name = "PolicePoI(Clone)";
        NPCPoI poi = poiObject.GetComponent<NPCPoI>();
        if (poi == null)
        {
            UnityEngine.Object.Destroy(poiObject);
            return null;
        }

        poi.SetNPC(npc);
        poi.enabled = true;
        poi.SetMainText(npc.FullName);
        poiObject.transform.SetParent(officer.transform, false);
        poiObject.SetActive(policeTrackingEnabled);
        poiOfficerMap[poi.Pointer] = officer;
        return poiObject;
    }

    private void UpdatePolicePoiColor(GameObject poiObject)
    {
        if (poiObject == null)
        {
            return;
        }

        try
        {
            NPCPoI poi = poiObject.GetComponent<NPCPoI>();
            if (poi == null || !poiOfficerMap.TryGetValue(poi.Pointer, out PoliceOfficer officer)
                || officer == null || officer.WasCollected)
            {
                return;
            }

            if (IsOfficerDead(officer))
            {
                Utils.SafeSetActive(poiObject, false);
                policeImageCache.Remove(poi.Pointer);
                return;
            }

            Color color = PoliceBlue;
            if (officer.PursuitTarget != null)
            {
                color = policeFlashState ? PoliceRed : PoliceBlue;
            }
            else if (officer.currentBodySearchInvestigation != null)
            {
                color = PoliceRed;
            }

            ApplyColorToPoI(poi, color);
        }
        catch
        {
            // The native officer can be collected between checks.
        }
    }

    private void RemoveInactivePolicePois()
    {
        List<int> deadKeys = null;
        foreach (KeyValuePair<int, GameObject> pair in policePois)
        {
            GameObject poiObject = pair.Value;
            bool keep = false;
            if (poiObject != null && poiObject.transform.parent != null && poiObject.transform.parent.gameObject.activeInHierarchy)
            {
                NPCPoI poi = poiObject.GetComponent<NPCPoI>();
                keep = poi != null
                    && poiOfficerMap.TryGetValue(poi.Pointer, out PoliceOfficer officer)
                    && officer != null
                    && !officer.WasCollected
                    && !IsOfficerDead(officer);
            }

            if (keep)
            {
                continue;
            }

            RemovePolicePoiMappings(poiObject);
            if (poiObject != null)
            {
                UnityEngine.Object.Destroy(poiObject);
            }

            deadKeys ??= new List<int>();
            deadKeys.Add(pair.Key);
        }

        if (deadKeys == null)
        {
            return;
        }

        foreach (int key in deadKeys)
        {
            policePois.Remove(key);
        }
    }

    private void RemovePolicePoiMappings(GameObject poiObject)
    {
        if (poiObject == null)
        {
            return;
        }

        NPCPoI poi = poiObject.GetComponent<NPCPoI>();
        if (poi != null)
        {
            poiOfficerMap.Remove(poi.Pointer);
            policeImageCache.Remove(poi.Pointer);
        }
    }

    private bool IsOfficerDead(PoliceOfficer officer)
    {
        try
        {
            NPCHealth health = officer?.GetComponent<NPCHealth>();
            return health != null && health.IsDead;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyColorToPoI(POI poi, Color color)
    {
        IntPtr pointer = poi.Pointer;
        if (policeImageCache.TryGetValue(pointer, out Image[] cachedImages) && ColorImages(cachedImages, color))
        {
            return;
        }

        policeImageCache.Remove(pointer);
        Image[] images = null;
        if (poi.IconContainer != null)
        {
            images = poi.IconContainer.GetComponentsInChildren<Image>(true);
        }

        if ((images == null || images.Length == 0) && poi.UI != null)
        {
            images = poi.UI.GetComponentsInChildren<Image>(true);
        }

        if (images != null && images.Length > 0)
        {
            policeImageCache[pointer] = images;
            ColorImages(images, color);
        }
    }

    private static bool ColorImages(Image[] images, Color color)
    {
        if (images == null || images.Length == 0)
        {
            return false;
        }

        bool coloredAny = false;
        foreach (Image image in images)
        {
            if (image != null)
            {
                image.color = color;
                coloredAny = true;
            }
        }

        return coloredAny;
    }

    private bool CheckNpcPoi(GameObject poi)
    {
        if (poi == null)
        {
            return false;
        }

        foreach (GameObject trackedPoi in npcPois.Values)
        {
            if (trackedPoi == poi)
            {
                return true;
            }
        }

        return false;
    }

    private static GameObject CreateMapLayer(string name, Transform parent)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        RectTransform layerRect = layer.AddComponent<RectTransform>();
        StretchToParent(layerRect, 0f, 0f);
        return layer;
    }

    private static void StretchToParent(RectTransform rect, float minimumOffset, float maximumOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(minimumOffset, minimumOffset);
        rect.offsetMax = new Vector2(maximumOffset, maximumOffset);
    }

    private static void StopMelonCoroutine(ref object token)
    {
        if (token == null)
        {
            return;
        }

        MelonCoroutines.Stop(token);
        token = null;
    }

    private static void DestroyGeneratedSprite(ref Sprite sprite)
    {
        if (sprite == null)
        {
            return;
        }

        Texture2D texture = sprite.texture;
        UnityEngine.Object.Destroy(sprite);
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
        }

        sprite = null;
    }
}
