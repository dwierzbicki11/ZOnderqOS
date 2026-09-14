using System;
using System.Collections.Generic;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public enum AppCategory
    {
        System = 1,
        Tools = 2,
        Files = 3
    }

    public enum StartMenuPlacement
    {
        None = 0,
        Pinned = 1,
        Tool = 2
    }

    public static class AppIds
    {
        public const string Terminal = "terminal";
        public const string FileManager = "file-manager";
        public const string Notepad = "notepad";
        public const string ImageViewer = "image-viewer";
        public const string Calculator = "calculator";
        public const string Calendar = "calendar";
        public const string NetworkCenter = "network-center";
        public const string DiskManager = "disk-manager";
        public const string TaskManager = "task-manager";
        public const string Settings = "settings";
        public const string Diagnostics = "diagnostics";
        public const string About = "about";
        public const string AppCenter = "app-center";
    }

    public sealed class AppDescriptor
    {
        public string Id { get; }
        public string Name { get; }
        public string MenuName { get; }
        public string DesktopName { get; }
        public string Description { get; }
        public AppCategory Category { get; }
        public IconType Icon { get; }
        public StartMenuPlacement StartPlacement { get; }
        public int StartOrder { get; }
        public int DesktopOrder { get; }
        public bool ShowInAppCenter { get; }
        public Action Launcher { get; }

        public AppDescriptor(
            string id,
            string name,
            string menuName,
            string desktopName,
            string description,
            AppCategory category,
            IconType icon,
            Action launcher,
            StartMenuPlacement startPlacement,
            int startOrder,
            int desktopOrder,
            bool showInAppCenter)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            MenuName = string.IsNullOrEmpty(menuName) ? Name : menuName;
            DesktopName = string.IsNullOrEmpty(desktopName) ? Name : desktopName;
            Description = description ?? string.Empty;
            Category = category;
            Icon = icon;
            Launcher = launcher;
            StartPlacement = startPlacement;
            StartOrder = startOrder;
            DesktopOrder = desktopOrder;
            ShowInAppCenter = showInAppCenter;
        }

        public bool Launch()
        {
            if (Launcher == null)
                return false;

            Launcher();
            return true;
        }
    }

    /// <summary>
    /// Session-local source of truth for built-in GUI applications. App metadata and
    /// launch actions are registered once by GuiManager, then reused by Start Menu,
    /// desktop shortcuts and App Center.
    /// </summary>
    public sealed class AppRegistry
    {
        private readonly List<AppDescriptor> applications = new List<AppDescriptor>(16);

        public int Count
        {
            get { return applications.Count; }
        }

        public AppDescriptor GetAt(int index)
        {
            if (index < 0 || index >= applications.Count)
                return null;
            return applications[index];
        }

        public bool Register(AppDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Id) || descriptor.Launcher == null)
                return false;

            for (int i = 0; i < applications.Count; i++)
            {
                if (string.Equals(applications[i].Id, descriptor.Id, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            applications.Add(descriptor);
            return true;
        }

        public AppDescriptor Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < applications.Count; i++)
            {
                AppDescriptor descriptor = applications[i];
                if (string.Equals(descriptor.Id, id, StringComparison.OrdinalIgnoreCase))
                    return descriptor;
            }

            return null;
        }

        public bool Launch(string id)
        {
            AppDescriptor descriptor = Find(id);
            return descriptor != null && descriptor.Launch();
        }

        public int AppCenterCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < applications.Count; i++)
                {
                    if (applications[i].ShowInAppCenter)
                        count++;
                }
                return count;
            }
        }
    }
}
