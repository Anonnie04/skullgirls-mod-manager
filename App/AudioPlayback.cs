using System.Runtime.InteropServices;

namespace SkullgirlsModManager.Desktop;

// Keep PCM alive until waveOut releases its buffer. Device position drives the UI.
internal sealed class AudioPlayback : IDisposable
{
    private IntPtr device, samples, header;
    private readonly DecodedAudio audio;
    private int firstFrame;
    private bool prepared;
    public bool Paused { get; private set; }
    public bool Active { get; private set; }
    public double Position
    {
        get
        {
            if (!Active) return firstFrame / (double)audio.SampleRate;
            var time = new WaveTime { Type = 4 };
            Check(waveOutGetPosition(device, ref time, (uint)Marshal.SizeOf<WaveTime>()));
            if (time.Type != 4) throw new InvalidOperationException("Audio device did not return a byte position.");
            return Math.Min(audio.Duration, firstFrame / (double)audio.SampleRate + time.Value / (double)(audio.SampleRate * audio.Channels * 2));
        }
    }
    public AudioPlayback(DecodedAudio audio)
    {
        this.audio = audio;
        try
        {
            var format = new WaveFormat { Format = 1, Channels = (ushort)audio.Channels, Rate = (uint)audio.SampleRate,
                BytesPerSecond = (uint)(audio.SampleRate * audio.Channels * 2), Align = (ushort)(audio.Channels * 2), Bits = 16 };
            Check(waveOutOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
            samples = Marshal.AllocHGlobal(checked(audio.Samples.Length * 2));
            Marshal.Copy(audio.Samples, 0, samples, audio.Samples.Length);
            header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        }
        catch { Dispose(); throw; }
    }
    public void Start(double seconds)
    {
        ReleaseBuffer();
        firstFrame = (int)Math.Clamp(seconds * audio.SampleRate, 0, audio.Samples.Length / audio.Channels);
        Paused = false;
        int offset = checked(firstFrame * audio.Channels * 2);
        int length = audio.Samples.Length * 2 - offset;
        if (length == 0) return;
        Marshal.StructureToPtr(new WaveHeader { Data = IntPtr.Add(samples, offset), Length = (uint)length }, header, false);
        Check(waveOutPrepareHeader(device, header, (uint)Marshal.SizeOf<WaveHeader>())); prepared = true;
        Check(waveOutWrite(device, header, (uint)Marshal.SizeOf<WaveHeader>()));
        Check(waveOutRestart(device)); Active = true;
    }
    public void Pause() { if (!Active) return; Check(waveOutPause(device)); Paused = true; }
    public void Resume() { if (!Active) return; Check(waveOutRestart(device)); Paused = false; }
    private void ReleaseBuffer()
    {
        if (device == IntPtr.Zero) return;
        Check(waveOutReset(device)); Active = false;
        if (prepared) { Check(waveOutUnprepareHeader(device, header, (uint)Marshal.SizeOf<WaveHeader>())); prepared = false; }
    }
    public void Dispose()
    {
        ReleaseBuffer();
        if (device != IntPtr.Zero) { waveOutClose(device); device = IntPtr.Zero; }
        if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
        if (samples != IntPtr.Zero) { Marshal.FreeHGlobal(samples); samples = IntPtr.Zero; }
    }
    private static void Check(uint result)
    {
        if (result == 0) return;
        var message = new System.Text.StringBuilder(256); waveOutGetErrorText(result, message, message.Capacity);
        throw new InvalidOperationException($"Audio output: {message} ({result})");
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat { public ushort Format, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveTime { public uint Type, Value, Padding; }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(IntPtr handle, ref WaveTime time, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutPause(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutRestart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetErrorTextW")] private static extern uint waveOutGetErrorText(uint error, System.Text.StringBuilder text, int size);
}
