namespace XPBar
{
    using System;
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

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

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
        }

        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname)!);
            File.WriteAllText(this.SettingPathname, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            ImGui.Checkbox("Enable XP overlay", ref this.Settings.Enable);

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
        }

        public override void DrawUI()
        {
            if (!this.Settings.Enable ||
                Core.States.GameCurrentState != GameStateTypes.InGameState ||
                Core.Process.WindowArea.Width <= 0 ||
                Core.Process.WindowArea.Height <= 0)
            {
                return;
            }

            if (this.Settings.HideWhenGameNotForeground &&
                !Core.Process.Foreground &&
                (!this.Settings.ShowWhenGameHelperForeground || !IsGameHelperForeground()))
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
            this.Settings.DecimalPlaces = Math.Clamp(this.Settings.DecimalPlaces, 0, 4);
            var text = TryGetLevelPercent(level, rawXp, out var percent)
                ? $"{level}: {percent.ToString($"F{this.Settings.DecimalPlaces}", CultureInfo.InvariantCulture)}%"
                : $"{level}: XP table unavailable";

            if (!string.IsNullOrWhiteSpace(this.Settings.CustomLabel))
            {
                text = $"{this.Settings.CustomLabel} {text}";
            }

            if (this.Settings.ShowRawDebug)
            {
                text += $"{Environment.NewLine}Raw XP: {rawXp}";
            }

            this.DrawOverlayText(text);
        }

        private static bool TryGetLevelPercent(int level, uint rawXp, out double percent)
        {
            percent = 0;
            if (level < 0 || level + 1 >= ExperienceThresholdByLevel.Length)
            {
                return false;
            }

            var previous = level > 0 ? ExperienceThresholdByLevel[level - 1] : 0;
            var current = ExperienceThresholdByLevel[level];
            var next = ExperienceThresholdByLevel[level + 1];
            if (current < previous || next <= current || rawXp < current)
            {
                return false;
            }

            var levelSpan = next - current;
            percent = Math.Clamp(((double)(rawXp - current) / levelSpan) * 100.0, 0.0, 100.0);
            return true;
        }

        private void DrawOverlayText(string text)
        {
            var windowArea = Core.Process.WindowArea;
            var scale = this.Settings.TextScale;
            if (this.Settings.AutoScale)
            {
                scale *= Math.Clamp(windowArea.Height / 1080f, 0.5f, 3f);
            }

            scale = Math.Clamp(scale, 0.5f, 3f);
            var textSize = ImGui.CalcTextSize(text) * scale;
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

            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos,
                ImGuiHelper.Color(this.Settings.TextColor), text);
        }

        private static bool IsGameHelperForeground()
        {
            return Process.GetCurrentProcess().MainWindowHandle == GetForegroundWindow();
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();
    }
}
