using System.Collections.Generic;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;

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

            applications.Add(application);
            Activate(application);
        }

        public void Activate(Application application)
        {
            if (application == null || !application.IsRunning || application.Window == null)
                return;

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
                application.Window.RestoreFromMinimized();
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

            if (leftClicked && !leftWasClicked)
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
