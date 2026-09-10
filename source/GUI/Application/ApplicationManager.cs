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

        public void Launch(Application application)
        {
            if (application == null || !application.IsRunning)
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
        }

        public void ToggleMinimize(Application application)
        {
            if (application == null || !application.IsRunning || application.Window == null)
                return;

            if (application.Window.IsMinimized)
            {
                application.Window.RestoreFromMinimized();
                Activate(application);
            }
            else if (ActiveApplication == application)
            {
                application.Window.Minimize();
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
        }

        public void HandleKeyboard(KeyEvent key)
        {
            ActiveApplication?.HandleKeyboard(key);
        }

        public void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            HandleMouse(mouseX, mouseY, isClicked, wasClicked, false, false);
        }

        public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
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
            for (int i = applications.Count - 1; i >= 0; i--)
            {
                Application application = applications[i];
                if (application == null || !application.IsRunning || application.Window == null)
                    applications.RemoveAt(i);
                else
                    application.Update();
            }
        }

        public void Render(Canvas canvas)
        {
            for (int i = 0; i < applications.Count; i++)
            {
                Application application = applications[i];
                if (application != null && application.IsRunning && application.Window != null &&
                    application.Window.Visible && !application.Window.IsMinimized)
                    application.Render(canvas);
            }
        }
    }
}
