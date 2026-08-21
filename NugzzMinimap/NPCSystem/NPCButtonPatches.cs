using System;
using NugzzMinimap.Main;
using HarmonyLib;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.ContactsApp;
using Il2CppScheduleOne.UI.Phone.Map;
using MelonLoader;
using UnityEngine;

namespace NugzzMinimap.NPCSystem;

/// <summary>
/// Bridges phone UI actions to the minimap. The minimap reproduces the phone map visuals, while the
/// contacts screen points its Show On Map button at customer markers owned by the game's map system.
/// </summary>
public static class NPCButtonPatches
{
    private static MinimapView minimapViewInstance;

    public static void SetMinimapView(MinimapView view)
    {
        minimapViewInstance = view;
    }

    [HarmonyPatch(typeof(MapApp), nameof(MapApp.SetOpen), typeof(bool))]
    private static class MapAppSetOpenPatch
    {
        private static void Prefix(bool open)
        {
            if (minimapViewInstance == null || !open)
            {
                return;
            }

            try
            {
                minimapViewInstance.OnMapAppOpened();
            }
            catch (Exception exception)
            {
                LogPatchFailure("opening the phone map", exception);
            }
        }

        private static void Postfix(bool open)
        {
            if (minimapViewInstance == null)
            {
                return;
            }

            try
            {
                if (!open)
                {
                    minimapViewInstance.OnMapAppClosed();
                    minimapViewInstance.OnMapClosed();
                }
            }
            catch (Exception exception)
            {
                LogPatchFailure("closing the phone map", exception);
            }
        }
    }

    [HarmonyPatch(typeof(ContactsDetailPanel), nameof(ContactsDetailPanel.Open), typeof(NPC))]
    private static class ContactsDetailPanelOpenPatch
    {
        private static void Postfix(NPC npc)
        {
            if (minimapViewInstance == null || npc == null)
            {
                return;
            }

            try
            {
                GameObject showOnMap = GameObject.Find("ContactsApp/Container/Detail/Map/ShowOnMap");
                if (showOnMap != null)
                {
                    showOnMap.SetActive(true);
                }

                GameObject detailObject = GameObject.Find("ContactsApp/Container/Detail");
                ContactsDetailPanel detailPanel = detailObject?.GetComponent<ContactsDetailPanel>();
                Transform poiTransform = npc.transform.Find("NPCPoI(Clone)");
                if (detailPanel != null && poiTransform != null)
                {
                    detailPanel.poi = poiTransform.GetComponent<NPCPoI>();
                    minimapViewInstance.OnContactDetailOpened(npc);
                }
            }
            catch (Exception exception)
            {
                LogPatchFailure("opening a contact", exception);
            }
        }
    }

    private static void LogPatchFailure(string operation, Exception exception)
    {
        MelonLogger.Warning($"NugzzMinimap failed while {operation}: {exception.Message}");
    }
}
