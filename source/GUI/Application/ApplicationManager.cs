using System.Collections.Generic;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Apps
{
    public class ApplicationManager
    {
        private readonly List<Application> applications = new List<Application>();

        public List<Application> Applications { get { return applications; } }

        public Application ActiveApplication
        {
            get
            {
                for (int i = applications.Count - 1; i >= 0; i--)
                {
                    Application application = applications[i];
                    if (application != null && application.IsRunning && application.Window != null &&
                        application.Window.Visible && !application.Window.IsMinimized)
                        return application;
                }
                return null;
            }
        }

        public bool HasLiveTelemetryWindow
        {
            get
            {
                for (int i = 0; i < applications.Count; i++)
                {
                    Application application = applications[i];
                    if (application is TaskManagerModernApp && application.IsRunning &&
                        application.Window != null && application.Window.Visible &&
                        !application.Window.IsMinimized)
                        return true;
                }
                return false;
            }
        }

        public void Launch(Application application)
        {
            if (application == null || !application.IsRunning)
                return;

            // Never create a new interactive window after the authentication state has
            // already been torn down by logout/account switching.
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return;

            // A launcher may return an already-running application instance.
            // Re-adding it would render the same window twice and break MRU,
            // taskbar pagination and Alt+Tab ordering.
            if (!applications.Contains(application))
                applications.Add(application);
            Activate(application);
        }

        public void Activate(Application application)
        {
            if (application == null || !application.IsRunning || application.Window == null)
                return;

            // Focusing an already-visible window must not restart its opening
            // animation or reset its layout on every mouse click.
            if (application.Window.IsMinimized)
                application.Window.RestoreFromMinimized();
            applications.Remove(application);
            applications.Add(application);
            SetActiveStates(application);
        }

        public void ToggleMinimize(Application application)
        {
            if (application == null || !application.IsRunning || application.Window == null)
                return;

            if (application.Window.IsMinimized)
            {
                // Activate performs the restore exactly once.
                Activate(application);
                return;
            }

            if (ActiveApplication == application)
            {
                application.Window.Minimize();
                SetActiveStates(ActiveApplication);
            }
            else
            {
                Activate(application);
            }
        }

        public void Close(Application application)
        {
            if (application == null)
                return;

            application.Close();
            applications.Remove(application);
            SetActiveStates(ActiveApplication);
        }

        /// <summary>
        /// Closes every window owned by the current desktop session. Used on logout so
        /// another authenticated user cannot inherit paths, documents or application state.
        /// </summary>
        public void CloseAll()
        {
            for (int i = applications.Count - 1; i >= 0; i--)
            {
                Application application = applications[i];
                if (application != null && application.IsRunning)
                    application.Close();
            }

            applications.Clear();
            SetActiveStates(null);
        }

        /// <summary>
        /// Rotate through running desktop windows without allocating a switcher list.
        /// Minimized windows participate, and are restored only when selected.
        /// Reverse = Alt+Shift+Tab, forward = Alt+Tab.
        /// </summary>
        public bool CycleWindow(bool reverse)
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return false;

            Application current = ActiveApplication;
            if (current == null)
            {
                // The user may have minimized every window.
                for (int i = applications.Count - 1; i >= 0; i--)
                {
                    Application candidate = applications[i];
                    if (CanSwitchTo(candidate))
                    {
                        Activate(candidate);
                        return true;
                    }
                }
                return false;
            }

            int currentIndex = applications.IndexOf(current);
            int count = applications.Count;
            for (int distance = 1; distance < count; distance++)
            {
                int nextIndex = (currentIndex + (reverse ? distance : count - distance)) % count;
                Application candidate = applications[nextIndex];
                if (!CanSwitchTo(candidate))
                    continue;

                if (!reverse)
                {
                    // Push the former foreground window to the bottom of the
                    // MRU ring before selecting the next one. Otherwise repeated
                    // Alt+Tab bounces between the same two windows.
                    applications.RemoveAt(currentIndex);
                    applications.Insert(0, current);
                }

                Activate(candidate);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Requests a normal title-bar close, preserving app-specific unsaved
        /// document confirmation (e.g. Notepad). Never bypass CloseAction.
        /// </summary>
        public bool RequestCloseActiveWindow()
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return false;

            Application active = ActiveApplication;
            if (active == null)
                return false;

            if (active.Window.CloseAction != null)
                active.Window.CloseAction();
            else
                active.Close();

            // Keep the application in the list if it opened a confirmation
            // dialog instead of closing, just like a mouse title-bar click.
            if (!active.IsRunning)
            {
                applications.Remove(active);
                SetActiveStates(ActiveApplication);
            }
            return true;
        }

        private static bool CanSwitchTo(Application application)
        {
            return application != null && application.IsRunning && application.Window != null &&
                (application.Window.Visible || application.Window.IsMinimized);
        }

        public void HandleKeyboard(KeyEvent key)
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return;
            ActiveApplication?.HandleKeyboard(key);
        }

        public void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            HandleMouse(mouseX, mouseY, isClicked, wasClicked, false, false);
        }

        public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return;

            Application target = ActiveApplication;

            // A fresh press outside every window belongs to the desktop or
            // taskbar, never to the active application's client controls.
            // Keep forwarding motion and release events to the active window
            // so a drag can finish even after the pointer leaves its bounds.
            bool newPress = (leftClicked && !leftWasClicked) ||
                (rightClicked && !rightWasClicked);
            if (newPress)
                target = null;

            // Both mouse buttons must focus the window under the pointer.
            // Otherwise right-clicking a background File Manager could send
            // its context-menu event to the unrelated foreground window.
            if (newPress)
            {
                for (int i = applications.Count - 1; i >= 0; i--)
                {
                    Application application = applications[i];
                    if (application == null || !application.IsRunning || application.Window == null)
                        continue;

                    if (application.Window.ContainsPoint(mouseX, mouseY))
                    {
                        target = application;
                        Activate(application);
                        break;
                    }
                }
            }

            target?.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);
        }

        public void Update()
        {
            // This also catches a session ended from inside an application (for example the
            // Accounts tool). Closing happens before any further app update can run.
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
            {
                CloseAll();
                return;
            }

            bool removed = false;
            for (int i = applications.Count - 1; i >= 0; i--)
            {
                Application application = applications[i];
                if (application == null || !application.IsRunning || application.Window == null)
                {
                    applications.RemoveAt(i);
                    removed = true;
                }
                else
                {
                    application.Update();
                }
            }

            if (removed)
                SetActiveStates(ActiveApplication);
        }

        public void Render(Canvas canvas)
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return;

            for (int i = 0; i < applications.Count; i++)
            {
                Application application = applications[i];
                if (application != null && application.IsRunning && application.Window != null &&
                    application.Window.Visible && !application.Window.IsMinimized)
                    application.Render(canvas);
            }
        }

        private void SetActiveStates(Application active)
        {
            for (int i = 0; i < applications.Count; i++)
            {
                Application application = applications[i];
                if (application == null || application.Window == null)
                    continue;

                application.Window.IsActive = application == active && application.IsRunning &&
                    application.Window.Visible && !application.Window.IsMinimized;
            }
        }
    }
}
