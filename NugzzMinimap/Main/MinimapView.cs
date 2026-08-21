using System;
using System.Collections;
using System.Collections.Generic;
using NugzzMinimap.Helpers;
using Il2CppInterop.Runtime.Attributes;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.UI.Phone.Map;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NugzzMinimap.Main;

/// <summary>
/// Builds the HUD and renders live location, NPC, police, and multiplayer markers from game objects.
/// All Unity and game-object access happens on MelonLoader's main-thread coroutine runner.
/// </summary>
[RegisterTypeInIl2Cpp]
public sealed class MinimapView : MonoBehaviour
{
    private const float VisualStateSyncInterval = 0.1f;
    private const float LiveMarkerScanInterval = 1f;
    private const float NativeLabelReferenceZoom = 1.5f;

    private static readonly Color PoliceBlue = new(0.2f, 0.4f, 1f, 1f);
    private static readonly Color PoliceRed = new(1f, 0.15f, 0.15f, 1f);

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
    private readonly HashSet<int> livePlayerIds = new();
    private readonly List<int> stalePlayerIds = new();
    private readonly Dictionary<int, POI> livePoiSources = new();
    private readonly Dictionary<int, GameObject> livePoiMarkers = new();
    private readonly Dictionary<int, RectTransform> livePoiMarkerRects = new();
    private readonly Dictionary<int, Image> livePoiMarkerBackgrounds = new();
    private readonly Dictionary<int, Image> livePoiMarkerIcons = new();
    private readonly Dictionary<int, Text> livePoiMarkerLabels = new();
    private readonly Dictionary<int, GameObject> liveBuildingLabelObjects = new();
    private readonly Dictionary<int, RectTransform> liveBuildingLabelRects = new();
    private readonly Dictionary<int, Text> liveBuildingLabels = new();
    private readonly Dictionary<int, Shadow> liveBuildingLabelShadows = new();
    private readonly Dictionary<int, NPC> liveNpcSources = new();
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
    private Action<bool> deadDropPreferenceCallback;
    private UnityAction deadDropToggleAction;

    private Transform playerTransform;
    private RectTransform mapContentRT;
    private RectTransform containerRT;
    private MapApp mapAppInstance;
    private RectTransform mapSourceRoot;
    private GameObject mapMirrorRoot;
    private GameObject mapBackgroundMirror;
    private Image mapBackgroundImage;
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

    private GameObject selectedNpcPoi;
    private GameObject benjiPoITemplate;
    private bool npcTrackingEnabled = true;
    private bool policeTrackingEnabled = true;
    private bool deadDropPoisVisible = true;
    private bool npcPoisInitialized;
    private bool policePoisInitialized;
    private bool policeFlashState;
    private bool isMinimapEnabled = true;
    private bool mapAppOpen;
    private bool cleanedUp;
    private float zoomScale = 0.7f;
    private float nextVisualStateSyncTime;
    private float nextLiveMarkerScanTime;
    private int lastLoggedPoiCount = -1;
    private int lastLoggedBuildingLabelCount = -1;
    private int lastLoggedNpcCount = -1;
    private int lastLoggedPoliceCount = -1;
    private int lastLoggedPotentialCustomerCount = -1;

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
        float minimapZoomScale,
        float positionX,
        float positionY,
        float timeDisplayPositionX,
        float timeDisplayPositionY,
        TimeDisplayColor timeDisplayColor,
        float timeDisplayBackgroundOpacity,
        int timeDisplayTextSize,
        Action<bool> onDeadDropVisibilityChanged)
    {
        if (player == null || mapApp == null)
        {
            MelonLogger.Error("NugzzMinimap initialization was attempted before the player/map was ready.");
            return;
        }

        cleanedUp = false;
        playerTransform = player.transform;
        mapAppInstance = mapApp;
        npcTrackingEnabled = trackNPCs;
        policeTrackingEnabled = trackPolice;
        deadDropPoisVisible = showDeadDropPois;
        deadDropPreferenceCallback = onDeadDropVisibilityChanged;
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
        nextLiveMarkerScanTime = 0f;
        RefreshLiveMarkerSources();
        ToggleMinimapVisibility(minimapEnabled);
    }

    public void Cleanup()
    {
        if (cleanedUp)
        {
            return;
        }

        cleanedUp = true;
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

            UnityEngine.Object.Destroy(canvasGo);
            canvasGo = null;
        }

        DestroyGeneratedSprite(ref circleSprite);
        DestroyGeneratedSprite(ref squareSprite);
        DestroyGeneratedSprite(ref solidCircleSprite);
        DestroyGeneratedSprite(ref playerArrowSprite);
        DestroyGeneratedSprite(ref deadDropButtonSprite);

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
        livePlayerIds.Clear();
        stalePlayerIds.Clear();
        livePoiSources.Clear();
        livePoiMarkers.Clear();
        livePoiMarkerRects.Clear();
        livePoiMarkerBackgrounds.Clear();
        livePoiMarkerIcons.Clear();
        livePoiMarkerLabels.Clear();
        liveBuildingLabelObjects.Clear();
        liveBuildingLabelRects.Clear();
        liveBuildingLabels.Clear();
        liveBuildingLabelShadows.Clear();
        liveNpcSources.Clear();
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
        selectedNpcPoi = null;
        benjiPoITemplate = null;
        mapSourceRoot = null;
        mapMirrorRoot = null;
        mapBackgroundMirror = null;
        mapBackgroundImage = null;
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
        deadDropPreferenceCallback = null;
        deadDropToggleAction = null;
        mapAppInstance = null;
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
        nextLiveMarkerScanTime = 0f;
        if (mapContentContainer != null)
        {
            RefreshLiveMarkerSources();
        }
    }

    public void UpdateZoomLevel(float newZoomScale)
    {
        zoomScale = Mathf.Clamp(newZoomScale, 0.4f, 1f);
        if (mapContentRT != null)
        {
            mapContentRT.localScale = new Vector3(zoomScale, zoomScale, 1f);
        }

        nextLiveMarkerScanTime = 0f;
        CenterMapOnPlayer();
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

    public void OnMapAppClosed()
    {
        mapAppOpen = false;
        nextVisualStateSyncTime = 0f;
        SynchronizeBackgroundMirror();
        nextLiveMarkerScanTime = 0f;
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
        UpdateCanvasVisibility();
        if (!isMinimapEnabled || mapAppOpen || Time.timeScale == 0f || mapContentContainer == null)
        {
            return;
        }

        Player player = Player.Local;
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

        SynchronizeBackgroundMirror();
        UpdateLiveMapMarkers();
        UpdatePlayerMarkers();
        CenterMapOnPlayer();
    }

    private void OnDestroy()
    {
        Cleanup();
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

        CreateDeadDropToggleButton();
    }

    [HideFromIl2Cpp]
    private void CreateDeadDropToggleButton()
    {
        if (containerRT == null)
        {
            return;
        }

        GameObject buttonObject = new("DeadDropToggleButton");
        buttonObject.transform.SetParent(containerRT, false);
        RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = new Vector2(0f, -48f);
        buttonRect.sizeDelta = new Vector2(132f, 28f);

        deadDropToggleButtonImage = buttonObject.AddComponent<Image>();
        deadDropButtonSprite ??= Utils.CreateRoundedRectSprite(132, 28, 6f, Color.white);
        deadDropToggleButtonImage.sprite = deadDropButtonSprite;
        deadDropToggleButtonImage.raycastTarget = true;

        deadDropToggleButton = buttonObject.AddComponent<Button>();
        deadDropToggleButton.targetGraphic = deadDropToggleButtonImage;
        deadDropToggleButton.transition = Selectable.Transition.None;
        Navigation navigation = deadDropToggleButton.navigation;
        navigation.mode = Navigation.Mode.None;
        deadDropToggleButton.navigation = navigation;

        GameObject textObject = new("Label");
        textObject.transform.SetParent(buttonRect, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        StretchToParent(textRect, 2f, -2f);
        deadDropToggleButtonText = textObject.AddComponent<Text>();
        deadDropToggleButtonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        deadDropToggleButtonText.fontSize = 11;
        deadDropToggleButtonText.fontStyle = FontStyle.Bold;
        deadDropToggleButtonText.alignment = TextAnchor.MiddleCenter;
        deadDropToggleButtonText.color = Color.white;
        deadDropToggleButtonText.raycastTarget = false;
        Outline outline = textObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);

        deadDropToggleAction = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityAction>(
            new Action(ToggleDeadDropsFromButton));
        deadDropToggleButton.onClick.AddListener(deadDropToggleAction);
        UpdateDeadDropButtonVisual();
        buttonObject.transform.SetAsLastSibling();
    }

    [HideFromIl2Cpp]
    private void ToggleDeadDropsFromButton()
    {
        bool newVisibility = !deadDropPoisVisible;
        UpdateDeadDropVisibility(newVisibility);
        deadDropPreferenceCallback?.Invoke(newVisibility);
    }

    private void UpdateDeadDropButtonVisual()
    {
        if (deadDropToggleButtonText != null)
        {
            deadDropToggleButtonText.text = deadDropPoisVisible ? "DEAD DROPS: ON" : "DEAD DROPS: OFF";
        }

        if (deadDropToggleButtonImage != null)
        {
            deadDropToggleButtonImage.color = deadDropPoisVisible
                ? new Color(0.12f, 0.48f, 0.24f, 0.95f)
                : new Color(0.5f, 0.14f, 0.14f, 0.95f);
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
            playerMarkerLayer?.transform.SetAsLastSibling();
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
        playerMarkerLayer?.transform.SetAsLastSibling();
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

    private void SynchronizeBackgroundMirror()
    {
        Image sourceImage = mapAppInstance?.BackgroundImage;
        if (sourceImage == null || mapBackgroundImage == null)
        {
            return;
        }

        mapBackgroundImage.enabled = true;
        mapBackgroundImage.sprite = sourceImage.sprite ?? mapAppInstance.MainMapSprite;
        mapBackgroundImage.overrideSprite = sourceImage.overrideSprite;
        // Use the default UI material and an opaque white tint so closed-phone fade/stencil state
        // cannot turn the standalone HUD copy black or transparent.
        mapBackgroundImage.material = null;
        mapBackgroundImage.type = sourceImage.type;
        mapBackgroundImage.preserveAspect = true;
        mapBackgroundImage.color = Color.white;
        mapBackgroundImage.raycastTarget = false;

        RectTransform backgroundRect = mapBackgroundMirror.GetComponent<RectTransform>();
        if (backgroundRect != null)
        {
            backgroundRect.sizeDelta = GetMapDimensions();
        }
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

    private static void CreateVisualComponents(GameObject source, GameObject mirror)
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

    private static void CopyMutableVisualState(GameObject source, GameObject mirror, bool forceVisible)
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
            mirrorText.enabled = forceVisible || sourceText.enabled;
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
                // Move the common parent once. Moving the background too would double the offset.
                mapContentRT.anchoredPosition = -mapPosition * zoomScale;
            }
        }
        catch
        {
            // Game objects can be collected between the Unity null check and the IL2CPP call during scene changes.
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

                markerRect.anchoredPosition = mapUtility.GetMapPosition(trackedTransform.position);
                if (playerMarkerImages.TryGetValue(playerId, out Image markerImage) && markerImage != null)
                {
                    markerImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -trackedTransform.eulerAngles.y);
                    markerImage.color = GetPlayerMarkerColor(playerId, lobbyPlayer == Player.Local);
                }

                if (playerMarkerLabels.TryGetValue(playerId, out Text markerLabel) && markerLabel != null)
                {
                    string playerName = string.IsNullOrWhiteSpace(lobbyPlayer.PlayerName) ? "Player" : lobbyPlayer.PlayerName;
                    markerLabel.text = lobbyPlayer == Player.Local ? $"{playerName} (You)" : playerName;
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
        }

        playerMarkerLayer.transform.SetAsLastSibling();
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

        if (Time.unscaledTime >= nextLiveMarkerScanTime)
        {
            RefreshLiveMarkerSources();
        }

        MapPositionUtility mapUtility = Singleton<MapPositionUtility>.Instance;
        if (mapUtility == null)
        {
            return;
        }

        foreach (KeyValuePair<int, POI> pair in livePoiSources)
        {
            try
            {
                POI source = pair.Value;
                if (source != null && source.transform != null
                    && livePoiMarkerRects.TryGetValue(pair.Key, out RectTransform markerRect))
                {
                    markerRect.anchoredPosition = mapUtility.GetMapPosition(source.transform.position);
                }
            }
            catch
            {
                // A location can be unloaded while its marker position is being sampled.
            }
        }

        foreach (KeyValuePair<int, NPC> pair in liveNpcSources)
        {
            try
            {
                NPC source = pair.Value;
                if (source != null && source.transform != null
                    && liveNpcMarkerRects.TryGetValue(pair.Key, out RectTransform markerRect))
                {
                    markerRect.anchoredPosition = mapUtility.GetMapPosition(source.transform.position);
                    if (liveNpcMarkerPortraits.TryGetValue(pair.Key, out Image portrait) && portrait != null)
                    {
                        portrait.sprite = source.MugshotSprite;
                        portrait.enabled = portrait.sprite != null;
                        if (liveNpcMarkerBackgrounds.TryGetValue(pair.Key, out Image background)
                            && background != null)
                        {
                            background.enabled = portrait.enabled;
                        }
                    }
                }
            }
            catch
            {
                // NPCs can be spawned, despawned, or moved between network scenes at any time.
            }
        }

        policeFlashState = (Mathf.FloorToInt(Time.unscaledTime * 3f) & 1) == 0;
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
                    markerRect.anchoredPosition = mapUtility.GetMapPosition(officer.transform.position);
                }

                if (livePoliceMarkerPortraits.TryGetValue(pair.Key, out Image portrait) && portrait != null)
                {
                    portrait.sprite = officer.MugshotSprite;
                    portrait.enabled = portrait.sprite != null;
                }

                if (livePoliceMarkerBackgrounds.TryGetValue(pair.Key, out Image background) && background != null)
                {
                    background.color = GetPoliceMarkerColor(officer);
                }
            }
            catch
            {
                // Native officer state can be collected between the Unity null check and property access.
            }
        }

        liveBuildingLabelLayer.transform.SetAsLastSibling();
        livePoiMarkerLayer.transform.SetAsLastSibling();
        liveNpcMarkerLayer.transform.SetAsLastSibling();
        livePoliceMarkerLayer.transform.SetAsLastSibling();
        playerMarkerLayer?.transform.SetAsLastSibling();
    }

    [HideFromIl2Cpp]
    private void RefreshLiveMarkerSources()
    {
        nextLiveMarkerScanTime = Time.unscaledTime + LiveMarkerScanInterval;
        ScanNativeBuildingLabels();
        ScanLiveLocations();
        ScanLiveNpcs();

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
            liveBuildingLabelRects.Remove(labelId);
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
        targetRect.localRotation = sourceRelativeRotation;

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
        targetLabel.enabled = true;
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
            targetShadow.enabled = true;
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
                    if (location == null || location.gameObject == null
                        || location.gameObject.GetComponent<NPCPoI>() != null
                        || IsPlayerPoi(location)
                        || (!deadDropPoisVisible && IsDeadDropPoi(location)))
                    {
                        continue;
                    }

                    int locationId = location.gameObject.GetInstanceID();
                    scannedPoiIds.Add(locationId);
                    livePoiSources[locationId] = location;
                    EnsureLocationMarker(locationId);
                    UpdateLocationMarkerVisual(locationId, location);
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
            DestroyMarker(livePoiMarkers, locationId);
            livePoiSources.Remove(locationId);
            livePoiMarkerRects.Remove(locationId);
            livePoiMarkerBackgrounds.Remove(locationId);
            livePoiMarkerIcons.Remove(locationId);
            livePoiMarkerLabels.Remove(locationId);
        }
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
    private void ScanLiveNpcs()
    {
        scannedNpcIds.Clear();
        scannedPoliceIds.Clear();
        try
        {
            NPC[] npcs = UnityEngine.Object.FindObjectsOfType<NPC>();
            if (npcs != null)
            {
                foreach (NPC npc in npcs)
                {
                    if (npc == null || npc.gameObject == null || !npc.gameObject.activeInHierarchy
                        || npc.IsLocalPlayer)
                    {
                        continue;
                    }

                    int npcId = npc.gameObject.GetInstanceID();
                    PoliceOfficer officer = npc.gameObject.GetComponent<PoliceOfficer>();
                    if (officer != null)
                    {
                        if (IsOfficerDead(officer))
                        {
                            continue;
                        }

                        scannedPoliceIds.Add(npcId);
                        livePoliceSources[npcId] = officer;
                        EnsurePoliceMarker(npcId);
                        if (livePoliceMarkerPortraits.TryGetValue(npcId, out Image policePortrait)
                            && policePortrait != null)
                        {
                            policePortrait.sprite = officer.MugshotSprite;
                            policePortrait.enabled = policePortrait.sprite != null;
                        }
                    }
                    else
                    {
                        scannedNpcIds.Add(npcId);
                        liveNpcSources[npcId] = npc;
                        EnsureNpcMarker(npcId);
                        if (liveNpcPotentialCustomerCircles.TryGetValue(npcId, out Image potentialCircle)
                            && potentialCircle != null)
                        {
                            Customer customer = npc.gameObject.GetComponent<Customer>();
                            potentialCircle.enabled = IsPotentialCustomer(customer);
                        }

                        if (liveNpcMarkerPortraits.TryGetValue(npcId, out Image npcPortrait)
                            && npcPortrait != null)
                        {
                            npcPortrait.sprite = npc.MugshotSprite;
                            npcPortrait.enabled = npcPortrait.sprite != null;
                            if (liveNpcMarkerBackgrounds.TryGetValue(npcId, out Image npcBackground)
                                && npcBackground != null)
                            {
                                npcBackground.enabled = npcPortrait.enabled;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"NugzzMinimap could not refresh NPC markers: {exception.Message}");
        }

        RemoveStaleNpcMarkers();
        RemoveStalePoliceMarkers();
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
            label.enabled = !string.IsNullOrWhiteSpace(label.text);
        }

        Sprite iconSprite = GetLocationIcon(location, out Color iconColor);
        bool hasIcon = iconSprite != null;
        if (livePoiMarkerIcons.TryGetValue(locationId, out Image icon) && icon != null)
        {
            icon.sprite = iconSprite;
            icon.color = iconColor;
            icon.enabled = hasIcon;
        }

        if (livePoiMarkerBackgrounds.TryGetValue(locationId, out Image background) && background != null)
        {
            background.enabled = hasIcon;
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
        Customer[] customers = UnityEngine.Object.FindObjectsOfType<Customer>();
        if (customers == null)
        {
            return;
        }

        foreach (Customer customer in customers)
        {
            try
            {
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
                NPC npc = customer.gameObject.GetComponent<NPC>();
                if (npc == null)
                {
                    continue;
                }

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
