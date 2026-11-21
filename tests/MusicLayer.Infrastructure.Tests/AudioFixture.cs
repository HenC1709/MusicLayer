namespace MusicLayer.Infrastructure.Tests;

internal static class AudioFixture
{
    internal static void Create(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".flac")
        {
            // One frame of silence at 44.1 kHz, mono.
            File.WriteAllBytes(path, Convert.FromHexString(
                "664c614380000022100010000000000000000ac440f00000100000000000000000000000000000000000fff8c908009500000021bd"));
            return;
        }

        using var writer = new BinaryWriter(File.Create(path));
        if (extension == ".mp3")
        {
            // MPEG-1 Layer III frames at 128 kbps, 44.1 kHz, without ID3 tags.
            byte[] frame = new byte[417];
            new byte[] { 0xFF, 0xFB, 0x90, 0xC4 }.CopyTo(frame, 0);
            for (var i = 0; i < 3; i++) writer.Write(frame);
            return;
        }

        const int dataSize = 16;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(88200);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
