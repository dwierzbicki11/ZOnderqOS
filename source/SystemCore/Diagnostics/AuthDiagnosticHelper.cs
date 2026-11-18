using System;

namespace ZonderqOS.SystemCore.Diagnostics
{
    public static class AuthDiagnosticHelper
    {
        /// <summary>
        /// Read-only session identity diagnostic. No synthetic account,
        /// password guessing, login attempts, or account DB mutation.
        /// Full Lock/Unlock regression is reserved for disposable QEMU E2E.
        /// </summary>
        public static (bool ok, string reason, string metrics) CheckSessionReadOnly()
        {
            try
            {
                string user = global::ZonderqOS.SecurityContext.CurrentUser;
                bool authenticated = global::ZonderqOS.SecurityContext.IsAuthenticated;
                bool present = authenticated && !string.IsNullOrEmpty(user) &&
                               global::ZonderqOS.UserManager.UserExists(user);

                return (present,
                    present ? "Authenticated session exists in account database"
                            : "No consistent authenticated session",
                    present ? "session=authenticated,db=present"
                            : "session=invalid");
            }
            catch (Exception ex)
            {
                return (false, "Auth read-only check failed: " + ex.Message, "error");
            }
        }
    }
}
