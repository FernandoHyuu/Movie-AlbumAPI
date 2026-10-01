using System.Buffers.Binary;
using System.IO.Compression;

namespace StreamingPanel.Infrastructure.Persistence.Seed;

/// <summary>
/// Builds tiny solid-colour PNGs for the <see cref="DatabaseSeeder"/> so every seeded
/// record carries real, decodable <c>image/png</c> bytes the cover endpoints can stream
/// back unchanged — generated in code to keep the seed payload small and dependency-free.
/// </summary>
internal static class SampleCovers
{
    /// <summary>The content type stored alongside every generated cover.</summary>
    public const string ContentType = "image/png";

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Builds a valid 1x1 solid-colour PNG for the supplied RGB components. The
    /// result is a complete PNG (signature + IHDR + IDAT + IEND) that decodes to a
    /// single opaque pixel of the given colour.
    /// </summary>
    public static byte[] SolidColor(byte red, byte green, byte blue)
    {
        using var output = new MemoryStream();
        output.Write(PngSignature);

        // IHDR: 1x1, 8-bit depth, colour type 2 (truecolour), no interlace.
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], 1);   // width
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], 1);  // height
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // colour type: truecolour (RGB)
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WriteChunk(output, "IHDR", ihdr);

        // Raw scanline: one filter byte (0 = none) followed by the RGB pixel,
        // then zlib-compressed into the IDAT chunk.
        byte[] rawScanline = [0x00, red, green, blue];
        byte[] compressed = ZlibCompress(rawScanline);
        WriteChunk(output, "IDAT", compressed);

        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        // CRC-32 over the chunk type and data.
        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var buffer = new MemoryStream();

        // zlib header for default compression, no preset dictionary.
        buffer.WriteByte(0x78);
        buffer.WriteByte(0x9C);

        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        // Adler-32 checksum of the uncompressed data, appended big-endian.
        uint adler = Adler32(data);
        Span<byte> adlerBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adlerBytes, adler);
        buffer.Write(adlerBytes);

        return buffer.ToArray();
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint modAdler = 65521;
        uint a = 1, b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % modAdler;
            b = (b + a) % modAdler;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }
}
