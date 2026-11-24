using NAudio.Wave;

namespace MusicLayer.Infrastructure.Audio;

public static class TrackWaveformReader
{
    private const int BandCount = 144;

    public static Task<double[]> ReadAsync(string filePath, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            using var reader = new MediaFoundationReader(filePath);
            var source = reader.ToSampleProvider();
            var channels = source.WaveFormat.Channels;
            var totalSamples = reader.Length / reader.BlockAlign * channels;
            var samplesPerBand = Math.Max(1, (totalSamples + BandCount - 1) / BandCount);
            var buffer = new float[4096 * channels];
            var levels = new double[BandCount];
            var band = 0;
            long count = 0;
            double energy = 0;
            while (band < levels.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;

                for (var i = 0; i < read && band < levels.Length; i++)
                {
                    var sample = buffer[i];
                    energy += sample * (double)sample;
                    if (++count < samplesPerBand) continue;
                    levels[band++] = Math.Sqrt(energy / count);
                    energy = 0;
                    count = 0;
                }
            }
            if (band < levels.Length && count > 0) levels[band] = Math.Sqrt(energy / count);
            var peak = levels.Max();
            if (peak > 0 && double.IsFinite(peak))
            {
                for (var i = 0; i < levels.Length; i++)
                    levels[i] /= peak;
            }
            return levels;
        }, cancellationToken);
}
