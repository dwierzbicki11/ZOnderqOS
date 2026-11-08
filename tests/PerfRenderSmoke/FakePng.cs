namespace Cosmos.Kernel.System.Graphics
{
    // Host smoke only: IconManager compiles against the production Png API.
    // No PNG file is opened by these tests.
    public sealed class Png
    {
        public uint Width { get; }
        public uint Height { get; }
        public int[] RawData { get; }
        public Png(string path)
        {
            Width = 0;
            Height = 0;
            RawData = null;
        }
    }
}
