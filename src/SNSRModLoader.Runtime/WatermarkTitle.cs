namespace SNSRModLoader.Runtime
{
    internal static class WatermarkTitle
    {
        internal static string Format(bool checksEnabled, bool verified, bool uce)
        {
            if (checksEnabled && verified)
                return "SNSRModLoader <color=#00FF00>(Mods Verified)</color>";
            if (checksEnabled && uce)
                return "SNSRModLoader <color=#FFFF00>(Mods Verified for UCE)</color>";
            return "SNSRModLoader <color=#FF0000>(Mods Not Verified)</color>";
        }
    }
}
