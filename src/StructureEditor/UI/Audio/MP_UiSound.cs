using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StructureEditor;

// Dry gamepad-button clicks: a gentle midrange plastic snap with a short
// rubber membrane decay. No musical tones, shrill square waves, or dull thuds.
// Samples are synthesized locally and mixed asynchronously via NAudio.
internal static class MP_UiSound
{
    internal enum Cue { Navigate, Confirm, Toggle, Close, Open, Cancel, Focus, Error, Place, Select }

    internal const int Rate = 44100;
    private static readonly object Sync = new();
    private static readonly long[] LastPlayed = new long[Enum.GetValues<Cue>().Length];
    private static readonly float[][] Samples = Enum.GetValues<Cue>().Select(MP_GamepadSoundDesign.Synthesize).ToArray();
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
