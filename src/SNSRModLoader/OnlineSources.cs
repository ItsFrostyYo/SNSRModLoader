using System;

namespace SNSRModLoader
{
    // Published builds embed their own repository endpoints. Config may override them.
    public static class OnlineSources
    {
        public static string Get(string name)
        {
            try { return Registry.ReadEmbedded("online.xml").DocumentElement.GetAttribute(name); }
            catch (InvalidOperationException) { return ""; }
        }
    }
}
