using System;
using System.IO;
using System.Text;

namespace ZonderqOS.Commands
{
    // /tmp and /run currently store strings. Never let their text boundary
    // silently replace undecodable bytes or strip a BOM before mv deletes FAT data.
    internal static class RamTextTransfer
    {
        // RAM files can contain isolated UTF-16 surrogates. Reject these before
        // opening a physical destination: encoders may replace them or throw
        // after creating/truncating the file. Valid surrogate pairs need no copy.
        public static void ValidateForDisk(string content)
        {
            if (content == null) return;
            for (int i = 0; i < content.Length; i++)
            {
                char value = content[i];
                if (value >= '\uD800' && value <= '\uDBFF')
                {
                    if (i + 1 >= content.Length || content[i + 1] < '\uDC00' || content[i + 1] > '\uDFFF')
                        throw new IOException("RAM content contains invalid UTF-16; source preserved");
                    i++;
                }
                else if (value >= '\uDC00' && value <= '\uDFFF')
                    throw new IOException("RAM content contains invalid UTF-16; source preserved");
            }
        }

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
