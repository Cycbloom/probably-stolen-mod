using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Changes only the dump stage's artwork for the current visit.</summary>
    internal static class RegionBackdrop
    {
        private static SpriteRenderer owner;
        private static Sprite originalSprite, appliedSprite;
        private static Vector3 originalScale, appliedScale;
        private static Region appliedRegion;

        internal static void Refresh(MapUIManager map)
        {
            Region region = Picker.ChosenRegion;
            if (!Core.Enabled || map == null)
            {
                Restore();
                return;
            }
            if (region == null || !Picker.IsAtDump(map))
            {
                // ReturnNow marks inTransit before the fade has covered the scene.
                // Keep the applied art until HideStage actually hides its renderer.
                RestoreIfHidden();
                return;
            }

            GameObject stage = map.scavengingScene;
            if (stage == null || CustomUIManager.Instance == null) return;
            // The native --ScavScene is a container; its DumpingGround child
            // owns the visible sprite. It can still be inactive during arrival.
            SpriteRenderer renderer = stage.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer == null) return;
            if (owner == renderer && appliedRegion == region) return;
            Restore();

            Sprite art = CustomUIManager.Instance.GetModSprite(Core.Id, "dumping_ground_" + region.Id, true);
            // Missing art must leave the native scene usable.
            if (art == null || renderer.sprite == null) return;
            Vector3 before = renderer.sprite.bounds.size;
            Vector3 after = art.bounds.size;
            if (after.x <= 0f || after.y <= 0f) return;

            owner = renderer;
            originalSprite = renderer.sprite;
            originalScale = renderer.transform.localScale;
            appliedSprite = art;
            appliedRegion = region;
            // Paper sprites can have a different PPU/resolution. Keep the original
            // world-space footprint so the camera and inventory layout stay aligned.
            appliedScale = new Vector3(originalScale.x * before.x / after.x,
                originalScale.y * before.y / after.y, originalScale.z);
            renderer.sprite = art;
            renderer.transform.localScale = appliedScale;
            Debug.Log("[DGM-ART] Applied region " + region.Id + ": " + art.name
                + " on " + renderer.gameObject.name);
        }

        internal static void Restore()
        {
            if (owner != null)
            {
                // Do not overwrite a replacement another mod installed afterwards.
                if (owner.sprite == appliedSprite) owner.sprite = originalSprite;
                if (owner.transform.localScale == appliedScale)
                    owner.transform.localScale = originalScale;
            }
            owner = null;
            originalSprite = appliedSprite = null;
            appliedRegion = null;
        }

        internal static void RestoreIfHidden()
        {
            if (owner == null || !owner.gameObject.activeInHierarchy) Restore();
        }
    }
}
