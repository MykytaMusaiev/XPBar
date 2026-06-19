namespace XPBar
{
    using System.Numerics;
    using GameHelper.Plugin;

    public sealed class XPBarSettings : IPSettings
    {
        public bool Enable = true;
        public bool ShowBackground = true;
        public bool ShowRawDebug = false;
        public bool HideWhenGameNotForeground = true;
        public bool ShowWhenGameHelperForeground = true;
        public bool CenterByWidth = true;
        public float PositionX = 0f;
        public float PositionY = -28f;
        public int DecimalPlaces = 3;
        public string CustomLabel = string.Empty;
        public float TextScale = 1f;
        public bool AutoScale = true;
        public float BackgroundPadding = 6f;
        public Vector4 TextColor = new(1f, 1f, 1f, 1f);
        public Vector4 BackgroundColor = new(0f, 0f, 0f, 0.72f);
    }
}
