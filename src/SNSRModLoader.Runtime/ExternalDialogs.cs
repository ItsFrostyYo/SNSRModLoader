using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace SNSRModLoader.Runtime
{
    internal static class ExternalDialogs
    {
        private static readonly object gate = new object();
        internal static void Show(string[] messages)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                // Serialize native dialogs and keep Unity's main thread running.
                lock (gate)
                {
                    var pages = new List<string>();
                    var page = "";
                    foreach (var message in messages)
                    {
                        if (page.Length > 0 && page.Length + message.Length > 2400) { pages.Add(page); page = ""; }
                        page += message + "\n\n";
                    }
                    if (page.Length > 0) pages.Add(page);
                    for (int i = 0; i < pages.Count; i++)
                        MessageBoxW(IntPtr.Zero, pages[i] + "Full details: Mods/Loader/loader.log", "SNSRModLoader warnings" + (pages.Count > 1 ? " (" + (i + 1) + "/" + pages.Count + ")" : ""), 0x30);
                }
            });
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr window, string message, string caption, uint flags);
    }
}
