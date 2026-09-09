using System.Collections.Generic;
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
                if (applications.Count == 0) return null;
                return applications[applications.Count - 1];
            }
        }

        public void Launch(Application application)
        {
            if (application == null) return;
            applications.Add(application);
        }

        public void Close(Application application)
        {
            if (application == null) return;
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
            ActiveApplication?.Window?.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
        }

        public void Update()
        {
            for (int i = applications.Count - 1; i >= 0; i--)
            {
                if (!applications[i].IsRunning)
                    applications.RemoveAt(i);
                else
                    applications[i].Update();
            }
        }

        public void Render(Cosmos.Kernel.System.Graphics.Canvas canvas)
        {
            foreach (var application in applications)
            {
                if (application.IsRunning)
                    application.Render(canvas);
            }
        }
    }
}