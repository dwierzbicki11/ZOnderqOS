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

        public static string ReadLossless(string source, int maxCharacters)
        {
            if (maxCharacters < 0 || maxCharacters > (int.MaxValue - 1) / 3)
                throw new ArgumentOutOfRangeException(nameof(maxCharacters));
            // UTF-8 needs at most three bytes per UTF-16 code unit. One extra
            // byte detects oversize input without trusting a stale file length.
            int maxBytes = maxCharacters * 3;
            byte[] bytes = new byte[maxBytes + 1];
            int count = 0;
            using (var stream = File.OpenRead(source))
            {
                while (count < bytes.Length)
                {
                    int read = stream.Read(bytes, count, Math.Min(4096, bytes.Length - count));
                    if (read == 0) break;
                    count += read;
                }
            }
            if (count > maxBytes)
                throw new IOException("File exceeds RAM transfer byte limit; source preserved");
            string content = Encoding.UTF8.GetString(bytes, 0, count);
            if (content.Length > maxCharacters)
                throw new IOException("File exceeds RAM character limit; source preserved");
            byte[] encoded = Encoding.UTF8.GetBytes(content);
            if (count != encoded.Length)
                throw new IOException("RAM filesystem requires lossless UTF-8 content; source preserved");

            for (int i = 0; i < count; i++)
            {
                if (bytes[i] != encoded[i])
                    throw new IOException("RAM filesystem requires lossless UTF-8 content; source preserved");
            }

            return content;
        }
    }
}
