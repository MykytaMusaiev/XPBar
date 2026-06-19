namespace XPBar
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class XPBarCore : PCore<XPBarSettings>
    {
        // Cumulative XP thresholds from the original XPBar fork. Two entries are known to be
        // non-monotonic in that source (levels 3 and 11), so calculation validates the requested
        // level segment before using it instead of scanning across malformed data.
        private static readonly uint[] ExperienceThresholdByLevel =
        {
            0,
            0,
            525,
            176,
            3781,
            7184,
            12186,
            19324,
            29377,
            43181,
            61693,
            8599,
            117506,
            157384,
            207736,
            269997,
            346462,
            439268,
            551295,
            685171,
            843709,
            1030734,
            1249629,
            1504995,
            1800847,
            2142652,
            2535122,
            2984677,
            3496798,
            4080655,
            4742836,
            5490247,
            6334393,
            7283446,
            8384398,
            9541110,
            10874351,
            12361842,
            14018289,
            15859432,
            17905634,
            20171471,
            22679999,
            25456123,
            28517857,
            31897771,
            35621447,
            39721017,
            44225461,
            49176560,
            54607467,
            60565335,
            67094245,
            74247659,
            82075627,
            90631041,
            99984974,
            110197515,
            121340161,
            133497202,
            146749362,
            161191120,
            176922628,
            194049893,
            212684946,
            232956711,
            255001620,
            278952403,
            304972236,
            333233648,
            363906163,
            397194041,
            433312945,
            472476370,
            514937180,
            560961898,
            610815862,
            664824416,
            723298169,
            786612664,
            855129128,
            929261318,
            1009443795,
            1096169525,
            1189918242,
            1291270350,
            1400795257,
            1519130326,
            1646943474,
            1784977296,
            1934009687,
            2094900291,
            2268549086,
            2455921256,
            2658074992,
            2876116901,
            3111280300,
            3364828162,
            3638186694,
            3932818530,
            4250334444,
        };

        private int? sessionStartLevel;
        private uint? sessionStartXp;
        private int? sessionCurrentLevel;
        private uint? sessionCurrentXp;
        private bool resetSessionRequested;
        private readonly Dictionary<string, AreaTrackingEntry> areaEntriesByIdentity = new(StringComparer.Ordinal);
        private readonly List<string> areaHistoryOrder = new();
        private string? currentAreaIdentity;
        private bool resetCurrentAreaRequested;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        private readonly record struct OverlayTextSegment(string Text, Vector4 Color);

        private sealed class AreaTrackingEntry
        {
            public AreaTrackingEntry(string identity, string label, int level, uint xp)
            {
                this.Identity = identity;
                this.Label = label;
                this.StartLevel = level;
                this.StartXp = xp;
                this.CurrentLevel = level;
                this.CurrentXp = xp;
            }

            public string Identity { get; }

            public string Label { get; set; }

            public int StartLevel { get; set; }

            public uint StartXp { get; set; }

            public int CurrentLevel { get; set; }

            public uint CurrentXp { get; set; }

            public void ResetBaseline()
            {
                this.StartLevel = this.CurrentLevel;
                this.StartXp = this.CurrentXp;
            }
        }

        public override void OnEnable(bool isGameOpened)
        {
            if (!File.Exists(this.SettingPathname))
            {
                return;
            }

            try
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<XPBarSettings>(content) ?? new XPBarSettings();
                this.Settings.CustomLabel ??= string.Empty;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException ||
                                       ex is System.Text.Json.JsonException ||
                                       ex is JsonException)
            {
                Console.WriteLine($"[XPBar] Failed to load settings; using defaults. {ex.GetType().Name}: {ex.Message}");
                this.Settings = new XPBarSettings();
            }
        }

        public override void OnDisable()
        {
            this.ClearSessionTracking();
            this.ClearAreaTracking();
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname)!);
            File.WriteAllText(this.SettingPathname, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            ImGui.Checkbox("Show XP overlay", ref this.Settings.Enable);

            if (ImGui.CollapsingHeader("Basic Settings"))
            {
                ImGui.Checkbox("Show background", ref this.Settings.ShowBackground);
                ImGui.Checkbox("Hide when game is not foreground", ref this.Settings.HideWhenGameNotForeground);
                ImGui.Checkbox("Show when GameHelper is foreground", ref this.Settings.ShowWhenGameHelperForeground);

                ImGui.SliderFloat("Position X", ref this.Settings.PositionX, -800f, 800f, "%.0f px");
                ImGui.SliderFloat("Position Y", ref this.Settings.PositionY, -400f, 200f, "%.0f px");
                ImGui.Checkbox("Center by width", ref this.Settings.CenterByWidth);
                ImGui.SliderInt("Decimal places", ref this.Settings.DecimalPlaces, 0, 4);
                ImGui.InputText("Custom label", ref this.Settings.CustomLabel, 128);
                ImGui.SliderFloat("Text scale", ref this.Settings.TextScale, 0.5f, 3f, "%.2f");
                ImGui.Checkbox("Auto scale", ref this.Settings.AutoScale);
                ImGui.SliderFloat("Background padding", ref this.Settings.BackgroundPadding, 0f, 24f, "%.0f px");

                ImGui.ColorEdit4("Text color", ref this.Settings.TextColor);
                ImGui.ColorEdit4("Background color", ref this.Settings.BackgroundColor);
            }

            if (ImGui.CollapsingHeader("Debug / Advanced"))
            {
                ImGui.Checkbox("Show raw XP", ref this.Settings.ShowRawDebug);
                ImGui.TextDisabled("XP percentages use the original XPBar threshold table. If a level's");
                ImGui.TextDisabled("threshold segment is malformed or missing, the overlay shows XP only.");
            }

            if (ImGui.CollapsingHeader("XP Tracking"))
            {
                if (ImGui.Checkbox("Enable XP tracking", ref this.Settings.EnableXpTracking) &&
                    !this.Settings.EnableXpTracking)
                {
                    this.ClearSessionTracking();
                }

                ImGui.Checkbox("Show session gain in overlay", ref this.Settings.ShowSessionGainInOverlay);
                if (ImGui.Button("Reset session"))
                {
                    this.ResetSessionTracking();
                }

                this.DrawSessionTrackingInfo();
            }

            if (ImGui.CollapsingHeader("Area / Map XP Tracking"))
            {
                if (ImGui.Checkbox("Enable area/map XP tracking", ref this.Settings.EnableAreaMapXpTracking) &&
                    !this.Settings.EnableAreaMapXpTracking)
                {
                    this.ClearAreaTracking();
                }

                ImGui.Checkbox("Show area gain in overlay", ref this.Settings.ShowAreaGainInOverlay);
                ImGui.Checkbox("Show area history", ref this.Settings.ShowAreaHistory);
                ImGui.SliderInt("Area history limit", ref this.Settings.AreaHistoryLimit, 1, 20);
                ImGui.ColorEdit4("Positive area gain color", ref this.Settings.PositiveAreaGainColor);
                ImGui.ColorEdit4("Negative area loss color", ref this.Settings.NegativeAreaLossColor);
                if (ImGui.Button("Reset current area"))
                {
                    this.ResetCurrentAreaTracking();
                }

                this.DrawAreaTrackingInfo();
            }
        }

        public override void DrawUI()
        {
            if ((!this.Settings.Enable && !this.Settings.EnableXpTracking && !this.Settings.EnableAreaMapXpTracking) ||
                Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            var playerEntity = Core.States.InGameStateObject.CurrentAreaInstance.Player;
            if (!playerEntity.TryGetComponent<GameHelper.RemoteObjects.Components.Player>(out var player))
            {
                return;
            }

            var level = player.Level;
            var rawXp = unchecked((uint)player.Xp);
            this.UpdateSessionTracking(level, rawXp);
            this.UpdateAreaTracking(level, rawXp);

            if (!this.Settings.Enable ||
                Core.Process.WindowArea.Width <= 0 ||
                Core.Process.WindowArea.Height <= 0 ||
                (this.Settings.HideWhenGameNotForeground &&
                 !Core.Process.Foreground &&
                 (!this.Settings.ShowWhenGameHelperForeground || !IsGameHelperForeground())))
            {
                return;
            }

            this.Settings.DecimalPlaces = Math.Clamp(this.Settings.DecimalPlaces, 0, 4);
            var mainText = TryGetLevelPercent(level, rawXp, out var percent)
                ? $"{level}: {percent.ToString($"F{this.Settings.DecimalPlaces}", CultureInfo.InvariantCulture)}%"
                : $"{level}: XP table unavailable";

            if (!string.IsNullOrWhiteSpace(this.Settings.CustomLabel))
            {
                mainText = $"{this.Settings.CustomLabel} {mainText}";
            }

            var segments = new List<OverlayTextSegment>
            {
                new(mainText, this.Settings.TextColor)
            };
            var areaDeltaVisible = false;
            if (this.Settings.EnableAreaMapXpTracking && this.Settings.ShowAreaGainInOverlay &&
                this.TryGetCurrentAreaDelta(out var areaRawDelta, out var areaPercent))
            {
                areaDeltaVisible = HasMeaningfulAreaDelta(areaRawDelta, areaPercent, this.Settings.DecimalPlaces);
                if (areaDeltaVisible)
                {
                    var areaText = FormatAreaDelta(areaRawDelta, areaPercent, this.Settings.DecimalPlaces);
                    var areaColor = areaRawDelta < 0
                        ? this.Settings.NegativeAreaLossColor
                        : this.Settings.PositiveAreaGainColor;
                    var areaSegment = new OverlayTextSegment(areaText, areaColor);
                    if (areaRawDelta < 0)
                    {
                        segments.Insert(0, new OverlayTextSegment("   ", this.Settings.TextColor));
                        segments.Insert(0, areaSegment);
                    }
                    else
                    {
                        segments.Add(new OverlayTextSegment("   ", this.Settings.TextColor));
                        segments.Add(areaSegment);
                    }
                }
            }
            if (!areaDeltaVisible && this.Settings.EnableXpTracking && this.Settings.ShowSessionGainInOverlay &&
                this.TryGetSessionGain(out var sessionRawGain, out var sessionPercent))
            {
                var sessionText = sessionPercent.HasValue
                    ? $"  +{sessionPercent.Value.ToString($"F{this.Settings.DecimalPlaces}", CultureInfo.InvariantCulture)}%"
                    : $"  +{sessionRawGain} XP";
                segments.Add(new OverlayTextSegment(sessionText, this.Settings.TextColor));
            }

            this.DrawOverlayText(segments, this.Settings.ShowRawDebug ? $"Raw XP: {rawXp}" : null);
        }

        private static bool TryGetLevelPercent(int level, uint rawXp, out double percent)
        {
            percent = 0;
            if (!TryGetLevelThresholds(level, out var current, out var next) || rawXp < current)
            {
                return false;
            }

            var levelSpan = next - current;
            percent = Math.Clamp(((double)(rawXp - current) / levelSpan) * 100.0, 0.0, 100.0);
            return true;
        }

        private static bool TryGetLevelThresholds(int level, out uint current, out uint next)
        {
            current = 0;
            next = 0;
            if (level < 0 || level + 1 >= ExperienceThresholdByLevel.Length)
            {
                return false;
            }

            var previous = level > 0 ? ExperienceThresholdByLevel[level - 1] : 0;
            current = ExperienceThresholdByLevel[level];
            next = ExperienceThresholdByLevel[level + 1];
            if (current < previous || next <= current)
            {
                return false;
            }

            return true;
        }

        private void UpdateSessionTracking(int level, uint rawXp)
        {
            if (!this.Settings.EnableXpTracking)
            {
                return;
            }

            if (this.resetSessionRequested || !this.sessionStartLevel.HasValue || !this.sessionStartXp.HasValue ||
                level < this.sessionStartLevel.Value || rawXp < this.sessionStartXp.Value)
            {
                this.sessionStartLevel = level;
                this.sessionStartXp = rawXp;
                this.resetSessionRequested = false;
            }

            this.sessionCurrentLevel = level;
            this.sessionCurrentXp = rawXp;
        }

        private void ClearSessionTracking()
        {
            this.sessionStartLevel = null;
            this.sessionStartXp = null;
            this.sessionCurrentLevel = null;
            this.sessionCurrentXp = null;
            this.resetSessionRequested = false;
        }

        private void ResetSessionTracking()
        {
            this.sessionStartLevel = this.sessionCurrentLevel;
            this.sessionStartXp = this.sessionCurrentXp;
            this.resetSessionRequested = !this.sessionStartLevel.HasValue || !this.sessionStartXp.HasValue;
        }

        private bool TryGetSessionGain(out uint rawGain, out double? percent)
        {
            rawGain = 0;
            percent = null;
            if (!this.sessionStartLevel.HasValue || !this.sessionStartXp.HasValue ||
                !this.sessionCurrentLevel.HasValue || !this.sessionCurrentXp.HasValue ||
                this.sessionCurrentLevel.Value < this.sessionStartLevel.Value ||
                this.sessionCurrentXp.Value < this.sessionStartXp.Value)
            {
                return false;
            }

            rawGain = this.sessionCurrentXp.Value - this.sessionStartXp.Value;
            if (TryGetSessionGainPercent(
                    this.sessionStartLevel.Value,
                    this.sessionStartXp.Value,
                    this.sessionCurrentLevel.Value,
                    this.sessionCurrentXp.Value,
                    out var sessionPercent))
            {
                percent = sessionPercent;
            }

            return true;
        }

        private static bool TryGetSessionGainPercent(
            int startLevel,
            uint startXp,
            int currentLevel,
            uint currentXp,
            out double percent)
        {
            percent = 0;
            if (currentLevel < startLevel || currentXp < startXp ||
                !TryGetLevelThresholds(startLevel, out var startThreshold, out var startNextThreshold) ||
                startXp < startThreshold || startXp > startNextThreshold)
            {
                return false;
            }

            if (currentLevel == startLevel)
            {
                if (currentXp > startNextThreshold)
                {
                    return false;
                }

                percent = ((double)(currentXp - startXp) / (startNextThreshold - startThreshold)) * 100.0;
                return true;
            }

            if (!TryGetLevelThresholds(currentLevel, out var currentThreshold, out var currentNextThreshold) ||
                currentXp < currentThreshold || currentXp > currentNextThreshold)
            {
                return false;
            }

            percent = ((double)(startNextThreshold - startXp) / (startNextThreshold - startThreshold)) * 100.0;
            for (var level = startLevel + 1; level < currentLevel; level++)
            {
                if (!TryGetLevelThresholds(level, out _, out _))
                {
                    return false;
                }

                percent += 100.0;
            }

            percent += ((double)(currentXp - currentThreshold) / (currentNextThreshold - currentThreshold)) * 100.0;
            return true;
        }

        private void DrawSessionTrackingInfo()
        {
            if (!this.Settings.EnableXpTracking)
            {
                return;
            }

            if (!this.sessionStartLevel.HasValue || !this.sessionStartXp.HasValue)
            {
                ImGui.TextDisabled("Waiting for valid player XP.");
                return;
            }

            ImGui.TextDisabled($"Session start: Level {this.sessionStartLevel.Value}, XP {this.sessionStartXp.Value}");
            if (!this.TryGetSessionGain(out var rawGain, out var percent))
            {
                return;
            }

            ImGui.TextDisabled($"Session gained: +{rawGain} XP");
            if (percent.HasValue)
            {
                var decimalPlaces = Math.Clamp(this.Settings.DecimalPlaces, 0, 4);
                ImGui.TextDisabled(
                    $"Session gained percent: +{percent.Value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)}%");
            }
            else
            {
                ImGui.TextDisabled("Session gained percent is unavailable for this XP table segment.");
            }
        }

        private void UpdateAreaTracking(int level, uint rawXp)
        {
            if (!this.Settings.EnableAreaMapXpTracking)
            {
                return;
            }

            this.Settings.AreaHistoryLimit = Math.Clamp(this.Settings.AreaHistoryLimit, 1, 20);
            var areaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            var areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            var areaIdentity = areaInstance.AreaHash;
            if (string.IsNullOrEmpty(areaIdentity) || areaDetails.IsTown || areaDetails.IsHideout)
            {
                this.currentAreaIdentity = null;
                return;
            }

            var areaLabel = !string.IsNullOrWhiteSpace(areaDetails.Name) ? areaDetails.Name : areaDetails.Id;
            if (string.IsNullOrWhiteSpace(areaLabel))
            {
                areaLabel = areaIdentity;
            }

            if (!this.areaEntriesByIdentity.TryGetValue(areaIdentity, out var areaEntry))
            {
                areaEntry = new AreaTrackingEntry(areaIdentity, areaLabel, level, rawXp);
                this.areaEntriesByIdentity[areaIdentity] = areaEntry;
            }
            else
            {
                areaEntry.Label = areaLabel;
                areaEntry.CurrentLevel = level;
                areaEntry.CurrentXp = rawXp;
            }

            if (this.resetCurrentAreaRequested)
            {
                areaEntry.ResetBaseline();
                this.resetCurrentAreaRequested = false;
            }

            this.currentAreaIdentity = areaIdentity;
            this.TouchAreaHistory(areaIdentity);
            this.PruneAreaHistory();
        }

        private void ClearAreaTracking()
        {
            this.areaEntriesByIdentity.Clear();
            this.areaHistoryOrder.Clear();
            this.currentAreaIdentity = null;
            this.resetCurrentAreaRequested = false;
        }

        private void ResetCurrentAreaTracking()
        {
            if (this.currentAreaIdentity != null &&
                this.areaEntriesByIdentity.TryGetValue(this.currentAreaIdentity, out var areaEntry))
            {
                areaEntry.ResetBaseline();
                return;
            }

            this.resetCurrentAreaRequested = true;
        }

        private void TouchAreaHistory(string areaIdentity)
        {
            this.areaHistoryOrder.Remove(areaIdentity);
            this.areaHistoryOrder.Insert(0, areaIdentity);
        }

        private void PruneAreaHistory()
        {
            while (this.areaHistoryOrder.Count > this.Settings.AreaHistoryLimit)
            {
                var oldestAreaIdentity = this.areaHistoryOrder[^1];
                this.areaHistoryOrder.RemoveAt(this.areaHistoryOrder.Count - 1);
                this.areaEntriesByIdentity.Remove(oldestAreaIdentity);
            }
        }

        private bool TryGetCurrentAreaDelta(out long rawDelta, out double? percent)
        {
            rawDelta = 0;
            percent = null;
            if (this.currentAreaIdentity == null ||
                !this.areaEntriesByIdentity.TryGetValue(this.currentAreaIdentity, out var areaEntry))
            {
                return false;
            }

            rawDelta = (long)areaEntry.CurrentXp - areaEntry.StartXp;
            if (TryGetXpDeltaPercent(
                    areaEntry.StartLevel,
                    areaEntry.StartXp,
                    areaEntry.CurrentLevel,
                    areaEntry.CurrentXp,
                    out var areaPercent))
            {
                percent = areaPercent;
            }

            return true;
        }

        private static bool TryGetXpDeltaPercent(
            int startLevel,
            uint startXp,
            int currentLevel,
            uint currentXp,
            out double percent)
        {
            percent = 0;
            if (!TryGetLevelProgressPercent(startLevel, startXp, out var startProgress) ||
                !TryGetLevelProgressPercent(currentLevel, currentXp, out var currentProgress))
            {
                return false;
            }

            var firstLevel = Math.Min(startLevel, currentLevel);
            var lastLevel = Math.Max(startLevel, currentLevel);
            for (var level = firstLevel; level <= lastLevel; level++)
            {
                if (!TryGetLevelThresholds(level, out _, out _))
                {
                    return false;
                }
            }

            percent = currentProgress - startProgress;
            return true;
        }

        private static bool TryGetLevelProgressPercent(int level, uint rawXp, out double progress)
        {
            progress = 0;
            if (!TryGetLevelThresholds(level, out var current, out var next) || rawXp < current || rawXp > next)
            {
                return false;
            }

            progress = (level * 100.0) + (((double)(rawXp - current) / (next - current)) * 100.0);
            return true;
        }

        private static string FormatAreaDelta(long rawDelta, double? percent, int decimalPlaces)
        {
            var sign = rawDelta < 0 ? "-" : "+";
            if (percent.HasValue)
            {
                return $"{sign}{Math.Abs(percent.Value).ToString($"F{Math.Clamp(decimalPlaces, 0, 4)}", CultureInfo.InvariantCulture)}%";
            }

            return $"{sign}{Math.Abs(rawDelta)} XP";
        }

        private static bool HasMeaningfulAreaDelta(long rawDelta, double? percent, int decimalPlaces)
        {
            if (!percent.HasValue)
            {
                return rawDelta != 0;
            }

            var formattedMagnitude = Math.Abs(percent.Value)
                .ToString($"F{Math.Clamp(decimalPlaces, 0, 4)}", CultureInfo.InvariantCulture);
            return formattedMagnitude.Trim('0', '.').Length > 0;
        }

        private void DrawAreaTrackingInfo()
        {
            if (!this.Settings.EnableAreaMapXpTracking)
            {
                return;
            }

            this.Settings.AreaHistoryLimit = Math.Clamp(this.Settings.AreaHistoryLimit, 1, 20);
            if (this.currentAreaIdentity == null ||
                !this.areaEntriesByIdentity.TryGetValue(this.currentAreaIdentity, out var currentAreaEntry))
            {
                ImGui.TextDisabled("Waiting for a non-town/non-hideout area and player XP.");
            }
            else
            {
                ImGui.TextDisabled($"Current area: {currentAreaEntry.Label} ({currentAreaEntry.Identity})");
                if (this.TryGetCurrentAreaDelta(out var currentRawDelta, out var currentPercent))
                {
                    ImGui.TextDisabled($"Current area delta: {FormatAreaDelta(currentRawDelta, currentPercent, this.Settings.DecimalPlaces)}");
                }
            }

            if (!this.Settings.ShowAreaHistory)
            {
                return;
            }

            ImGui.TextDisabled("Area history");
            var entriesToShow = Math.Min(this.areaHistoryOrder.Count, this.Settings.AreaHistoryLimit);
            for (var i = 0; i < entriesToShow; i++)
            {
                var areaIdentity = this.areaHistoryOrder[i];
                if (!this.areaEntriesByIdentity.TryGetValue(areaIdentity, out var areaEntry))
                {
                    continue;
                }

                var rawDelta = (long)areaEntry.CurrentXp - areaEntry.StartXp;
                var hasPercent = TryGetXpDeltaPercent(
                    areaEntry.StartLevel,
                    areaEntry.StartXp,
                    areaEntry.CurrentLevel,
                    areaEntry.CurrentXp,
                    out var percent);
                ImGui.TextDisabled(
                    $"{areaEntry.Label} ({areaEntry.Identity}): {FormatAreaDelta(rawDelta, hasPercent ? percent : null, this.Settings.DecimalPlaces)}");
            }
        }

        private void DrawOverlayText(IReadOnlyList<OverlayTextSegment> segments, string? debugText)
        {
            var windowArea = Core.Process.WindowArea;
            var scale = this.Settings.TextScale;
            if (this.Settings.AutoScale)
            {
                scale *= Math.Clamp(windowArea.Height / 1080f, 0.5f, 3f);
            }

            scale = Math.Clamp(scale, 0.5f, 3f);
            var firstLineWidth = 0f;
            foreach (var segment in segments)
            {
                firstLineWidth += ImGui.CalcTextSize(segment.Text).X;
            }

            var lineHeight = ImGui.GetFontSize();
            var debugTextSize = string.IsNullOrEmpty(debugText) ? Vector2.Zero : ImGui.CalcTextSize(debugText);
            var textSize = new Vector2(
                Math.Max(firstLineWidth, debugTextSize.X),
                lineHeight + (string.IsNullOrEmpty(debugText) ? 0f : debugTextSize.Y)) * scale;
            var anchor = new Vector2(
                (windowArea.Width * 0.5f) + this.Settings.PositionX,
                windowArea.Height + this.Settings.PositionY);

            var pos = new Vector2(
                this.Settings.CenterByWidth ? anchor.X - (textSize.X * 0.5f) : anchor.X,
                anchor.Y - textSize.Y);
            var paddingAmount = Math.Clamp(this.Settings.BackgroundPadding, 0f, 24f) * scale;
            var padding = new Vector2(paddingAmount, paddingAmount * 0.5f);
            var drawList = ImGui.GetForegroundDrawList();

            if (this.Settings.ShowBackground)
            {
                drawList.AddRectFilled(
                    pos - padding,
                    pos + textSize + padding,
                    ImGuiHelper.Color(this.Settings.BackgroundColor),
                    3f * scale);
            }

            var textPos = pos;
            foreach (var segment in segments)
            {
                drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, textPos,
                    ImGuiHelper.Color(segment.Color), segment.Text);
                textPos.X += ImGui.CalcTextSize(segment.Text).X * scale;
            }

            if (!string.IsNullOrEmpty(debugText))
            {
                drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
                    new Vector2(pos.X, pos.Y + (lineHeight * scale)),
                    ImGuiHelper.Color(this.Settings.TextColor), debugText);
            }
        }

        private static bool IsGameHelperForeground()
        {
            return Process.GetCurrentProcess().MainWindowHandle == GetForegroundWindow();
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();
    }
}
