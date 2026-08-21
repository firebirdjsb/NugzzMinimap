using NugzzMinimap.Helpers;
using Il2CppInterop.Runtime.Attributes;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace NugzzMinimap.Main;

/// <summary>Renders the in-game weekday and time below the minimap.</summary>
[RegisterTypeInIl2Cpp]
public sealed class TimeDisplayView : MonoBehaviour
{
    private const int BaseWidth = 240;
    private const int BaseHeight = 38;
    private const int DefaultTextSize = 18;

    private Text dayText;
    private Text timeText;
    private GameObject timeDisplayGo;
    private RectTransform rectTransform;
    private Image backgroundImage;
    private Image borderImage;
    private Sprite backgroundSprite;
    private Sprite borderSprite;
    private bool showGameTime;
    private bool cleanedUp;
    private int lastDisplayedTime = -1;
    private EDay lastDisplayedDay = (EDay)(-1);

    [HideFromIl2Cpp]
    public void Initialize(
        RectTransform parent,
        bool initialShowGameTime,
        float positionX,
        float positionY,
        TimeDisplayColor textColor,
        float backgroundOpacity,
        int textSize)
    {
        cleanedUp = false;
        showGameTime = initialShowGameTime;
        backgroundOpacity = Mathf.Clamp01(backgroundOpacity);
        textSize = Mathf.Clamp(textSize, 10, 30);

        float scale = textSize / (float)DefaultTextSize;
        float width = BaseWidth * scale;
        float height = BaseHeight * scale;

        timeDisplayGo = new GameObject("TimeDisplay");
        timeDisplayGo.transform.SetParent(parent, false);
        rectTransform = timeDisplayGo.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.anchoredPosition = new Vector2(positionX, positionY);
        rectTransform.sizeDelta = new Vector2(width, height);

        // Border is created first so the background and text render above it.
        GameObject borderObject = CreateLayer("Border", rectTransform, -1.5f, 1.5f);
        borderImage = borderObject.AddComponent<Image>();
        borderSprite = Utils.CreateRoundedRectSprite((int)width, (int)height, 10f, new Color(0.3f, 0.3f, 0.3f, 1f));
        borderImage.sprite = borderSprite;
        borderImage.type = Image.Type.Sliced;
        borderImage.color = new Color(1f, 1f, 1f, backgroundOpacity * 0.92f);
        borderImage.raycastTarget = false;

        GameObject backgroundObject = CreateLayer("Background", rectTransform, 0f, 0f);
        backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundSprite = Utils.CreateRoundedRectSprite((int)width, (int)height, 10f, new Color(0.1f, 0.1f, 0.1f, 1f));
        backgroundImage.sprite = backgroundSprite;
        backgroundImage.type = Image.Type.Sliced;
        backgroundImage.color = new Color(1f, 1f, 1f, backgroundOpacity);
        backgroundImage.raycastTarget = false;

        Color resolvedTextColor = GetColorFromChoice(textColor);
        dayText = CreateText("DayText", rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 1f), textSize, resolvedTextColor);
        timeText = CreateText("TimeText", rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 1f), textSize, resolvedTextColor);

        GameObject separator = new("Separator");
        separator.transform.SetParent(rectTransform, false);
        RectTransform separatorRect = separator.AddComponent<RectTransform>();
        separatorRect.anchorMin = new Vector2(0.5f, 0.2f);
        separatorRect.anchorMax = new Vector2(0.5f, 0.8f);
        separatorRect.sizeDelta = new Vector2(1f, 0f);
        separatorRect.anchoredPosition = Vector2.zero;
        Image separatorImage = separator.AddComponent<Image>();
        separatorImage.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
        separatorImage.raycastTarget = false;

        timeDisplayGo.SetActive(showGameTime);
    }

    public void Cleanup()
    {
        if (cleanedUp)
        {
            return;
        }

        cleanedUp = true;
        if (timeDisplayGo != null)
        {
            Object.Destroy(timeDisplayGo);
            timeDisplayGo = null;
        }

        DestroyGeneratedSprite(ref backgroundSprite);
        DestroyGeneratedSprite(ref borderSprite);
    }

    public void ToggleVisibility(bool isVisible)
    {
        showGameTime = isVisible;
        if (timeDisplayGo != null)
        {
            timeDisplayGo.SetActive(isVisible);
        }
    }

    public void UpdatePosition(float positionX, float positionY)
    {
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = new Vector2(positionX, positionY);
        }
    }

    [HideFromIl2Cpp]
    public void UpdateColor(TimeDisplayColor colorChoice)
    {
        Color color = GetColorFromChoice(colorChoice);
        if (dayText != null)
        {
            dayText.color = color;
        }

        if (timeText != null)
        {
            timeText.color = color;
        }
    }

    public void UpdateBackgroundOpacity(float opacity)
    {
        opacity = Mathf.Clamp01(opacity);
        if (backgroundImage != null)
        {
            Color color = backgroundImage.color;
            color.a = opacity;
            backgroundImage.color = color;
        }

        if (borderImage != null)
        {
            Color color = borderImage.color;
            color.a = opacity * 0.92f;
            borderImage.color = color;
        }
    }

    public void UpdateTextSize(int textSize)
    {
        textSize = Mathf.Clamp(textSize, 10, 30);
        if (dayText != null)
        {
            dayText.fontSize = textSize;
        }

        if (timeText != null)
        {
            timeText.fontSize = textSize;
        }

        if (rectTransform != null)
        {
            float scale = textSize / (float)DefaultTextSize;
            rectTransform.sizeDelta = new Vector2(BaseWidth * scale, BaseHeight * scale);
        }
    }

    private void Update()
    {
        if (!showGameTime || !NetworkSingleton<TimeManager>.InstanceExists || dayText == null || timeText == null)
        {
            return;
        }

        TimeManager timeManager = NetworkSingleton<TimeManager>.Instance;
        int currentTime = timeManager.CurrentTime;
        EDay currentDay = timeManager.CurrentDay;
        if (currentTime == lastDisplayedTime && currentDay == lastDisplayedDay)
        {
            return;
        }

        lastDisplayedTime = currentTime;
        lastDisplayedDay = currentDay;
        dayText.text = currentDay.ToString();
        timeText.text = TimeManager.Get12HourTime(currentTime, true);
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private static Color GetColorFromChoice(TimeDisplayColor colorChoice)
    {
        return colorChoice switch
        {
            TimeDisplayColor.Red => new Color(1f, 0.2f, 0.2f, 1f),
            TimeDisplayColor.Orange => new Color(1f, 0.6f, 0f, 1f),
            TimeDisplayColor.Yellow => new Color(1f, 1f, 0.2f, 1f),
            TimeDisplayColor.Green => new Color(0.2f, 1f, 0.2f, 1f),
            TimeDisplayColor.Blue => new Color(0.3f, 0.7f, 1f, 1f),
            TimeDisplayColor.Indigo => new Color(0.3f, 0f, 0.8f, 1f),
            TimeDisplayColor.Violet => new Color(0.8f, 0.3f, 1f, 1f),
            _ => Color.white
        };
    }

    private static GameObject CreateLayer(string name, RectTransform parent, float minimumOffset, float maximumOffset)
    {
        GameObject layer = new(name);
        layer.transform.SetParent(parent, false);
        RectTransform rect = layer.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(minimumOffset, minimumOffset);
        rect.offsetMax = new Vector2(maximumOffset, maximumOffset);
        return layer;
    }

    private static Text CreateText(
        string name,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        int textSize,
        Color color)
    {
        GameObject textObject = new(name);
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = textSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(4f, 2f);
        rect.offsetMax = new Vector2(-4f, -2f);

        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    private static void DestroyGeneratedSprite(ref Sprite sprite)
    {
        if (sprite == null)
        {
            return;
        }

        Texture2D texture = sprite.texture;
        Object.Destroy(sprite);
        if (texture != null)
        {
            Object.Destroy(texture);
        }

        sprite = null;
    }
}
