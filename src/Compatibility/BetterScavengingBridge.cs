using System;
using System.Reflection;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 「更好的拾荒 / Better Scavenging」的两个箱子由它自己的 RollGroup 前缀裁定。
    /// 我们接管 SpawnDumpingGroundLoot 后，那条路径不会执行，箱子也就永远不出现。
    /// 这里直接调用它的公开判定入口 BoxDrop.TryRoll(groupId, out itemId)：
    /// 概率、电子学加成和配置项都由它自己算，命中后我们再 Spawn 对应的物品，
    /// 它的一次性状态（首个木箱必给钥匙卡、首个金属箱补发手套）由它的物品工厂负责。
    /// 没装时这里永远返回 false，开销只有一次空引用判断。
    /// </summary>
    internal static class BetterScavengingBridge
    {
        private const string TypeName = "BetterScavenging.BoxDrop";
        private const string AssemblyName = "BetterScavenging";

        private static MethodInfo tryRoll;
        private static bool probed;
        private static bool warned;

        /// <summary>可用时返回 true，避免每趟都做类型查找。</summary>
        internal static bool Available
        {
            get { return Resolve() != null; }
        }

        /// <summary>
        /// 按其自身概率判定一次。返回 true 时 itemId 是本次唯一应该生成的物品。
        /// </summary>
        internal static bool TryRoll(out string itemId)
        {
            itemId = null;
            MethodInfo method = Resolve();
            if (method == null) return false;

            object[] args = { ForeignLoot.GroupId, null };
            bool hit;
            try
            {
                hit = (bool)method.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Warn(ex);
                return false;
            }
            if (!hit) return false;

            itemId = args[1] as string;
            if (string.IsNullOrEmpty(itemId))
            {
                itemId = null;
                return false;
            }
            return true;
        }

        private static MethodInfo Resolve()
        {
            if (probed) return tryRoll;

            Type type = Type.GetType(TypeName + ", " + AssemblyName);
            if (type == null)
            {
                // 程序集可能还没进 AppDomain（加载顺序不定），逐帧调用也无所谓，
                // 只有拾荒时才会走到这里。
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && type == null; ++i)
                {
                    if (assemblies[i].GetName().Name != AssemblyName) continue;
                    type = assemblies[i].GetType(TypeName, false);
                }
            }
            if (type == null) return null;

            try
            {
                tryRoll = type.GetMethod("TryRoll", BindingFlags.Public | BindingFlags.Static, null,
                    new Type[] { typeof(string), typeof(string).MakeByRefType() }, null);
            }
            catch (Exception ex)
            {
                Warn(ex);
            }
            probed = true;
            return tryRoll;
        }

        private static void Warn(Exception ex)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[DumpingGroundMap] Better Scavenging bridge disabled: " + ex.Message);
        }
    }
}
