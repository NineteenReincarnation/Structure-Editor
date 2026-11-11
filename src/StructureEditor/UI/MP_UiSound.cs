using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StructureEditor;

// Soft, physical controller-button feedback: a filtered plastic contact
// followed by a very quiet membrane/rebound thump, never a pitched UI beep.
// NAudio mixes short local samples asynchronously without bundling sound files.
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

    // A two-stage low-pass filters the contact noise to the frequency
    // range of a real rubber-dome controller, rather than bright square waves.
    // Each cue has its own press/release weight but stays under 75 ms.
    private static MP_Click[] Pattern(Cue cue) => cue switch
    {
        Cue.Navigate => [new(0, 23, 158, .068, .39),
                         new(18, 14, 118, .020, .24)],
        Cue.Confirm => [new(0, 34, 132, .088, .48),
                        new(23, 27, 108, .048, .32)],
        Cue.Toggle => [new(0, 28, 149, .065, .45),
                       new(21, 18, 121, .028, .24)],
        Cue.Close => [new(0, 33, 116, .078, .38),
                      new(24, 22, 97, .034, .22)],
        Cue.Open => [new(0, 32, 144, .068, .41),
                     new(22, 22, 137, .038, .29)],
        Cue.Cancel => [new(0, 31, 108, .067, .36),
                       new(17, 24, 91, .028, .21)],
        Cue.Focus => [new(0, 17, 120, .030, .26)],
        Cue.Error => [new(0, 53, 88, .075, .32),
                      new(27, 36, 82, .042, .25)],
        Cue.Place => [new(0, 34, 124, .084, .51),
                      new(22, 18, 109, .031, .27)],
        Cue.Select => [new(0, 29, 141, .061, .37)],
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
            double filter = 0, filter2 = 0, phase = 0;
            // Damping > 1 kHz prevents the sharp treble of the old chiptune.
            double cutoff = 700 + click.Contact * 560;
            double alpha = 1 - Math.Exp(-2 * Math.PI * cutoff / Rate);

            for (int i = 0; i < length; i++)
            {
                double progress = i / (double)length;
                phase += 2 * Math.PI * click.Hz * (1 - progress * .12) / Rate;
                double unfiltered = noise.NextDouble() * 2 - 1;
                filter += alpha * (unfiltered - filter);
                filter2 += alpha * (filter - filter2);

                // The contact is a quiet filtered knock, while a damped bass
                // membrane gives the button its soft, close-up controller feel.
                double contact = filter2 * (1.8 + click.Contact) *
                    Math.Exp(-progress * 8);
                double membrane = Math.Sin(phase) * Math.Exp(-progress * 3.6);
                double ramp = Math.Min(1d, i / (Rate * .00065));
                double tail = Math.Min(1d, (length - i) / (Rate * .003));
                double level = click.Gain * Math.Min(ramp, tail);
                samples[offset + i] += (float)(level *
                    (membrane * (1 - click.Contact * .35) + contact * click.Contact));
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
