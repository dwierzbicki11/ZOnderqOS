using System;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Small, allocation-free navigation rules shared by the production Task
    /// Manager and the host smoke tests. Keyboard shortcuts must never select
    /// a resource or logical CPU page outside the rendered range.
    /// </summary>
    internal static class TaskManagerPerformanceNavigation
    {
        internal static int StepResource(int current, int direction)
        {
            if (current < 0 || current > 2)
                current = 0;
            if (direction == 0)
                return current;
            return (current + (direction > 0 ? 1 : 2)) % 3;
        }

        internal static int ClampLogicalPage(int page, int pageCount)
        {
            int last = Math.Max(0, pageCount - 1);
            return Math.Max(0, Math.Min(page, last));
        }
    }
}
