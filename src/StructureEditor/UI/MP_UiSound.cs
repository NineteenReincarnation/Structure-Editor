using System.Media;
using System.Text;

namespace StructureEditor;

// Original low-volume cues, synthesized in code. No external media files.
// Navigation is rate-limited; missing audio hardware must not fail commands.
internal static class MP_UiSound
{
    internal enum Cue { Navigate, Confirm, Toggle, Close }
    private static readonly SoundPlayer[] Players = Build();
    private static readonly long[] LastPlayed = new long[4];
    private static readonly object Sync = new();

    internal static bool Enabled => Program.Paths?.MP_UiSoundsEnabled ?? true;

    internal static void Play(Cue cue)
    {
        if (!Enabled) return;
        int index = (int)cue;
        lock (Sync)
        {
            long now = Environment.TickCount64;
            if (now - LastPlayed[index] < (cue == Cue.Navigate ? 65 : 42)) return;
            LastPlayed[index] = now;
        }
        try { Players[index]?.Play(); }
        catch (Exception e) when (e is InvalidOperationException or
            System.ComponentModel.Win32Exception or IOException)
        { /* Audio is never required to use the editor. */ }
    }

    private static SoundPlayer[] Build()
    {
        SoundPlayer[] players = new SoundPlayer[4];
        (double start, double end, int length)[] tones = {
            (570, 635, 38), (725, 855, 60),
            (615, 520, 50), (505, 400, 58)
        };
        for (int i = 0; i < tones.Length; i++)
        {
            try
            {
                var wav = Wave(tones[i].start, tones[i].end, tones[i].length);
                var player = new SoundPlayer(new MemoryStream(wav, writable: false));
                player.Load();
                players[i] = player;
            }
            catch (Exception e) when (e is InvalidOperationException or
                System.ComponentModel.Win32Exception or IOException)
            { /* Silent fallback on headless systems. */ }
        }
        return players;
    }

    private static byte[] Wave(double start, double end, int durationMs)
    {
        const int rate = 22050;
        int count = rate * durationMs / 1000;
        using var stream = new MemoryStream(44 + count * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(count * 2);
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)count;
            phase += 2 * Math.PI * (start + (end - start) * t) / rate;
            double envelope = Math.Min(1, t * 40) * Math.Pow(1 - t, 2);
            writer.Write((short)Math.Round(Math.Sin(phase) * 1250 * envelope));
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static bool MP_ValidCueData()
    {
        byte[] data = Wave(570, 635, 38);
        return data.Length > 44
            && Encoding.ASCII.GetString(data, 0, 4) == "RIFF"
            && Encoding.ASCII.GetString(data, 8, 4) == "WAVE";
    }
}
