using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StructureEditor;

// Original 8-bit inspired UI sound palette. All voices are synthesized locally,
// mixed at low gain and reused without storing or shipping third-party samples.
// NAudio provides low-latency nonblocking playback and safe overlapping cues.
internal static class MP_UiSound
{
    internal enum Cue { Navigate, Confirm, Toggle, Close, Open, Cancel, Focus, Error, Place, Select }

    private readonly record struct MP_Note(double Start, double End, int Milliseconds,
        double Gain = 0.055, bool HardPulse = false);

    private const int Rate = 44100;
    private static readonly object Sync = new();
    private static readonly long[] LastPlayed = new long[Enum.GetValues<Cue>().Length];
    private static readonly float[][] Samples = Enum.GetValues<Cue>().Select(Synthesize).ToArray();
    private static WaveOutEvent output;
    private static MixingSampleProvider mixer;
    private static bool audioUnavailable;

    internal static bool Enabled => Program.Paths?.MP_UiSoundsEnabled ?? true;

    internal static void Play(Cue cue)
    {
        if (!Enabled || audioUnavailable) return;
        int index = (int)cue;
        lock (Sync)
        {
            long now = Environment.TickCount64;
            int cooldown = cue is Cue.Navigate or Cue.Focus ? 48 : 28;
            if (now - LastPlayed[index] < cooldown) return;
            LastPlayed[index] = now;
            try
            {
                if (output == null)
                {
                    mixer = new MixingSampleProvider(
                        WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1))
                    { ReadFully = true };
                    output = new WaveOutEvent { DesiredLatency = 72, NumberOfBuffers = 3 };
                    output.Init(new SampleToWaveProvider(mixer));
                    output.Play();
                }
                mixer.AddMixerInput(new MP_Voice(Samples[index]));
            }
            catch (Exception e) when (e is InvalidOperationException or
                System.ComponentModel.Win32Exception or IOException or
                DllNotFoundException or TypeInitializationException)
            {
                // Headless Windows, a disabled device or no available output
                // must not break any editor command.
                audioUnavailable = true;
                output?.Dispose();
                output = null;
                mixer = null;
            }
        }
    }

    internal static void Shutdown()
    {
        lock (Sync)
        {
            output?.Stop();
            output?.Dispose();
            output = null;
            mixer = null;
        }
    }

    private static MP_Note[] Pattern(Cue cue) => cue switch
    {
        Cue.Navigate => [new(585, 755, 36, .043, true)],
        Cue.Confirm => [new(570, 725, 42, .061), new(845, 1040, 53, .054)],
        Cue.Toggle => [new(445, 520, 34, .049, true), new(600, 710, 39, .044)],
        Cue.Close => [new(690, 480, 41, .05), new(410, 290, 49, .040, true)],
        Cue.Open => [new(360, 490, 30, .045), new(500, 730, 55, .055)],
        Cue.Cancel => [new(420, 310, 58, .047, true), new(260, 220, 36, .033)],
        Cue.Focus => [new(760, 780, 24, .025, true)],
        Cue.Error => [new(205, 190, 85, .056, true), new(170, 150, 85, .043, true)],
        Cue.Place => [new(350, 460, 34, .049, true), new(515, 620, 32, .040)],
        Cue.Select => [new(460, 630, 50, .050)],
        _ => []
    };

    private static float[] Synthesize(Cue cue)
    {
        var notes = Pattern(cue);
        const int gapMs = 3;
        int samples = notes.Sum(n => n.Milliseconds + gapMs) * Rate / 1000;
        float[] data = new float[samples];
        int offset = 0;
        double phase = 0;
        foreach (var note in notes)
        {
            int count = note.Milliseconds * Rate / 1000;
            for (int i = 0; i < count; i++)
            {
                double progress = i / (double)Math.Max(1, count - 1);
                double hz = note.Start * Math.Pow(note.End / note.Start, progress);
                phase += 2 * Math.PI * hz / Rate;
                // Crunchy pulse plus rounded triangle: recognizable 1980s
                // game-console transients without the harshness of raw squares.
                double triangle = 2 / Math.PI * Math.Asin(Math.Sin(phase));
                double pulse = Math.Sin(phase) >= .28 ? 1 : -1;
                double color = note.HardPulse
                    ? triangle * .35 + pulse * .65
                    : triangle * .77 + pulse * .23;
                double attack = Math.Min(1, i / (Rate * .0035));
                double release = Math.Min(1, (count - 1 - i) / (Rate * .012));
                double envelope = Math.Max(0, Math.Min(attack, release)) *
                    (1 - .27 * progress);
                data[offset + i] = (float)(color * envelope * note.Gain);
            }
            offset += count + gapMs * Rate / 1000;
        }
        return data;
    }

    private sealed class MP_Voice(float[] samples) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int read = Math.Min(count, samples.Length - position);
            if (read <= 0) return 0;
            Array.Copy(samples, position, buffer, offset, read);
            position += read;
            return read;
        }
    }

    internal static bool MP_ValidCueData()
        => Samples.Length == Enum.GetValues<Cue>().Length
        && Samples.All(samples => samples.Length > 100
            && samples.Any(value => float.IsFinite(value) && Math.Abs(value) > .001f));
}
