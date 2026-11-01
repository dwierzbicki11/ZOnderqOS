using System.Collections.Generic;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Apps
{
    public class ApplicationManager
    {
        private readonly List<Application> applications = new List<Application>();
        private bool closingAll;
        // Snapshot the frame's application order once. Focus changes may reorder the
        // live MRU list during Update; iterating that list skips or double-updates
        // windows. Reuse the buffer to avoid allocating on every GUI frame.
        private readonly List<Application> updateSnapshot = new List<Application>();
        private bool updating;
        private bool desktopGesture;
        // The window which received the first button-down owns the entire
        // multi-button gesture, even if focus changes or the pointer crosses
        // another window. Null means the desktop/taskbar owns the gesture.
        private Application mouseCapture;

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
            if (closingAll || application == null || !application.IsRunning)
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
            if (closingAll || !global::ZonderqOS.SecurityContext.IsAuthenticated ||
                application == null || !application.IsRunning || application.Window == null ||
                !applications.Contains(application))
                return;

            // Only Launch may register a new window. Activating a stale or
            // never-launched instance must not resurrect it after logout.
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
            if (closingAll || !global::ZonderqOS.SecurityContext.IsAuthenticated ||
                application == null || !application.IsRunning || application.Window == null ||
                !applications.Contains(application))
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
            // Detach first. A Close callback may close another window, re-enter
            // Close for the same instance, or tear down the entire session.
            // Never invoke an unregistered app's Close callback twice.
            if (application == null || !applications.Remove(application))
                return;

            // A closing window must never hand its held-button release to the
            // newly exposed foreground window.
            if (mouseCapture == application)
            {
                mouseCapture = null;
                desktopGesture = true;
            }

            if (application.IsRunning)
                application.Close();
            SetActiveStates(ActiveApplication);
        }

        /// <summary>
        /// Closes every window owned by the current desktop session. Used on logout so
        /// another authenticated user cannot inherit paths, documents or application state.
        /// </summary>
        public void CloseAll()
        {
            // A closing app may invoke another CloseAll or try to launch/focus
            // a replacement window. Block those actions during session teardown:
            // otherwise a Close callback can keep appending windows forever.
            if (closingAll)
                return;

            closingAll = true;
            try
            {
                // Detach before invoking callbacks; no temporary list is needed.
                while (applications.Count > 0)
                {
                    int last = applications.Count - 1;
                    Application application = applications[last];
                    applications.RemoveAt(last);
                    if (application != null && application.IsRunning)
                        application.Close();
                }
            }
            finally
            {
                closingAll = false;
                desktopGesture = false;
                mouseCapture = null;
                SetActiveStates(null);
            }
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
            bool rightClicked, bool rightWasClicked, bool physicalWasHeld = false)
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                return;

            Application target = ActiveApplication;
            bool newPress = (leftClicked && !leftWasClicked) ||
                (rightClicked && !rightWasClicked);
            // GuiManager suppresses left clicks while right is down. Preserve
            // the physical previous-button state for capture ownership even
            // when the sanitized UI states look like a fresh right press.
            bool wasHeld = physicalWasHeld || leftWasClicked || rightWasClicked;
            bool isHeld = leftClicked || rightClicked;

            // A second button pressed during an existing desktop gesture
            // cannot retarget the chord to a window underneath the pointer.
            if (desktopGesture && wasHeld)
            {
                if (!isHeld)
                    desktopGesture = false;
                return;
            }

            if (newPress)
            {
                if (wasHeld)
                {
                    // Another button joined an existing window gesture. Do
                    // not re-hit-test or change focus in the middle of a drag.
                    target = mouseCapture;
                }
                else
                {
                    // A genuinely new press recovers even if an overlay
                    // consumed the previous gesture's release.
                    desktopGesture = false;
                    mouseCapture = null;
                    target = null;
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
                    mouseCapture = target;
                    desktopGesture = target == null;
                }
            }
            else if (desktopGesture)
            {
                if (!isHeld)
                    desktopGesture = false;
                return;
            }
            else if (wasHeld)
            {
                // Motion and release belong to the original window, not the
                // current MRU head (which may have changed during the drag).
                target = mouseCapture;
            }

            if (desktopGesture)
                return;

            // Closing/minimizing the captured window must not leak a held
            // button or release into the window now exposed beneath it.
            if (wasHeld && (target == null || !applications.Contains(target) ||
                !target.IsRunning || target.Window == null ||
                !target.Window.Visible || target.Window.IsMinimized))
            {
                mouseCapture = null;
                desktopGesture = isHeld;
                return;
            }

            target?.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);
            if (!isHeld)
                mouseCapture = null;
        }

        public void Update()
        {
            // A callback can synchronously re-enter the manager. The outer
            // frame owns the snapshot; nested calls must not invalidate it.
            if (updating)
                return;

            if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
            {
                CloseAll();
                return;
            }

            updating = true;
            bool removed = false;
            try
            {
                // Prune invalid windows before capturing the frame's roster.
                // Apps launched during this frame begin updating next frame.
                for (int i = applications.Count - 1; i >= 0; i--)
                {
                    Application application = applications[i];
                    if (application == null || !application.IsRunning || application.Window == null)
                    {
                        applications.RemoveAt(i);
                        removed = true;
                    }
                }

                updateSnapshot.Clear();
                updateSnapshot.AddRange(applications);
                for (int i = updateSnapshot.Count - 1; i >= 0; i--)
                {
                    if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                    {
                        CloseAll();
                        return;
                    }

                    Application application = updateSnapshot[i];
                    // A previous callback may have closed this window. MRU
                    // reordering alone must not cause an app to be skipped.
                    if (application == null || !application.IsRunning ||
                        application.Window == null || !applications.Contains(application))
                        continue;

                    application.Update();
                    if (!global::ZonderqOS.SecurityContext.IsAuthenticated)
                    {
                        CloseAll();
                        return;
                    }
                }

                // A callback may mark itself stopped without calling Close().
                for (int i = applications.Count - 1; i >= 0; i--)
                {
                    Application application = applications[i];
                    if (application == null || !application.IsRunning || application.Window == null)
                    {
                        applications.RemoveAt(i);
                        removed = true;
                    }
                }
            }
            finally
            {
                updateSnapshot.Clear();
                updating = false;
                if (removed)
                    SetActiveStates(ActiveApplication);
            }
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
