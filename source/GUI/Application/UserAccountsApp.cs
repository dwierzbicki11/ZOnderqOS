using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class UserAccountsApp : SettingsToolWindow
    {
        private readonly string[] users = new string[32];
        private readonly int[] uids = new int[32];
        private readonly string[] homes = new string[32];
        private int userCount;
        private int selectedUser;

        // 0 none, 1 new username, 2 new-user password, 3 switch password,
        // 4 authorize password change, 5 new password, 6 confirm new password.
        private int inputMode;
        private readonly char[] inputChars = new char[global::ZonderqOS.PasswordPolicy.MaxLength];
        private int inputLength;
        private string cachedUsernameDisplay = string.Empty;
        private string pendingUsername = string.Empty;
        private readonly char[] pendingAuthChars = new char[global::ZonderqOS.PasswordPolicy.MaxLength];
        private int pendingAuthLength;
        private readonly char[] pendingNewPassChars = new char[global::ZonderqOS.PasswordPolicy.MaxLength];
        private int pendingNewPassLength;

        public UserAccountsApp(int x, int y)
            : base("Local Accounts", "Accounts and user sessions - ZOnderqOS", x, y, 900, 724)
        {
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            string selectedName = userCount > 0 ? users[selectedUser] : "NONE";
            DrawRow(canvas, 0, IconType.About,
                "SELECTED ACCOUNT", "Click to select the next local account",
                selectedName, Accent);
            DrawNumericRow(canvas, 1, IconType.About,
                "UID", "Selected account identifier",
                userCount > 0 ? (ulong)System.Math.Max(0, uids[selectedUser]) : 0UL, "");
            DrawRow(canvas, 2, IconType.Folder,
                "HOME DIRECTORY", "Selected user's home directory",
                userCount > 0 ? homes[selectedUser] : "NONE", Text);
            DrawRow(canvas, 3, IconType.Settings,
                "ADD USER", "Root-only: enter a username and password",
                inputMode == 1 || inputMode == 2 ? "ENTERING" : "CREATE",
                IsRootSession() ? Good : Muted);
            DrawRow(canvas, 4, IconType.Start,
                "SWITCH USER", "Confirm account password; the current session will end safely",
                inputMode == 3 ? "PASSWORD..." : "SWITCH", Warning);
            DrawRow(canvas, 5, IconType.Settings,
                "CHANGE PASSWORD", "Change your own password or reset one as root after verification",
                inputMode >= 4 ? "ENTERING" : CanChangeSelectedPassword() ? "CHANGE" : "UNAVAILABLE",
                CanChangeSelectedPassword() ? Good : Muted);
            DrawRow(canvas, 6, IconType.Settings,
                "AUTO LOCK", "Lock the desktop when idle; click to change",
                global::ZonderqOS.SystemSettings.AutoLockName,
                global::ZonderqOS.SystemSettings.AutoLockMinutes > 0 ? Good : Warning);
            DrawNumericRow(canvas, 7, IconType.About,
                "SESSION TIME", "Minutes since last successful login",
                global::ZonderqOS.SessionManager.ElapsedSeconds / 60UL, " MIN");

            if (inputMode != 0)
                RenderInputOverlay(canvas);
        }

        private void RenderInputOverlay(Canvas canvas)
        {
            int width = System.Math.Min(520, Window.Width - 80);
            bool passwordPolicyMode = inputMode == 2 || inputMode == 5 || inputMode == 6;
            int height = passwordPolicyMode ? 132 : 112;
            int x = Window.X + (Window.Width - width) / 2;
            int y = Window.Y + 230;
            canvas.DrawFilledRectangle(Color.FromArgb(18, 23, 29), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);

            string title;
            if (inputMode == 1)
                title = "NEW USER - NAME";
            else if (inputMode == 2)
                title = "NEW USER - PASSWORD";
            else if (inputMode == 3)
                title = "PASSWORD FOR SELECTED ACCOUNT";
            else if (inputMode == 4)
                title = "CONFIRM CURRENT SESSION PASSWORD";
            else if (inputMode == 5)
                title = "NEW PASSWORD";
            else
                title = "REPEAT NEW PASSWORD";

            SmallTextRenderer.Draw(canvas, title, x + 16, y + 16, Text);
            SmallTextRenderer.Draw(canvas, "ENTER NEXT  |  ESC CANCEL", x + 16, y + 36, Muted);

            int inputY = y + 58;
            if (passwordPolicyMode)
            {
                SmallTextRenderer.DrawClipped(canvas, global::ZonderqOS.PasswordPolicy.Summary,
                    x + 16, y + 56, width - 32, Warning);
                inputY = y + 78;
            }

            canvas.DrawFilledRectangle(Color.FromArgb(27, 34, 41), x + 16, inputY, width - 32, 34);
            canvas.DrawRectangle(Border, x + 16, inputY, width - 32, 34);
            if (inputMode == 1)
                SmallTextRenderer.DrawClipped(canvas, cachedUsernameDisplay, x + 26, inputY + 14, width - 54, Text);
            else
                DrawMasked(canvas, inputLength, x + 26, inputY + 14, width - 54);
        }

        private static void DrawMasked(Canvas canvas, int count, int x, int y, int maxWidth)
        {
            int max = System.Math.Min(count, System.Math.Max(0, maxWidth / 6));
            for (int i = 0; i < max; i++)
                SmallTextRenderer.Draw(canvas, "*", x + i * 6, y, Text);
        }

        protected override void RefreshData()
        {
            for (int i = 0; i < users.Length; i++)
            {
                users[i] = null;
                homes[i] = null;
                uids[i] = 0;
            }

            userCount = 0;
            try
            {
                const string path = "/etc/passwd";
                if (!File.Exists(path))
                    return;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length && userCount < users.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split(':');
                    if (parts.Length < 4 || string.IsNullOrEmpty(parts[0]))
                        continue;

                    users[userCount] = parts[0];
                    int uid;
                    uids[userCount] = int.TryParse(parts[2], out uid) ? uid : 0;
                    homes[userCount] = string.IsNullOrEmpty(parts[3]) ? "/" : parts[3];
                    userCount++;
                }
            }
            catch
            {
                userCount = 0;
            }

            if (selectedUser >= userCount)
                selectedUser = userCount > 0 ? userCount - 1 : 0;
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            if (inputMode != 0)
                return;

            int row = HitRow(mouseX, mouseY, 8);
            if (row < 0)
                return;

            if (row == 0)
            {
                if (userCount > 0)
                {
                    selectedUser++;
                    if (selectedUser >= userCount)
                        selectedUser = 0;
                    SetStatus("ACCOUNT SELECTED", Accent);
                }
                return;
            }

            if (row == 3)
            {
                if (!IsRootSession())
                {
                    SetStatus("ONLY AUTHENTICATED ROOT CAN CREATE ACCOUNTS", Danger);
                    return;
                }
                ClearInputState();
                inputMode = 1;
                SetStatus("ENTER NEW USERNAME", Warning);
                return;
            }

            if (row == 4)
            {
                if (userCount <= 0)
                    return;
                ClearInputState();
                inputMode = 3;
                SetStatus("CONFIRM THE PASSWORD FOR THE TARGET ACCOUNT", Warning);
                return;
            }

            if (row == 5)
            {
                if (!CanChangeSelectedPassword())
                {
                    SetStatus("NOT AUTHORIZED TO CHANGE THIS ACCOUNT PASSWORD", Danger);
                    return;
                }

                ClearInputState();
                inputMode = 4;
                SetStatus("CONFIRM CURRENT SESSION PASSWORD", Warning);
                return;
            }

            if (row == 6)
            {
                global::ZonderqOS.SystemSettings.CycleAutoLockTimeout();
                SetStatus("AUTO-LOCK TIMEOUT CHANGED", Good);
                return;
            }

            if (row == 7)
                SetStatus("CURRENT AUTHENTICATED SESSION TIME", Accent);
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (inputMode == 0)
            {
                base.HandleKeyboard(key);
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                ClearInputState();
                SetStatus("CANCELLED", Muted);
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (inputLength > 0)
                {
                    inputChars[--inputLength] = '\0';
                    if (inputMode == 1)
                        cachedUsernameDisplay = inputLength > 0 ? new string(inputChars, 0, inputLength) : string.Empty;
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.Delete)
            {
                ClearCurrentInputBuffer();
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                AcceptInput();
                return;
            }

            char ch = key.KeyChar;
            if (inputMode == 1)
            {
                bool allowed = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                               (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
                if (allowed && inputLength < 32)
                {
                    inputChars[inputLength++] = ch;
                    cachedUsernameDisplay = new string(inputChars, 0, inputLength);
                }
            }
            else if (ch >= 32 && ch <= 126 && inputLength < inputChars.Length)
            {
                inputChars[inputLength++] = ch;
            }
        }

        private void ClearCurrentInputBuffer()
        {
            Array.Clear(inputChars, 0, inputChars.Length);
            inputLength = 0;
            cachedUsernameDisplay = string.Empty;
        }

        private static bool CharBuffersEqual(char[] a, int aLen, char[] b, int bLen)
        {
            int diff = aLen ^ bLen;
            int max = aLen < bLen ? aLen : bLen;
            for (int i = 0; i < max; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private void AcceptInput()
        {
            if (inputMode == 1)
            {
                string enteredName = cachedUsernameDisplay;
                if (string.IsNullOrEmpty(enteredName) || global::ZonderqOS.UserManager.UserExists(enteredName))
                {
                    SetStatus("EMPTY NAME OR ACCOUNT ALREADY EXISTS", Danger);
                    return;
                }

                pendingUsername = enteredName;
                ClearCurrentInputBuffer();
                inputMode = 2;
                SetStatus("ENTER A PASSWORD FOR THE NEW ACCOUNT", Warning);
                return;
            }

            if (inputMode == 2)
            {
                string passStr = inputLength > 0 ? new string(inputChars, 0, inputLength) : string.Empty;
                string reason;
                if (!global::ZonderqOS.PasswordPolicy.Validate(passStr, out reason))
                {
                    passStr = null;
                    SetStatus(reason, Danger);
                    return;
                }

                bool ok = global::ZonderqOS.UserManager.CreateUser(pendingUsername, passStr);
                passStr = null;
                ClearInputState();
                RefreshData();
                SetStatus(ok ? "ACCOUNT CREATED" : "FAILED TO CREATE ACCOUNT", ok ? Good : Danger);
                return;
            }

            if (inputMode == 3)
            {
                string user = userCount > 0 ? users[selectedUser] : null;
                if (string.IsNullOrEmpty(user))
                {
                    ClearInputState();
                    SetStatus("NO ACCOUNT SELECTED", Danger);
                    return;
                }

                int retryAfter;
                if (!global::ZonderqOS.AuthenticationGuard.CanAttempt(user, out retryAfter))
                {
                    ClearCurrentInputBuffer();
                    SetStatus("TOO MANY ATTEMPTS - WAIT " + retryAfter + " S", Danger);
                    return;
                }

                bool ok = global::ZonderqOS.UserManager.ValidateCredentialsChars(user, inputChars, inputLength);
                ClearCurrentInputBuffer();
                if (!ok)
                {
                    global::ZonderqOS.AuthenticationGuard.RecordFailure(user);
                    retryAfter = global::ZonderqOS.AuthenticationGuard.GetRetryAfterSeconds(user);
                    SetStatus(retryAfter > 0
                        ? "INVALID PASSWORD - LOCKED FOR " + retryAfter + " S"
                        : "INVALID PASSWORD", Danger);
                    return;
                }

                global::ZonderqOS.AuthenticationGuard.RecordSuccess(user);
                global::ZonderqOS.UserProfileManager.RememberLastUser(user);
                global::ZonderqOS.SecurityLogger.LogEvent("INFO",
                    "Secure GUI account switch requested to '" + user + "'.");

                // End the old session instead of changing UID under already-open windows.
                // GuiManager sees the unauthenticated state on the next loop and closes all
                // applications before the login screen is shown again.
                ClearInputState();
                global::ZonderqOS.UserManager.EndSession();
                return;
            }

            if (inputMode == 4)
            {
                string actor = global::ZonderqOS.SecurityContext.CurrentUser;
                int retryAfter;
                if (!global::ZonderqOS.AuthenticationGuard.CanAttempt(actor, out retryAfter))
                {
                    ClearCurrentInputBuffer();
                    SetStatus("AUTHENTICATION LOCKED - WAIT " + retryAfter + " S", Danger);
                    return;
                }

                bool authorized = inputLength > 0 &&
                    global::ZonderqOS.UserManager.ValidateCredentialsChars(actor, inputChars, inputLength);
                if (!authorized)
                {
                    ClearCurrentInputBuffer();
                    global::ZonderqOS.AuthenticationGuard.RecordFailure(actor);
                    retryAfter = global::ZonderqOS.AuthenticationGuard.GetRetryAfterSeconds(actor);
                    SetStatus(retryAfter > 0
                        ? "INVALID PASSWORD - LOCKED FOR " + retryAfter + " S"
                        : "INVALID CURRENT SESSION PASSWORD", Danger);
                    return;
                }

                global::ZonderqOS.AuthenticationGuard.RecordSuccess(actor);
                Array.Clear(pendingAuthChars, 0, pendingAuthChars.Length);
                Array.Copy(inputChars, 0, pendingAuthChars, 0, inputLength);
                pendingAuthLength = inputLength;
                ClearCurrentInputBuffer();
                inputMode = 5;
                SetStatus("ENTER NEW PASSWORD", Warning);
                return;
            }

            if (inputMode == 5)
            {
                string candidate = inputLength > 0 ? new string(inputChars, 0, inputLength) : string.Empty;
                string reason;
                bool valid = global::ZonderqOS.PasswordPolicy.Validate(candidate, out reason);
                candidate = null;
                if (!valid)
                {
                    SetStatus(reason, Danger);
                    return;
                }

                Array.Clear(pendingNewPassChars, 0, pendingNewPassChars.Length);
                Array.Copy(inputChars, 0, pendingNewPassChars, 0, inputLength);
                pendingNewPassLength = inputLength;
                ClearCurrentInputBuffer();
                inputMode = 6;
                SetStatus("REPEAT NEW PASSWORD", Warning);
                return;
            }

            if (inputMode == 6)
            {
                if (!CharBuffersEqual(inputChars, inputLength, pendingNewPassChars, pendingNewPassLength))
                {
                    ClearCurrentInputBuffer();
                    Array.Clear(pendingNewPassChars, 0, pendingNewPassChars.Length);
                    pendingNewPassLength = 0;
                    inputMode = 5;
                    SetStatus("PASSWORDS DO NOT MATCH - ENTER A NEW PASSWORD", Danger);
                    return;
                }

                string user = userCount > 0 ? users[selectedUser] : null;
                string authPass = pendingAuthLength > 0 ? new string(pendingAuthChars, 0, pendingAuthLength) : string.Empty;
                string newPass = pendingNewPassLength > 0 ? new string(pendingNewPassChars, 0, pendingNewPassLength) : string.Empty;
                bool ok = !string.IsNullOrEmpty(user) &&
                          global::ZonderqOS.UserManager.ChangePassword(user, authPass, newPass);
                authPass = null;
                newPass = null;
                if (ok)
                    global::ZonderqOS.AuthenticationGuard.Reset(user);
                ClearInputState();
                SetStatus(ok ? "PASSWORD CHANGED" : "FAILED TO CHANGE PASSWORD",
                    ok ? Good : Danger);
            }
        }

        private bool CanChangeSelectedPassword()
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated || userCount <= 0)
                return false;

            string selected = users[selectedUser];
            string current = global::ZonderqOS.SecurityContext.CurrentUser;
            return !string.IsNullOrEmpty(selected) &&
                   (IsRootSession() || current == selected);
        }

        private static bool IsRootSession()
        {
            return global::ZonderqOS.SecurityContext.IsAuthenticated &&
                   global::ZonderqOS.SecurityContext.CurrentUid == 0 &&
                   global::ZonderqOS.SecurityContext.CurrentUser == "root";
        }

        private void ClearInputState()
        {
            inputMode = 0;
            ClearCurrentInputBuffer();
            pendingUsername = string.Empty;
            Array.Clear(pendingAuthChars, 0, pendingAuthChars.Length);
            pendingAuthLength = 0;
            Array.Clear(pendingNewPassChars, 0, pendingNewPassChars.Length);
            pendingNewPassLength = 0;
        }

        public override void Close()
        {
            ClearInputState();
            base.Close();
        }
    }
}