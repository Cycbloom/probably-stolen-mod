using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 主菜单改调试点数时的一次性交接记录。
    /// 走 ModHelper 的 ModDataFile 存进本模组自己的目录
    /// （persistentDataPath/ModData/&lt;modId&gt;/pending-debug.txt）：
    /// 不碰存档槽、不需要 files 能力，主菜单写、读档时读，跨存档可见。
    /// 四个值写在同一个文件里，不会出现「只写了一半」的中间态。
    /// 1.0.4 及更早把同样四个值写进游戏的 SaveFiles 索引，那份数据不会迁移：
    /// 调试点数本就是调试项，升级时丢掉一次排队值不影响存档与正常玩法。
    /// </summary>
    internal static class PendingDebugPoints
    {
        internal const string FileName = "pending-debug.txt";

        internal struct Record
        {
            internal bool Pending;
            internal string Run;
            internal int Slot;
            internal float Points;
        }

        internal static Record Empty()
        {
            Record record = new Record();
            record.Run = "";
            record.Slot = -1;
            return record;
        }

        /// <summary>序列化成 key=value 行文本。float 用不变文化，避免逗号小数点。</summary>
        internal static string Format(Record record)
        {
            StringBuilder text = new StringBuilder();
            text.Append("flag=").Append(record.Pending ? '1' : '0').Append('\n');
            text.Append("run=").Append(Sanitize(record.Run)).Append('\n');
            text.Append("slot=").Append(record.Slot.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("points=").Append(record.Points.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// 宽容解析：未知键、空行、CRLF、旧文件缺字段都按默认值处理，
        /// 缺 flag 视为没有排队值。解析不做 IO，便于单测。
        /// </summary>
        internal static Record Parse(string text)
        {
            Record record = Empty();
            if (string.IsNullOrEmpty(text)) return record;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; ++i)
            {
                string line = lines[i].TrimEnd('\r');
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                string key = line.Substring(0, split).Trim();
                string value = line.Substring(split + 1);
                if (key == "flag")
                {
                    record.Pending = value.Trim() == "1";
                }
                else if (key == "run")
                {
                    record.Run = value;
                }
                else if (key == "slot")
                {
                    int slot;
                    if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                        record.Slot = slot;
                }
                else if (key == "points")
                {
                    float points;
                    if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out points))
                        record.Points = points;
                }
            }
            return record;
        }

        /// <summary>读交接记录。文件不存在时返回 false，不报错。</summary>
        internal static bool TryRead(out Record record)
        {
            record = Empty();
            try
            {
                if (!ModHelper.ModDataFileExists(Core.Id, FileName)) return false;
                record = Parse(ModHelper.ReadModDataFileText(Core.Id, FileName));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not read pending points: " + ex.Message);
                return false;
            }
        }

        internal static bool Write(Record record)
        {
            try
            {
                ModHelper.WriteModDataFileText(Core.Id, FileName, Format(record));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not queue points: " + ex.Message);
                return false;
            }
        }

        internal static bool Clear()
        {
            try
            {
                ModHelper.DeleteModDataFile(Core.Id, FileName);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not clear pending points: " + ex.Message);
                return false;
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
