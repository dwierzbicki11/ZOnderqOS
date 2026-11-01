using Cosmos.Kernel.System.Storage;

namespace ZonderqOS
{
    public static partial class Disk
    {
        private static Partition _rootPartition;

        // Mount identity is fixed after successful mounting. Do not re-run the
        // discovery/label policy: hotplug may reorder or replace its candidates.
        public static bool IsRootPartition(Partition partition)
        {
            return partition != null && object.ReferenceEquals(_rootPartition, partition);
        }
    }
}
