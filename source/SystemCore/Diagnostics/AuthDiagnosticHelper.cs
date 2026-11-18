using System;
using ZonderqOS.Security;

namespace ZonderqOS.SystemCore.Diagnostics
{
    public static class AuthDiagnosticHelper
    {
        private const string TempUser = "_sysbench_tmp_user";

        public static (bool ok, string reason, string metrics) RunIsolatedAuthTest()
        {
            // Runtime-generated ephemeral credential; never logged or stored in reports.
            string tempPass = "Sb#" + Environment.TickCount.ToString() + "!9z";
            string wrongPass = "WrongPass!000";

            try
            {
                if (!UserManager.UserExists(TempUser))
                {
                    if (!UserManager.CreateUser(TempUser, tempPass))
                        return (false, "Nie udalo sie utworzyc izolowanego konta testowego", "create_failed");
                }

                int lockUnlockOk = 0;
                for (int i = 0; i < 50; i++)
                {
                    if (UserManager.ValidateCredentials(TempUser, tempPass))
                        lockUnlockOk++;
                }

                int wrongRejected = 0;
                for (int i = 0; i < 20; i++)
                {
                    if (!UserManager.ValidateCredentials(TempUser, wrongPass))
                        wrongRejected++;
                }

                if (lockUnlockOk == 50 && wrongRejected == 20)
                    return (true, "50 cykli Lock/Unlock + 20 blednych prob odrzuconych", "ok=50/50,rej=20/20");

                return (false, $"Niezgodny wynik auth: ok={lockUnlockOk}/50, rej={wrongRejected}/20", $"ok={lockUnlockOk},rej={wrongRejected}");
            }
            catch (Exception ex)
            {
                return (false, "Wyjatek Auth: " + ex.Message, "error");
            }
        }
    }
}
