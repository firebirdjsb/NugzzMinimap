using System;
using UnityEngine;

namespace NugzzMinimap.Helpers;

/// <summary>Small Unity helpers used to generate asset-free UI sprites safely.</summary>
public static class Utils
{
    public static Sprite CreateCircleSprite(
        int diameter,
        Color color,
        int resolutionMultiplier = 1,
        int featherWidth = 0,
        bool featherInside = true)
    {
        ValidateDimensions(diameter, diameter, resolutionMultiplier);

        int size = checked(diameter * resolutionMultiplier);
        int featherPixels = Math.Max(0, featherWidth * resolutionMultiplier);
        float center = (size - 1) / 2f;
        float radius = size / 2f;
        Color transparent = new(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float deltaX = x - center;
                float deltaY = y - center;
                float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
                float alpha = 0f;

                if (distance <= radius)
                {
                    alpha = color.a;
                    if (featherInside && featherPixels > 0)
                    {
                        alpha *= Mathf.Clamp01((radius - distance) / featherPixels);
                    }
                }
                else if (!featherInside && featherPixels > 0 && distance <= radius + featherPixels)
                {
                    alpha = color.a * Mathf.Clamp01(1f - (distance - radius) / featherPixels);
                }

                pixels[y * size + x] = alpha > 0f
                    ? new Color(color.r, color.g, color.b, alpha)
                    : transparent;
            }
        }

        return CreateSprite(size, size, pixels, resolutionMultiplier);
    }

    public static Sprite CreateRoundedSquareSprite(
        int size,
        float cornerRadius,
        Color color,
        int resolutionMultiplier = 2)
    {
        return CreateRoundedRectSprite(size, size, cornerRadius, color, resolutionMultiplier);
    }

    public static Sprite CreateTriangleSprite(int size, Color color, int resolutionMultiplier = 2)
    {
        ValidateDimensions(size, size, resolutionMultiplier);

        int pixelSize = checked(size * resolutionMultiplier);
        float center = (pixelSize - 1) / 2f;
        Color transparent = new(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[pixelSize * pixelSize];

        for (int y = 0; y < pixelSize; y++)
        {
            float heightProgress = pixelSize > 1 ? y / (float)(pixelSize - 1) : 1f;
            float halfWidth = (1f - heightProgress) * center;
            for (int x = 0; x < pixelSize; x++)
            {
                pixels[y * pixelSize + x] = Math.Abs(x - center) <= halfWidth ? color : transparent;
            }
        }

        return CreateSprite(pixelSize, pixelSize, pixels, 100f * resolutionMultiplier);
    }

    public static Sprite CreateRoundedRectSprite(
        int width,
        int height,
        float cornerRadius,
        Color color,
        int resolutionMultiplier = 2)
    {
        ValidateDimensions(width, height, resolutionMultiplier);

        int pixelWidth = checked(width * resolutionMultiplier);
        int pixelHeight = checked(height * resolutionMultiplier);
        float radius = Mathf.Clamp(
            cornerRadius * resolutionMultiplier,
            0f,
            Math.Min(pixelWidth, pixelHeight) / 2f);
        Color transparent = new(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[pixelWidth * pixelHeight];

        for (int y = 0; y < pixelHeight; y++)
        {
            for (int x = 0; x < pixelWidth; x++)
            {
                bool inside = IsInsideRoundedRectangle(x, y, pixelWidth, pixelHeight, radius);
                pixels[y * pixelWidth + x] = inside ? color : transparent;
            }
        }

        return CreateSprite(pixelWidth, pixelHeight, pixels, 100f * resolutionMultiplier);
    }

    public static bool SafeSetActive(GameObject gameObject, bool value)
    {
        if (gameObject == null)
        {
            return false;
        }

        try
        {
            gameObject.SetActive(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Sprite CreateSprite(int width, int height, Color[] pixels, float pixelsPerUnit)
    {
        Texture2D texture = new(width, height, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels(pixels);
        texture.Apply(false, false);

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit);
    }

    private static bool IsInsideRoundedRectangle(int x, int y, int width, int height, float radius)
    {
        if (radius <= 0f)
        {
            return true;
        }

        float closestX = Mathf.Clamp(x, radius, width - radius - 1f);
        float closestY = Mathf.Clamp(y, radius, height - radius - 1f);
        float deltaX = x - closestX;
        float deltaY = y - closestY;
        return deltaX * deltaX + deltaY * deltaY <= radius * radius;
    }

    private static void ValidateDimensions(int width, int height, int resolutionMultiplier)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        if (resolutionMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resolutionMultiplier), "Resolution multiplier must be positive.");
        }
    }
}
