using System.Collections.Generic;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    public class ApplicationManager
    {
        private readonly List<Application> applications = new List<Application>();

        public Application ActiveApplication
        {
            get
            {
                for (int i = applications.Count - 1; i >= 0; i--)
                {
                    Application application = applications[i];
                    if (application != null && application.IsRunning && application.Window != null && application.Window.Visible)
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
            ActiveApplication?.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
        }

        public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            ActiveApplication?.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);
        }

        public void Update()
        {
            for (int i = applications.Count - 1; i >= 0; i--)
            {
                Application application = applications[i];
                if (application == null || !application.IsRunning || application.Window == null || !application.Window.Visible)
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
                if (application != null && application.IsRunning && application.Window != null && application.Window.Visible)
                    application.Render(canvas);
            }
        }
    }
}
