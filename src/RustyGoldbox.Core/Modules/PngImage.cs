using System.Buffers.Binary;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Reads a PNG's header to check it is the kind of image the Engine renderer
/// admits: an 8-bit RGBA PNG. The Engine still decodes the image when the
/// Game draws it; this lets validation say what is wrong without a renderer.
/// </summary>
public static class PngImage
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private const byte RgbaColourType = 6;

    /// <summary>Returns null and the size for an admissible PNG, or what is wrong with it.</summary>
    public static string? Read(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Signature, then the IHDR chunk: length 13, "IHDR", width, height, bit depth, colour type.
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(Signature))
        {
            return "is not a PNG file.";
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes[12..16].SequenceEqual("IHDR"u8))
        {
            return "is a damaged PNG: it doesn't start with an image header.";
        }

        uint w = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        uint h = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
        {
            return $"has an impossible size ({w} x {h}).";
        }

        byte depth = bytes[24];
        byte colour = bytes[25];
        if (colour != RgbaColourType || depth != 8)
        {
            return $"is a {depth}-bit {ColourName(colour)} PNG, which the renderer doesn't admit.";
        }

        width = (int)w;
        height = (int)h;
        return null;
    }

    private static string ColourName(byte colour) => colour switch
    {
        0 => "greyscale",
        2 => "RGB",
        3 => "palette",
        4 => "greyscale-with-alpha",
        6 => "RGBA",
        _ => $"colour type {colour}",
    };
}
