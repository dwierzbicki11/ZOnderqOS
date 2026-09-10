namespace ZonderqOS
{
    public static class SecurityContext
    {
        // Boot-time services still start with root context so filesystem/security
        // initialization can complete. Kernel switches this context to the login
        // state before exposing a shell to the user.
        public static string CurrentUser { get; set; } = "root";
        public static string CurrentHome { get; set; } = "/root";
        public static int CurrentUid { get; set; } = 0;
        public static bool IsAuthenticated { get; internal set; }

        internal static void EnterLoginState()
        {
            CurrentUser = "unauthenticated";
            CurrentHome = "/";
            CurrentUid = -1;
            IsAuthenticated = false;
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
