using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StructureEditor;

// Dry gamepad-button clicks: a gentle midrange plastic snap with a short
// rubber membrane decay. No musical tones, shrill square waves, or dull thuds.
// Samples are synthesized locally and mixed asynchronously via NAudio.
internal static class MP_UiSound
{
    internal enum Cue { Navigate, Confirm, Toggle, Close, Open, Cancel, Focus, Error, Place, Select }

    private readonly record struct MP_Click(int OffsetMs, int DurationMs,
        double Hz, double Gain, double Contact = .46);

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
            int cooldown = cue is Cue.Navigate or Cue.Focus ? 62 : 40;
            if (now - LastPlayed[index] < cooldown) return;
            LastPlayed[index] = now;
            try
            {
                if (output == null)
                {
                    mixer = new MixingSampleProvider(
                        WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1))
                    { ReadFully = true };
                    output = new WaveOutEvent { DesiredLatency = 50, NumberOfBuffers = 3 };
                    output.Init(new SampleToWaveProvider(mixer));
                    output.Play();
                }
                mixer.AddMixerInput(new MP_Voice(Samples[index]));
            }
            catch (Exception e) when (e is InvalidOperationException or
                NAudio.MmException or System.ComponentModel.Win32Exception or
                IOException or DllNotFoundException or
                TypeInitializationException or PlatformNotSupportedException)
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

    // Each cue is a brief contact and a smaller release. Give the front
    // edge a 1-3 kHz click; remove the low-frequency boom of the previous
    // rubber-dome model without returning to an 8-bit high-pitched beep.
    private static MP_Click[] Pattern(Cue cue) => cue switch
    {
        Cue.Navigate => [new(0, 19, 208, .091, .68),
                         new(16, 12, 167, .025, .48)],
        Cue.Confirm => [new(0, 26, 191, .116, .73),
                        new(19, 21, 183, .058, .58)],
        Cue.Toggle => [new(0, 23, 194, .094, .70),
                       new(18, 16, 159, .036, .53)],
        Cue.Close => [new(0, 25, 174, .100, .72),
                      new(19, 16, 148, .034, .51)],
        Cue.Open => [new(0, 25, 207, .100, .69),
                     new(17, 19, 180, .040, .55)],
        Cue.Cancel => [new(0, 27, 166, .087, .65),
                       new(17, 17, 149, .030, .52)],
        Cue.Focus => [new(0, 15, 193, .048, .58)],
        Cue.Error => [new(0, 36, 150, .092, .63),
                      new(24, 21, 135, .041, .48)],
        Cue.Place => [new(0, 27, 182, .111, .74),
                      new(20, 18, 166, .042, .56)],
        Cue.Select => [new(0, 23, 200, .082, .67)],
        _ => []
    };

    private static float[] Synthesize(Cue cue)
    {
        MP_Click[] clicks = Pattern(cue);
        int lengthMs = clicks.Length == 0 ? 0 :
            clicks.Max(click => click.OffsetMs + click.DurationMs) + 4;
        float[] samples = new float[Math.Max(Rate / 100, lengthMs * Rate / 1000)];

        for (int layer = 0; layer < clicks.Length; layer++)
        {
            MP_Click click = clicks[layer];
            int offset = click.OffsetMs * Rate / 1000;
            int length = Math.Min(click.DurationMs * Rate / 1000, samples.Length - offset);
            if (length <= 0) continue;

            var noise = new Random(14387 + (int)cue * 977 + layer * 173);
            double fast = 0, slow = 0, phase = 0;
            double highCut = 2250 + click.Contact * 1100;
            double fastAlpha = 1 - Math.Exp(-2 * Math.PI * highCut / Rate);
            double slowAlpha = 1 - Math.Exp(-2 * Math.PI * 430 / Rate);

            for (int i = 0; i < length; i++)
            {
                double progress = i / (double)length;
                phase += 2 * Math.PI * click.Hz * (1 - progress * .08) / Rate;
                double raw = noise.NextDouble() * 2 - 1;
                fast += fastAlpha * (raw - fast);
                slow += slowAlpha * (raw - slow);

                // Band-limited transient supplies the distinct click of
                // plastic-on-rubber. No sharp full-band noise or sustained tone.
                double snap = (fast - slow) *
                    Math.Exp(-progress * 8.4) * (1.15 + click.Contact * .55);
                double dome = Math.Sin(phase) * .27 *
                    Math.Exp(-progress * 5.8);
                double attack = Math.Min(1, i / (Rate * .0003));
                double release = Math.Min(1, (length - i) / (Rate * .0035));
                samples[offset + i] += (float)(click.Gain *
                    Math.Min(attack, release) *
                    (snap * click.Contact + dome * (1 - click.Contact * .4)));
            }
        }
        return samples;
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
