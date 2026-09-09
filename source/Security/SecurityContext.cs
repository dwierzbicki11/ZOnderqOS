namespace ZonderqOS
{
    public static class SecurityContext
    {
        public static string CurrentUser { get; set; } = "root";
        public static string CurrentHome { get; set; } = "/root";
        public static int CurrentUid { get; set; } = 0;
    }
}