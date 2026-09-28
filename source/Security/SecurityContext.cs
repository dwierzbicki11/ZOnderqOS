namespace ZonderqOS
{
    public static class SecurityContext
    {
        // Boot-time services start with root identity only so filesystem/security
        // initialization can complete. The context can be changed only by the
        // authenticated session code below; ordinary applications cannot assign UID/user.
        public static string CurrentUser { get; private set; } = "root";
        public static string CurrentHome { get; private set; } = "/root";
        public static int CurrentUid { get; private set; } = 0;
        public static bool IsAuthenticated { get; private set; }

        internal static void EnterLoginState()
        {
#if ZONDERQ_NETWORK_N9_GUI_REGRESSION
            // The N9 regression image must keep the real desktop render loop alive long
            // enough for QEMU to sample it. This compile-time-only identity is never part
            // of a normal image and bypasses no production authentication path.
            CurrentUser = "n9-gui-regression";
            CurrentHome = "/";
            CurrentUid = 0;
            IsAuthenticated = true;
#else
            CurrentUser = "unauthenticated";
            CurrentHome = "/";
            CurrentUid = -1;
            IsAuthenticated = false;
#endif
        }

        internal static void SetAuthenticated(string user, string home, int uid)
        {
            CurrentUser = user;
            CurrentHome = home;
            CurrentUid = uid;
            IsAuthenticated = true;
        }
    }
}
