using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    /// <summary>
    /// 延后一帧再存档。读档时 FireOnGameLoadedLate 位于 PlayerStore.LoadGame
    /// 末尾、EmporiumEntry.Start 还没跑完，当场 SaveGame 会快照到未完成的界面
    /// 状态；等到 LateUpdate 时场景初始化已完成，与日终存档看到的状态一致。
    /// MonoBehaviour 必须是顶层类，Unity 才能 AddComponent。
    /// </summary>
    internal sealed class DeferredSave : MonoBehaviour
    {
        private static DeferredSave pending;
        private string reason;
        private RunIdentity identity;

        internal static void Run(string why)
        {
            if (!Core.Enabled || !PlayerStore.HasInstance || EmporiumEntry.Instance == null) return;
            PlayerStore owner = PlayerStore.Instance;
            if (pending != null && !pending.identity.Matches(owner, owner.runID, owner.saveSlotId)) Cancel();
            if (pending != null)
            {
                if (string.IsNullOrEmpty(pending.reason)) pending.reason = why;
                return;
            }
            GameObject go = new GameObject("DGM_DeferredSave");
            UnityEngine.Object.DontDestroyOnLoad(go);
            pending = go.AddComponent<DeferredSave>();
            pending.reason = why;
            pending.identity = new RunIdentity(owner, owner.runID, owner.saveSlotId);
        }

        internal static void Cancel()
        {
            DeferredSave save = pending;
            pending = null;
            if (save == null) return;
            save.reason = null;
            save.enabled = false;
            UnityEngine.Object.Destroy(save.gameObject);
        }

        private void LateUpdate()
        {
            if (string.IsNullOrEmpty(reason)) return;
            string why = reason;
            reason = null;
            pending = null;
            try
            {
                PlayerStore owner = PlayerStore.HasInstance ? PlayerStore.Instance : null;
                if (Core.Enabled && owner != null && EmporiumEntry.Instance != null
                    && identity.Matches(owner, owner.runID, owner.saveSlotId))
                    Reputation.SaveNow(why);
            }
            finally
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }
}
