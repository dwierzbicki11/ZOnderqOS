namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Visibility gate for destructive Task Manager actions. The same policy
    /// is used by the painted END TASK button, mouse hit-testing and Delete.
    /// Subtraction avoids overflowing when a long-running scroll index grows.
    /// </summary>
    internal static class TaskManagerSelectionPolicy
    {
        internal static bool IsVisible(int selectedIndex, int scrollIndex,
            int visibleRows, int itemCount)
        {
            return selectedIndex >= 0 && selectedIndex < itemCount &&
                scrollIndex >= 0 && visibleRows > 0 &&
                selectedIndex >= scrollIndex &&
                selectedIndex - scrollIndex < visibleRows;
        }
    }
}
