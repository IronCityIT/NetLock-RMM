namespace NetLock_RMM_Tray_Icon
{
    // Iron Clad Support: client-facing product name for the tray app (end users see these strings).
    // Policy-provided TrayConfig values still take precedence; these are the fallbacks and the
    // titles upstream hardcoded. Upstream attribution and the license notice stay in the About
    // dialog: AGPL-3.0 section 5(d) requires interactive UIs to keep Appropriate Legal Notices.
    public static class Branding
    {
        public const string Product_Name = "Iron Clad Support";

        public const string Actions_Window_Title = Product_Name + " Actions";

        public const string Chat_Window_Title = Product_Name + " Chat";

        public const string About_Window_Title = "About " + Product_Name;

        public static string Legal_Notice(int year) =>
            $"{Product_Name} is based on NetLock RMM. © {year} 0x101 GmbH. Licensed under the GNU AGPL v3.";
    }
}
