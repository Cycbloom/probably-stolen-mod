using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    /// <summary>
    /// All loot tuning lives in the game's own mod-config window instead of the map.
    /// A postfix on ModConfigWindow.BuildEntry hooks the native sliders, and a patch
    /// on CustomUIElement.SetFixedWidth appends a value label to the same row, right
    /// of the slider, so the number sits where the thumb is instead of on its own
    /// line. Below the last entry the area value block and the preset buttons
    /// are appended. Dragging only changes Tuning's in-memory values and the labels;
    /// the config file is written on PointerUp or when Apply is pressed.
    /// </summary>
    internal static class NativeSettings
    {
        private const string KeyJunk = "junk_percent";
        private const string KeyHigh = "high_percent";
        private const string KeyCrate = "crate_percent";
        private const string KeyCard = "keycard_percent";
        private const string KeyMinor = "minor_percent";
        private const string KeyMinorValue = "minor_value";
        private const string KeyGroundWidth = Tuning.GroundWidthKey;
        private const string KeyGroundHeight = Tuning.GroundHeightKey;
        private const string KeyGroundAlign = Tuning.GroundAlignKey;
        private const string LogTag = "[DGM-SLIDER] ";

        private static CustomUIWindow window;
        private static CustomUIWindow builtFor;
        private static CustomUIElement junkValue, highValue;
        private static CustomUIElement crateValue, cardValue;
        private static CustomUIElement minorValue;
        private static CustomUIElement minorCrateValue;
        private static CustomUIElement debugValue;
        private static CustomUIElement groundWidthValue, groundHeightValue;
        private static CustomUIElement specialtyDropdown;
        private static CustomUIElement groundAlignDropdown;
        private static CustomUIElement[] evLabels;
        private static CustomUIElement junkSlider, highSlider;
        private static CustomUIElement crateSlider, cardSlider;
        private static CustomUIElement minorSlider;
        private static CustomUIElement minorCrateSlider;
        private static CustomUIElement debugSlider;
        private static CustomUIElement groundWidthSlider, groundHeightSlider;
        private static bool dragging;
        private static int dragKind;
        private static float lastSlideAt;
        private static int slideCount;
        private static float lastLogAt;

        /// <summary>Builder and entry key of the row currently being built.</summary>
        private static CustomUIBuilder building;
        private static string buildingKey;

        internal static void BeginEntry(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            building = null;
            buildingKey = null;
            try
            {
                if (b == null || config == null || entry == null) return;
                if (config.ModId != Core.Id) return;
                string key = entry.Key;
                if (!IsOurKey(key)) return;

                CustomUIWindow target = b.Handle;
                if (target == null) return;
                if (!ReferenceEquals(target, builtFor))
                {
                    Reset();
                    builtFor = target;
                    window = target;
                }
                building = b;
                buildingKey = key;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "begin entry failed: " + ex);
            }
        }

        private static bool IsOurKey(string key)
        {
            return key == KeyJunk || key == KeyHigh
                || key == KeyCrate || key == KeyCard || key == KeyMinor
                || key == KeyMinorValue
                || key == KeyGroundWidth || key == KeyGroundHeight
                || key == KeyGroundAlign
                || key == Tuning.SpecialtyKey
                || key == Tuning.DebugEnabledKey || key == Tuning.DebugPointsKey;
        }

        /// <summary>
        /// The native row calls SetFixedWidth on the slider right after adding it.
        /// Our postfix appends a value label to that same open row, so the number
        /// sits to the right of the slider instead of taking a line of its own.
        /// </summary>
        internal static void AttachValueLabel(CustomUIElement element)
        {
            try
            {
                if (element == null || building == null || buildingKey == null) return;
                if (buildingKey != KeyJunk && buildingKey != KeyHigh
                    && buildingKey != KeyCrate && buildingKey != KeyCard
                    && buildingKey != KeyMinor && buildingKey != KeyMinorValue
                    && buildingKey != KeyGroundWidth && buildingKey != KeyGroundHeight
                    && buildingKey != Tuning.DebugPointsKey) return;
                if (!ReferenceEquals(element, building.Last)) return;
                if (element.slider == null) return;

                string key = buildingKey;
                // Cleared while we build the label so our own SetFixedWidth call below
                // cannot re-enter this postfix.
                buildingKey = null;
                building.AddLabel("", "dgm_value_" + key);
                CustomUIElement label = building.Last;
                // Fixed width with flexibleWidth 0 so the row keeps the slider where
                // the native layout put it and the number just sits to its right.
                // The debug row only prints "19.5", so a narrower label is enough
                // and leaves room next to the 224px slider.
                label.SetFixedWidth(key == Tuning.DebugPointsKey
                    || key == KeyGroundWidth || key == KeyGroundHeight ? 56f : 72f);
                if (label.text != null)
                {
                    label.text.alignment = TextAlignmentOptions.MidlineLeft;
                    label.text.color = Picker.Ink;
                }
                if (key == KeyJunk) junkValue = label;
                else if (key == KeyHigh) highValue = label;
                else if (key == KeyCrate) crateValue = label;
                else if (key == KeyCard) cardValue = label;
                else if (key == KeyMinor) minorValue = label;
                else if (key == KeyMinorValue) minorCrateValue = label;
                else if (key == KeyGroundWidth) groundWidthValue = label;
                else if (key == KeyGroundHeight) groundHeightValue = label;
                else debugValue = label;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "value label failed: " + ex);
            }
        }

        /// <summary>Called from the BuildEntry postfix, after the native row is closed.</summary>
        internal static void Inject(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            try
            {
                if (b == null || config == null || entry == null) return;
                if (config.ModId != Core.Id) return;
                string key = entry.Key;
                if (!IsOurKey(key)) return;
                if (window == null) window = b.Handle;
                if (window == null) return;

                if (key == KeyJunk)
                {
                    junkSlider = HookSlider(window, key, 1);
                    junkValue = EnsureValueLine(b, key, junkValue);
                }
                else if (key == KeyHigh)
                {
                    highSlider = HookSlider(window, key, 2);
                    highValue = EnsureValueLine(b, key, highValue);
                }
                else if (key == KeyCrate)
                {
                    crateSlider = HookSlider(window, key, 3);
                    crateValue = EnsureValueLine(b, key, crateValue);
                }
                else if (key == KeyCard)
                {
                    cardSlider = HookSlider(window, key, 4);
                    cardValue = EnsureValueLine(b, key, cardValue);
                }
                else if (key == KeyMinor)
                {
                    minorSlider = HookSlider(window, key, 5);
                    minorValue = EnsureValueLine(b, key, minorValue);
                }
                else if (key == KeyMinorValue)
                {
                    minorCrateSlider = HookSlider(window, key, 6);
                    minorCrateValue = EnsureValueLine(b, key, minorCrateValue);
                }
                else if (key == KeyGroundAlign)
                {
                    groundAlignDropdown = b.Last;
                    HookGroundAlignDropdown(groundAlignDropdown, key);
                }
                else if (key == KeyGroundWidth)
                {
                    groundWidthSlider = HookSlider(window, key, 7);
                    groundWidthValue = EnsureValueLine(b, key, groundWidthValue);
                }
                else if (key == KeyGroundHeight)
                {
                    groundHeightSlider = HookSlider(window, key, 8);
                    groundHeightValue = EnsureValueLine(b, key, groundHeightValue);
                    AddGroundPresetBlock(b);
                }
                else if (key == Tuning.SpecialtyKey)
                {
                    specialtyDropdown = b.Last;
                    HookSpecialtyDropdown(specialtyDropdown, key);
                    AddEvBlock(b);
                }
                else if (key == Tuning.DebugEnabledKey)
                {
                    HookDebugToggle(window, key);
                }
                else if (key == Tuning.DebugPointsKey)
                {
                    debugSlider = HookDebugSlider(window, key);
                    debugValue = EnsureValueLine(b, key, debugValue);
                    if (debugValue != null)
                        debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
                }
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "inject failed: " + ex);
            }
            finally
            {
                building = null;
                buildingKey = null;
            }
        }

        /// <summary>
        /// Fallback only: if the row patch did not add a value label, keep the old
        /// separate line so the number is never missing.
        /// </summary>
        private static CustomUIElement EnsureValueLine(CustomUIBuilder b, string key, CustomUIElement current)
        {
            if (current != null) return current;
            b.AddLabel("", "dgm_value_" + key);
            CustomUIElement label = b.Last;
            Picker.Size(label, -1, 22);
            return label;
        }

        private static CustomUIElement HookSlider(CustomUIWindow target, string key, int kind)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.slider == null)
            {
                Debug.LogWarning(LogTag + key + ": slider widget missing, live value disabled");
                return null;
            }
            element.slider.onValueChanged.AddListener(delegate(float value) { OnSlide(kind, value); });
            HookRelease(element, key);
            Debug.Log(LogTag + key + ": live listener bound (min="
                + element.slider.minValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " max=" + element.slider.maxValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " whole=" + element.slider.wholeNumbers + ")");
            return element;
        }

        /// <summary>
        /// PointerUp trigger on the GameObject that owns the Slider component. Putting
        /// it on child graphics would make EventTrigger the press/drag target and the
        /// Slider would never move (regression fixed in 0.3.3).
        /// </summary>
        private static void HookRelease(CustomUIElement element, string key)
        {
            GameObject go = element.slider.gameObject;
            UnityEngine.EventSystems.EventTrigger trigger =
                go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger.triggers == null)
                trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            UnityEngine.EventSystems.EventTrigger.Entry entry =
                new UnityEngine.EventSystems.EventTrigger.Entry();
            entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            entry.callback.AddListener(delegate(UnityEngine.EventSystems.BaseEventData data) { EndDrag(); });
            trigger.triggers.Add(entry);
            Debug.Log(LogTag + key + ": release hook on " + go.name);
        }

        private static void AddEvBlock(CustomUIBuilder b)
        {
            b.AddSpace(6);
            b.AddLabel(Copy.Text("—— 每次拾荒大约能捡到的物品价值（拖动时实时刷新） ——",
                "— Rough value of what one search turns up (live while dragging) —"), "dgm_ev_header");
            CustomUIElement header = b.Last;
            if (header.text != null) header.text.color = Picker.Accent;
            Picker.Size(header, -1, 26);

            // Fixed yardstick above the area rows: the untouched vanilla tables.
            b.AddLabel("", "dgm_ev_reference");
            CustomUIElement reference = b.Last;
            Picker.Size(reference, -1, 24);
            reference.SetText(ReferenceLine());
            if (reference.text != null) reference.text.color = Picker.Ink;

            evLabels = new CustomUIElement[Region.All.Length + 1];
            for (int i = 0; i < evLabels.Length; ++i)
            {
                b.AddLabel("", "dgm_ev_" + i);
                evLabels[i] = b.Last;
                Picker.Size(evLabels[i], -1, 24);
            }

            b.AddSpace(4);
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            PresetButton(b, 0, Copy.Text("默认", "Default"));
            PresetButton(b, 1, Copy.Text("专精拉满", "Specialty max"));
            PresetButton(b, 2, Copy.Text("赌贵重品", "Gamble"));
            b.End();
            b.AddSpace(2);
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            PresetButton(b, 3, Copy.Text("默认 200", "Default 200"));
            PresetButton(b, 4, Copy.Text("贴近原版 100", "Vanilla-ish 100"));
            b.End();
            b.AddSpace(4);
        }

        private static void PresetButton(CustomUIBuilder b, int preset, string text)
        {
            b.AddButton(text, delegate { ApplyPreset(preset); });
            Picker.Size(b.Last, 140, 34);
        }

        /// <summary>「拾荒地面」分组末尾的三个常用尺寸，只写宽高两项。</summary>
        private static void AddGroundPresetBlock(CustomUIBuilder b)
        {
            b.AddSpace(6);
            b.AddLabel(Copy.Text("—— 常用尺寸（点一下同时设置宽和高） ——",
                "— Common sizes (sets width and height together) —"), "dgm_ground_presets");
            CustomUIElement header = b.Last;
            if (header.text != null) header.text.color = Picker.Accent;
            Picker.Size(header, -1, 26);

            b.BeginRow(8f, TextAnchor.MiddleLeft);
            GroundPresetButton(b, 16, 13);
            GroundPresetButton(b, 19, 21);
            GroundPresetButton(b, 19, 31);
            b.End();
            b.AddSpace(4);
        }

        private static void GroundPresetButton(CustomUIBuilder b, int width, int height)
        {
            b.AddButton(width + " × " + height, delegate { ApplyGroundPreset(width, height); });
            Picker.Size(b.Last, 120, 34);
        }

        private static void ApplyGroundPreset(int width, int height)
        {
            EndDrag();
            Tuning.ApplyGroundPreset(width, height);
            Refresh();
        }

        internal static void RefreshIfOpen()
        {
            if (window == null || !window.IsAlive) return;
            Refresh();
        }

        /// <summary>
        /// 调试开关与滑条都常显，不按条件隐藏。原生 BuildEntry 给开关/滑条传的
        /// onChanged 是 null，勾选只改控件、要按「应用」才写回配置；这里自己绑
        /// 事件，让开关即时生效、滑条松手即写入。
        /// </summary>
        private static void HookDebugToggle(CustomUIWindow target, string key)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.toggle == null)
            {
                Debug.LogWarning(LogTag + key + ": toggle widget missing");
                return;
            }
            element.OnToggle(delegate(bool value) { Tuning.SetDebugEnabledLive(value); });
            Debug.Log(LogTag + key + ": live listener bound");
        }

        /// <summary>
        /// 原生下拉框的 onChanged 也是 null，换档只改控件、按「应用」才写回配置。
        /// 这里自己绑 OnDropdown，换档立即写配置项并走同一条刷新链路
        /// （Tuning.Changed → ModLifecycle.RefreshViews → NativeSettings.RefreshIfOpen → Refresh），
        /// 与滑条拖动、预设按钮完全一致。
        /// </summary>
        private static void HookSpecialtyDropdown(CustomUIElement element, string key)
        {
            if (element == null || element.dropdown == null)
            {
                Debug.LogWarning(LogTag + key + ": dropdown widget missing, live EV refresh disabled");
                return;
            }
            element.OnDropdown(delegate(int index)
            {
                Tuning.SetSpecialtyTierLive(index);
                Debug.Log(LogTag + "ev refresh tier=" + Tuning.SpecialtyTier
                    + " percent=" + Tuning.SpecialtyPercent.ToString("0.#", CultureInfo.InvariantCulture)
                    + " source=dropdown");
            });
            Debug.Log(LogTag + key + ": dropdown bound (options="
                + element.dropdown.options.Count + ")");
        }

        private static void HookGroundAlignDropdown(CustomUIElement element, string key)
        {
            if (element == null || element.dropdown == null)
            {
                Debug.LogWarning(LogTag + key + ": dropdown widget missing, alignment fixed at default");
                return;
            }
            element.OnDropdown(delegate(int index)
            {
                Tuning.SetGroundAlignLive(index);
                Debug.Log(LogTag + "ground align=" + Tuning.GroundAlign + " source=dropdown");
            });
            Debug.Log(LogTag + key + ": dropdown bound (options="
                + element.dropdown.options.Count + ", current=" + Tuning.GroundAlign + ")");
        }

        private static CustomUIElement HookDebugSlider(CustomUIWindow target, string key)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.slider == null)
            {
                Debug.LogWarning(LogTag + key + ": slider widget missing");
                return null;
            }
            element.OnSlider(delegate(float value)
            {
                float snapped = Mathf.Round(value * 2f) * 0.5f;
                if (Mathf.Abs(snapped - value) > 0.001f) element.SetFloat(snapped, false);
                if (debugValue != null)
                    debugValue.SetText(snapped.ToString("0.0", CultureInfo.InvariantCulture));
            });
            GameObject go = element.slider.gameObject;
            UnityEngine.EventSystems.EventTrigger trigger =
                go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger.triggers == null)
                trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            UnityEngine.EventSystems.EventTrigger.Entry entry =
                new UnityEngine.EventSystems.EventTrigger.Entry();
            entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            entry.callback.AddListener(delegate(UnityEngine.EventSystems.BaseEventData data)
            {
                CommitDebugSlider();
            });
            trigger.triggers.Add(entry);
            Debug.Log(LogTag + key + ": slider bound (min="
                + element.slider.minValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " max=" + element.slider.maxValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " whole=" + element.slider.wholeNumbers + ")");
            return element;
        }

        /// <summary>松手写入：按 0.5 点吸附，由 Tuning 决定是否因开关关闭而忽略。</summary>
        private static void CommitDebugSlider()
        {
            if (debugSlider == null || debugSlider.slider == null) return;
            float value = debugSlider.slider.value;
            Tuning.SetDebugPointsFromSlider(value);
            // 松手后以真正写入的值为准：开关关闭时会被忽略，标签要退回原值。
            if (debugValue != null)
                debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
            Debug.Log(LogTag + "debug slider release raw="
                + value.ToString("0.###", CultureInfo.InvariantCulture)
                + " entry=" + Tuning.DebugPoints.ToString("0.##", CultureInfo.InvariantCulture)
                + " stored=" + Reputation.ExactPoints.ToString("0.##", CultureInfo.InvariantCulture)
                + " hasStore=" + Reputation.HasStore);
        }

        /// <summary>
        /// Writes any value still held in memory. Called before the native Apply runs
        /// and when the window is rebuilt or the mod is disabled, so a lost PointerUp
        /// can never leave Tuning stuck on an unsaved preview value.
        /// </summary>
        internal static void CommitDrag()
        {
            if (dragging)
            {
                EndDrag();
                return;
            }
            Tuning.CommitLive();
        }

        private static void Refresh()
        {
            if (window == null || !window.IsAlive) return;

            if (junkValue != null)
                junkValue.SetText(Mathf.RoundToInt(Tuning.JunkPercent) + "%");
            if (highValue != null)
                highValue.SetText(Tuning.HighPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (crateValue != null)
                crateValue.SetText(Tuning.CratePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (cardValue != null)
                cardValue.SetText(Tuning.KeycardPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (minorValue != null)
                minorValue.SetText(Tuning.MinorPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (minorCrateValue != null)
                minorCrateValue.SetText(Tuning.MinorValue.ToString("0", CultureInfo.InvariantCulture));
            if (debugValue != null)
                debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
            if (groundWidthValue != null)
                groundWidthValue.SetText(Tuning.GroundWidth.ToString(CultureInfo.InvariantCulture));
            if (groundHeightValue != null)
                groundHeightValue.SetText(Tuning.GroundHeight.ToString(CultureInfo.InvariantCulture));
            if (evLabels == null) return;

            float baseSum = 0f;
            float newSum = 0f;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region region = Region.All[i];
                float before = Tuning.BaseEv(region);
                EffectiveTable table = Tuning.TableFor(region);
                baseSum += before;
                newSum += table.Ev;
                evLabels[i].SetText(Line(region.Name, before, table.Ev));
            }
            float count = Region.All.Length;
            evLabels[Region.All.Length].SetText(Line(
                Copy.Text("各区平均", "all-area average"), baseSum / count, newSum / count));

            SyncWidgets();
        }

        private static string ReferenceLine()
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                float value = Tuning.BaseEv(Region.All[i]);
                if (value < min) min = value;
                if (value > max) max = value;
            }
            // Every area is reverse-solved to the same target, so one compact figure
            // is enough; fall back to a range if rounding ever splits them.
            string valueText = (max - min) <= 0.011f
                ? Copy.Text("各区 ", "every area ") + min.ToString("0.00", CultureInfo.InvariantCulture)
                : min.ToString("0.00", CultureInfo.InvariantCulture) + " – "
                    + max.ToString("0.00", CultureInfo.InvariantCulture);
            return Copy.Text("原版参考（垃圾 100%、无专精）：",
                "Vanilla reference (100% junk, no specialty): ") + valueText;
        }

        private static string Line(string name, float before, float after)
        {
            float change = before > 0f ? (after - before) / before * 100f : 0f;
            return name + "　" + Copy.Text("原版 ", "vanilla ")
                + before.ToString("0.00", CultureInfo.InvariantCulture) + "  →  "
                + Copy.Text("现在 ", "now ")
                + after.ToString("0.00", CultureInfo.InvariantCulture)
                + "（" + (change >= 0f ? "+" : "") + change.ToString("0.0", CultureInfo.InvariantCulture) + "%）";
        }

        private static void OnSlide(int kind, float value)
        {
            if (kind == 1)
            {
                BeginDrag(1, Tuning.JunkPercent);
                float snapped = Mathf.Round(value);
                SyncSlider(junkSlider, snapped);
                Tuning.SetJunkPercentLive(snapped);
                LogSlide(1, value, Tuning.JunkPercent);
            }
            else if (kind == 2)
            {
                BeginDrag(2, Tuning.HighPercent);
                float snapped = Mathf.Round(value * 2f) / 2f;
                SyncSlider(highSlider, snapped);
                Tuning.SetHighPercentLive(snapped);
                LogSlide(2, value, Tuning.HighPercent);
            }
            else if (kind == 3)
            {
                BeginDrag(3, Tuning.CratePercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(crateSlider, snapped);
                Tuning.SetCratePercentLive(snapped);
                LogSlide(3, value, Tuning.CratePercent);
            }
            else if (kind == 4)
            {
                BeginDrag(4, Tuning.KeycardPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(cardSlider, snapped);
                Tuning.SetKeycardPercentLive(snapped);
                LogSlide(4, value, Tuning.KeycardPercent);
            }
            else if (kind == 5)
            {
                BeginDrag(5, Tuning.MinorPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(minorSlider, snapped);
                Tuning.SetMinorPercentLive(snapped);
                LogSlide(5, value, Tuning.MinorPercent);
            }
            else if (kind == 6)
            {
                BeginDrag(6, Tuning.MinorValue);
                float snapped = Mathf.Round(value / 10f) * 10f;
                SyncSlider(minorCrateSlider, snapped);
                Tuning.SetMinorValueLive(snapped);
                LogSlide(6, value, Tuning.MinorValue);
            }
            else if (kind == 7)
            {
                BeginDrag(7, Tuning.GroundWidth);
                float snapped = Mathf.Round(value);
                SyncSlider(groundWidthSlider, snapped);
                Tuning.SetGroundWidthLive((int)snapped);
                LogSlide(7, value, Tuning.GroundWidth);
            }
            else if (kind == 8)
            {
                BeginDrag(8, Tuning.GroundHeight);
                float snapped = Mathf.Round(value);
                SyncSlider(groundHeightSlider, snapped);
                Tuning.SetGroundHeightLive((int)snapped);
                LogSlide(8, value, Tuning.GroundHeight);
            }
        }

        private static void ApplyPreset(int preset)
        {
            EndDrag();
            Tuning.ApplyPreset(preset);
            // The native dropdown owns its own selection; writing the config entry
            // does not move it, so drive the widget explicitly.
            if (specialtyDropdown != null)
                specialtyDropdown.SetIndex(Tuning.SpecialtyTier, false);
            Refresh();
        }

        private static void SyncSlider(CustomUIElement element, float value)
        {
            if (element == null || element.slider == null) return;
            if (Mathf.Abs(element.slider.value - value) > 0.001f) element.SetFloat(value, false);
        }

        private static void SyncWidgets()
        {
            if (dragging) return;
            SyncSlider(junkSlider, Tuning.JunkPercent);
            SyncSlider(highSlider, Tuning.HighPercent);
            SyncSlider(crateSlider, Tuning.CratePercent);
            SyncSlider(cardSlider, Tuning.KeycardPercent);
            SyncSlider(minorSlider, Tuning.MinorPercent);
            SyncSlider(minorCrateSlider, Tuning.MinorValue);
            SyncSlider(groundWidthSlider, Tuning.GroundWidth);
            SyncSlider(groundHeightSlider, Tuning.GroundHeight);
            if (groundAlignDropdown != null)
                groundAlignDropdown.SetIndex(Tuning.GroundAlign, false);
        }

        private static void BeginDrag(int kind, float from)
        {
            float now = Time.realtimeSinceStartup;
            if (!dragging || kind != dragKind || now - lastSlideAt > 0.4f)
            {
                if (dragging) Tuning.CommitLive();
                dragging = true;
                dragKind = kind;
                slideCount = 0;
                lastLogAt = 0f;
                Debug.Log(LogTag + "drag begin kind=" + kind + " from="
                    + from.ToString("0.###", CultureInfo.InvariantCulture));
            }
            lastSlideAt = now;
        }

        private static void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            lastSlideAt = 0f;
            Tuning.CommitLive();
            Debug.Log(LogTag + "drag end kind=" + dragKind + " callbacks=" + slideCount
                + " saved=" + Num(Tuning.JunkPercent) + "/" + Num(Tuning.HighPercent));
            Refresh();
        }

        /// <summary>Throttled trail: one line every 0.25 s plus a callback counter.</summary>
        private static void LogSlide(int kind, float raw, float applied)
        {
            ++slideCount;
            float now = Time.realtimeSinceStartup;
            if (now - lastLogAt < 0.25f) return;
            lastLogAt = now;
            Debug.Log(LogTag + "kind=" + kind
                + " raw=" + raw.ToString("0.###", CultureInfo.InvariantCulture)
                + " applied=" + applied.ToString("0.###", CultureInfo.InvariantCulture)
                + " live=" + (Tuning.HasLive ? 1 : 0)
                + " callbacks=" + slideCount
                + " ev=" + Tuning.TableFor(Region.All[0]).Ev.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static string Num(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        internal static void Reset()
        {
            Tuning.CommitLive();
            if (dragging)
            {
                dragging = false;
                lastSlideAt = 0f;
            }
            window = null;
            builtFor = null;
            junkValue = null;
            highValue = null;
            crateValue = null;
            cardValue = null;
            minorValue = null;
            minorCrateValue = null;
            debugValue = null;
            groundWidthValue = null;
            groundHeightValue = null;
            specialtyDropdown = null;
            groundAlignDropdown = null;
            evLabels = null;
            junkSlider = null;
            highSlider = null;
            crateSlider = null;
            cardSlider = null;
            minorSlider = null;
            minorCrateSlider = null;
            debugSlider = null;
            groundWidthSlider = null;
            groundHeightSlider = null;
            building = null;
            buildingKey = null;
            dragging = false;
            dragKind = 0;
            lastSlideAt = 0f;
            slideCount = 0;
            lastLogAt = 0f;
        }
    }
}
