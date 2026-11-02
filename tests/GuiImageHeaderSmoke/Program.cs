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

    private static byte[] Png(uint width, uint height)
    {
        byte[] header = new byte[33];
        byte[] prefix = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        Array.Copy(prefix, header, prefix.Length);
        BigEndian(header, 16, width);
        BigEndian(header, 20, height);
        header[24] = 8; // PNG truecolor depth.
        header[25] = 2;
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
        Check(ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "valid indexed PNG");
        badPng[24] = 16;
        Check(!ImageHeaderGuard.HasSafeDimensions(badPng, 33, true), "invalid indexed PNG depth");

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
            Check(ImageHeaderGuard.IsSafeToDecode(temp, true),
                "valid PNG file preflight");
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, false),
                "reject PNG interpreted as BMP");
            File.WriteAllBytes(temp, Png(65535, 65535));
            Check(!ImageHeaderGuard.IsSafeToDecode(temp, true),
                "reject huge PNG file preflight");
            File.WriteAllBytes(temp, validBmp);
            Check(ImageHeaderGuard.IsSafeToDecode(temp, false),
                "valid BMP file preflight");
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
