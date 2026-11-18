using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Deliver one note at 20/40/100, with the legacy token only at 100.</summary>
    internal static class ScavengerMilestones
    {
        internal const string TokenId = "scav_token";
        // Preserve the old receipt so an upgraded save never receives a duplicate token.
        internal const string LegacyTokenGivenKey = "scav_medal_given";

        internal static void OnShutterOpenedLate()
        {
            if (!Core.Enabled || !PlayerStore.HasInstance) return;
            PlayerStore store = PlayerStore.Instance;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null || entry.frontInvinvElement == null) return;
            foreach (MilestoneNote note in MilestoneNotes.All)
            {
                try
                {
                    if (!note.Due(Reputation.Points, ModHelper.GetModDataBool(Core.Id, note.Key, false))) continue;
                    if (!entry.frontInvinvElement.CanTakeAnything()) break;
                    bool tokenAlreadyGiven = ModHelper.GetModDataBool(Core.Id, LegacyTokenGivenKey, false);
                    bool includeToken = note.GivesToken(tokenAlreadyGiven);
                    bool delivered = MilestoneDelivery.Give<GameItem>(includeToken,
                        () => DirectoryMaster.Item(TokenId, true),
                        () => PreBuiltItemHelper.CreateHandnote(
                            note.Body(includeToken),
                            note.Title, Copy.Get("scavengermilestones_the_scavengers"), 1),
                        item => store.TryAddDirectSellingItemToTable(item, true, false, true, 0),
                        items => store.RemoveItemFromStoreInventory(items),
                        () => MarkReceived(note, includeToken));
                    if (!delivered) break; // Retry the unfinished milestone next opening.
                    Debug.Log("[DumpingGroundMap] delivered scavenger milestone " + note.Threshold
                        + (includeToken ? " with token" : " note"));
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[DumpingGroundMap] milestone " + note.Threshold + " delivery failed: " + error.Message);
                    break;
                }
            }
        }

        private static void MarkReceived(MilestoneNote note, bool includeToken)
        {
            try
            {
                if (includeToken) ModHelper.SetModDataBool(Core.Id, LegacyTokenGivenKey, true);
                ModHelper.SetModDataBool(Core.Id, note.Key, true);
            }
            catch
            {
                ModHelper.SetModDataBool(Core.Id, note.Key, false);
                if (includeToken) ModHelper.SetModDataBool(Core.Id, LegacyTokenGivenKey, false);
                throw;
            }
        }
    }
}
