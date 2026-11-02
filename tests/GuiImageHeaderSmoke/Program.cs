using System;
using System.IO;
using ZonderqOS.GUI;

internal static class Program
{
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Image header regression: " + message);
    }

    private static void BigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static void LittleEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    // Independent fixture generator for the standard PNG CRC-32 polynomial.
    private static void SealPngIhdr(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = 12; i < 29; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1u) == 0 ? 0u : 0xEDB88320u);
        }
        BigEndian(bytes, 29, ~crc);
    }

    private static byte[] Png(uint width, uint height)
    {
        byte[] header = new byte[33];
        byte[] prefix = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        Array.Copy(prefix, header, prefix.Length);
        BigEndian(header, 16, width);
        BigEndian(header, 20, height);
        header[24] = 8; // PNG truecolor depth.
        header[25] = 2;
        SealPngIhdr(header);
        return header;
    }

    private static byte[] Bmp(int width, int height)
    {
        byte[] header = new byte[54];
        header[0] = 66;
        header[1] = 77;
        LittleEndian(header, 10, 54); // Pixel data after DIB.
        LittleEndian(header, 14, 40);
        header[26] = 1; // One plane.
        header[28] = 24; // RGB24.
        LittleEndian(header, 18, unchecked((uint)width));
        LittleEndian(header, 22, unchecked((uint)height));
        return header;
    }

    private static byte[] BmpFile(int width, int height)
    {
        // A real uncompressed RGB24 payload, including 4-byte row padding.
        long stride = (((long)width * 24 + 31) / 32) * 4;
        byte[] data = new byte[checked((int)(54 + stride * height))];
        Array.Copy(Bmp(width, height), data, 54);
        LittleEndian(data, 2, (uint)data.Length);
        return data;
    }

    public static int Main()
    {
        Check(ImageHeaderGuard.HasSafeDimensions(Png(1920, 1080), 33, true),
            "Full HD PNG");
        Check(ImageHeaderGuard.HasSafeDimensions(Png(4096, 4096), 33, true),
            "16 Mi pixel PNG");
        Check(!ImageHeaderGuard.HasSafeDimensions(Png(4097, 4096), 33, true),
            "PNG pixel cap");
        Check(!ImageHeaderGuard.HasSafeDimensions(Png(16385, 1), 33, true),
            "PNG dimension cap");
        Check(!ImageHeaderGuard.HasSafeDimensions(Png(0, 1080), 33, true),
            "zero PNG width");
        Check(!ImageHeaderGuard.HasSafeDimensions(Png(uint.MaxValue, 1), 33, true),
            "unsigned PNG width overflow");
        Check(ImageHeaderGuard.HasSafeDimensions(Bmp(1366, -768), 54, false),
            "top-down BMP");
        Check(!ImageHeaderGuard.HasSafeDimensions(Bmp(-1, 768), 54, false),
            "negative BMP width");
        Check(!ImageHeaderGuard.HasSafeDimensions(Bmp(1, int.MinValue), 54, false),
            "BMP signed height overflow");
        Check(!ImageHeaderGuard.HasSafeDimensions(Bmp(16384, 16384), 54, false),
            "BMP pixel cap");

        byte[] validPng = Png(800, 600);
        byte[] validBmp = Bmp(800, 600);
        // Known CRC32 of IHDR for RGB8 800x600, cross-checked with zlib.
        Check(validPng[29] == 0x15 && validPng[30] == 0x14 &&
              validPng[31] == 0x15 && validPng[32] == 0x27, "known PNG IHDR CRC");
        for (int length = 0; length < 33; length++)
            Check(!ImageHeaderGuard.HasSafeDimensions(validPng, length, true),
                "truncated PNG header " + length);
        for (int length = 0; length < 54; length++)
            Check(!ImageHeaderGuard.HasSafeDimensions(validBmp, length, false),
                "truncated BMP header " + length);

        Check(!ImageHeaderGuard.HasSafeDimensions(validPng, -1, true),
            "negative header length");
        Check(!ImageHeaderGuard.HasSafeDimensions(validPng, 34, true),
            "header length beyond buffer");
        Check(!ImageHeaderGuard.HasSafeDimensions(null!, 0, true),
            "null header");

        for (int i = 0; i < 16; i++)
        {
            byte[] corrupt = (byte[])validPng.Clone();
            corrupt[i] ^= 1;
            Check(!ImageHeaderGuard.HasSafeDimensions(corrupt, 33, true),
                "PNG signature/IHDR byte " + i);
        }

        byte[] invalidDib = (byte[])validBmp.Clone();
        LittleEndian(invalidDib, 14, 12);
        Check(!ImageHeaderGuard.HasSafeDimensions(invalidDib, 54, false),
            "unsupported short BMP DIB header");
        invalidDib = (byte[])validBmp.Clone();
        invalidDib[0] = 0;
        Check(!ImageHeaderGuard.HasSafeDimensions(invalidDib, 54, false),
            "invalid BMP signature");

        // Corrupt but dimensionally small headers must fail before decoder allocation.
        byte[] badPng = (byte[])validPng.Clone();
        badPng[24] = 1;
        Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "invalid PNG RGB depth");
        badPng = (byte[])validPng.Clone();
        badPng[25] = 5;
        Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "invalid PNG color type");
        for (int offset = 26; offset <= 28; offset++)
        {
            badPng = (byte[])validPng.Clone();
            badPng[offset] = 2;
            Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true),
                "invalid PNG method/interlace " + offset);
        }
        badPng = (byte[])validPng.Clone();
        badPng[25] = 3;
        badPng[24] = 4;
        SealPngIhdr(badPng);
        Check(ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "valid indexed PNG");
        badPng[24] = 16;
        SealPngIhdr(badPng);
        Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "invalid indexed PNG depth");
        badPng = (byte[])validPng.Clone();
        badPng[30] ^= 1;
        Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "corrupted PNG CRC");
        badPng = (byte[])validPng.Clone();
        badPng[16] ^= 1; // Valid dimensions, invalid IHDR CRC.
        Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "corrupted PNG width CRC");

        byte[] badBmp = (byte[])validBmp.Clone();
        LittleEndian(badBmp, 10, 53);
        Check(!ImageHeaderGuard.HasSafeDimensions(badBmp, 54, false), "overlapping BMP pixels");
        badBmp = (byte[])validBmp.Clone();
        LittleEndian(badBmp, 14, uint.MaxValue);
        Check(!ImageHeaderGuard.HasSafeDimensions(badBmp, 54, false), "overflowing BMP DIB");
        badBmp = (byte[])validBmp.Clone();
        badBmp[26] = 2;
        Check(!ImageHeaderGuard.HasSafeDimensions(badBmp, 54, false), "multiple BMP planes");
        badBmp = (byte[])validBmp.Clone();
        badBmp[28] = 0;
        Check(!ImageHeaderGuard.HasSafeDimensions(badBmp, 54, false), "zero BMP bit depth");
        badBmp = (byte[])validBmp.Clone();
        LittleEndian(badBmp, 14, 124);
        LittleEndian(badBmp, 10, 138);
        Check(ImageHeaderGuard.HasSafeDimensions(badBmp, 54, false), "valid V5 BMP offset");

        // A valid header alone must not allow the decoder to allocate pixels
        // from a truncated file. This exercises the production file-size gate.
        Check(!ImageHeaderGuard.HasSafeFileLength(validPng, 33, true, 33),
            "header-only PNG file rejected");
        Check(!ImageHeaderGuard.HasSafeFileLength(validPng, 33, true, 56),
            "PNG shorter than IHDR/IDAT/IEND minimum rejected");
        Check(ImageHeaderGuard.HasSafeFileLength(validPng, 33, true, 57),
            "PNG minimum file length accepted for decoder validation");
        Check(!ImageHeaderGuard.HasSafeFileLength(validPng, 33, true, -1),
            "negative file length rejected");
        Check(!ImageHeaderGuard.HasSafeFileLength(validBmp, 54, false, 54),
            "header-only BMP file rejected");
        Check(!ImageHeaderGuard.HasSafeFileLength(validBmp, 54, false, 1440053),
            "BMP one byte short of 800x600 RGB24 rejected");
        Check(ImageHeaderGuard.HasSafeFileLength(validBmp, 54, false, 1440054),
            "complete BMP RGB24 payload accepted");
        byte[] tinyBmp = Bmp(1, 1);
        Check(!ImageHeaderGuard.HasSafeFileLength(tinyBmp, 54, false, 57),
            "BMP row padding counted");
        Check(ImageHeaderGuard.HasSafeFileLength(tinyBmp, 54, false, 58),
            "BMP padded row accepted");
        LittleEndian(tinyBmp, 2, 59);
        Check(!ImageHeaderGuard.HasSafeFileLength(tinyBmp, 54, false, 58),
            "BMP declared file size beyond actual file rejected");
        LittleEndian(tinyBmp, 2, 57);
        Check(!ImageHeaderGuard.HasSafeFileLength(tinyBmp, 54, false, 58),
            "BMP declared file size smaller than pixel payload rejected");
        LittleEndian(tinyBmp, 2, 58);
        Check(ImageHeaderGuard.HasSafeFileLength(tinyBmp, 54, false, 58),
            "BMP matching declared and actual size accepted");
        byte[] highOffset = Bmp(1, 1);
        LittleEndian(highOffset, 14, 124);
        LittleEndian(highOffset, 10, 138);
        Check(!ImageHeaderGuard.HasSafeFileLength(highOffset, 54, false, 141),
            "BMP V5 payload one byte short rejected");
        Check(ImageHeaderGuard.HasSafeFileLength(highOffset, 54, false, 142),
            "BMP V5 payload offset accounted for");
        byte[] unsupportedBpp = Bmp(1, 1);
        unsupportedBpp[28] = 3;
        Check(!ImageHeaderGuard.HasSafeFileLength(unsupportedBpp, 54, false, 100),
            "unsupported uncompressed BMP depth rejected");
        Check(!ImageHeaderGuard.HasSafeFileLength(Bmp(16384, 1024), 54, false, 100),
            "large BMP cannot bypass payload length check");

        // Exhaustive boundary sweep across the normal desktop resolutions.
        foreach (int width in new[] { 1, 800, 1366, 1920, 4096, 16384, 16385 })
        foreach (int height in new[] { 1, 600, 768, 1080, 4096, 16384, 16385 })
        {
            bool expected = width <= 16384 && height <= 16384 &&
                (long)width * height <= ImageHeaderGuard.MaxPixels;
            Check(ImageHeaderGuard.HasSafeDimensions(Png((uint)width, (uint)height), 33, true) == expected,
                "PNG dimensions " + width + "x" + height);
            Check(ImageHeaderGuard.HasSafeDimensions(Bmp(width, -height), 54, false) == expected,
                "BMP dimensions " + width + "x" + height);
        }

        // This checks the actual file-read preflight on the host, not a Cosmos
        // filesystem or a successful PNG/BMP decode.
        string temp = Path.Combine(Path.GetTempPath(),
            "zonderq-image-header-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(temp, validPng);
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, true),
                "reject PNG containing only IHDR");
            byte[] pngWithChunkSpace = new byte[57];
            Array.Copy(validPng, pngWithChunkSpace, validPng.Length);
            File.WriteAllBytes(temp, pngWithChunkSpace);
            Check(ImageHeaderGuard.IsSafeToDecode(temp, true),
                "PNG header and minimum payload length preflight");
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, false),
                "reject PNG interpreted as BMP");
            byte[] corruptCrc = (byte[])validPng.Clone();
            corruptCrc[32] ^= 1;
            File.WriteAllBytes(temp, corruptCrc);
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, true),
                "reject corrupted PNG CRC in file preflight");
            File.WriteAllBytes(temp, Png(65535, 65535));
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, true),
                "reject huge PNG file preflight");
            File.WriteAllBytes(temp, validBmp);
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, false),
                "reject BMP containing only DIB header");
            File.WriteAllBytes(temp, BmpFile(800, 600));
            Check(ImageHeaderGuard.IsSafeToDecode(temp, false),
                "complete BMP RGB24 file preflight");
            byte[] truncatedBmp = BmpFile(1, 1);
            Array.Resize(ref truncatedBmp, 57);
            File.WriteAllBytes(temp, truncatedBmp);
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, false),
                "reject BMP missing final row-padding byte");
            File.WriteAllBytes(temp, new byte[] { 66, 77, 0 });
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, false),
                "truncated BMP file preflight");
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        Check(!ImageHeaderGuard.IsSafeToDecode(temp, true),
            "missing file preflight fails closed");

        Console.WriteLine("PASS: " + assertions +
            " production ImageHeaderGuard C# assertions");
        return 0;
    }
}
