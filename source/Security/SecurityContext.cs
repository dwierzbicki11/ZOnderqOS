namespace ZonderqOS
{
    public static class SecurityContext
    {
        // Boot-time services start with root identity only so filesystem/security
        // initialization can complete. The context is sealed once the login screen
        // initializes or a session authenticates, and ordinary callers cannot re-open it.
        private static bool bootInitializationActive = true;

        public static string CurrentUser { get; private set; } = "root";
        public static string CurrentHome { get; private set; } = "/root";
        public static int CurrentUid { get; private set; } = 0;
        public static bool IsAuthenticated { get; private set; }
        internal static bool IsBootInitializationActive => bootInitializationActive && !IsAuthenticated;

        internal static void CompleteBootInitialization()
        {
            bootInitializationActive = false;
        }

        internal static void EnterLoginState()
        {
            CompleteBootInitialization();
            CurrentUser = "unauthenticated";
            CurrentHome = "/";
            CurrentUid = -1;
            IsAuthenticated = false;
        }

        internal static void SetAuthenticated(string user, string home, int uid)
        {
            CompleteBootInitialization();
            CurrentUser = user;
            CurrentHome = home;
            CurrentUid = uid;
            IsAuthenticated = true;
        }

#if ZIG_HOST_TESTS
        internal static void ResetToBootStateForTests()
        {
            bootInitializationActive = true;
            CurrentUser = "root";
            CurrentHome = "/root";
            CurrentUid = 0;
            IsAuthenticated = false;
        }
#endif
    }
}
