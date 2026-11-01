using System;
using System.IO;
using System.Text;

namespace ZonderqOS.Commands
{
    // /tmp and /run currently store strings. Never let their text boundary
    // silently replace undecodable bytes or strip a BOM before mv deletes FAT data.
    internal static class RamTextTransfer
    {
        public static string ReadLossless(string source)
        {
            byte[] bytes = File.ReadAllBytes(source);
            string content = Encoding.UTF8.GetString(bytes);
            byte[] encoded = Encoding.UTF8.GetBytes(content);
            if (bytes.Length != encoded.Length)
                throw new IOException("RAM filesystem requires lossless UTF-8 content; source preserved");

            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != encoded[i])
                    throw new IOException("RAM filesystem requires lossless UTF-8 content; source preserved");
            }

            return content;
        }
    }
}
