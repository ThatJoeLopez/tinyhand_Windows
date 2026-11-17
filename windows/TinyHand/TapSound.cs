using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TinyHand;

/// Plays the tap sound. The WAV files are read into memory once and played with PlaySound on a
/// background thread, so a slow audio device costs a missing tap sound, never a frozen pointer.
internal sealed class TapSound : IDisposable
{
    // Unmanaged copies: an async PlaySound keeps reading the buffer after the call returns,
    // so it must not move or be freed. They live as long as the app.
    private static readonly Dictionary<TapSoundChoice, IntPtr> Buffers = new();
    private static readonly object LoadGate = new();

    private readonly BlockingCollection<IntPtr> queue = new(boundedCapacity: 4);
    private readonly Thread worker;
    private TapSoundChoice choice;

    /// Read and written on the UI thread.
    public bool Enabled { get; set; } = true;

    public TapSound() : this(Preferences.TapSound)
    {
    }

    public TapSound(TapSoundChoice choice)
    {
        this.choice = choice;
        worker = new Thread(Run) { Name = "TinyHand tap sound", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        worker.Start();
    }

    public void Load(TapSoundChoice next) => choice = next;

    /// Reads the files ahead of the first tap.
    public void Warm()
    {
        TapSoundChoice c = choice;
        ThreadPool.QueueUserWorkItem(_ => Read(c));
    }

    public void Play()
    {
        if (!Enabled)
        {
            return;
        }
        PlayAlways();
    }

    /// Plays regardless of Enabled, for previews in Settings.
    public void PlayAlways()
    {
        IntPtr data = Read(choice);
        if (data != IntPtr.Zero)
        {
            queue.TryAdd(data);
        }
    }

    private void Run()
    {
        foreach (IntPtr data in queue.GetConsumingEnumerable())
        {
            try
            {
                // SND_ASYNC from memory returns at once; the next tap cuts the previous one off,
                // which suits a sound this short.
                Native.PlaySound(data, IntPtr.Zero, Native.SND_ASYNC | Native.SND_MEMORY | Native.SND_NODEFAULT);
            }
            catch (Exception e)
            {
                Log.Write($"tap sound failed: {e.Message}");
            }
        }
    }

    private static IntPtr Read(TapSoundChoice c)
    {
        lock (LoadGate)
        {
            if (Buffers.TryGetValue(c, out IntPtr hit))
            {
                return hit;
            }
            string? path = Paths.Asset(Path.Combine("sounds", c.FileName()));
            if (path == null)
            {
                Log.Write($"sound {c.FileName()} not found");
                return IntPtr.Zero;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                IntPtr data = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, data, bytes.Length);
                Buffers[c] = data;
                return data;
            }
            catch (IOException e)
            {
                Log.Write($"could not read {path}: {e.Message}");
                return IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
    }
}
