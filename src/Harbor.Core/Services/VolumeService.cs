using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;  // IMMNotificationClient

namespace Harbor.Core.Services;

public enum VolumeIconState
{
    Muted,
    Low,
    Medium,
    High,
}

public sealed class AudioOutputDevice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class VolumeChangedEventArgs : EventArgs
{
    public int VolumePercent { get; init; }
    public bool IsMuted { get; init; }
    public VolumeIconState IconState { get; init; }
}

public sealed class VolumeService : IDisposable, IMMNotificationClient
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _activeDevice;
    private AudioEndpointVolume? _endpointVolume;
    private readonly object _lock = new();
    private bool _disposed;

    public int VolumePercent { get; private set; }
    public bool IsMuted { get; private set; }
    public VolumeIconState IconState { get; private set; }
    public string ActiveDeviceName { get; private set; } = "";
    public IReadOnlyList<AudioOutputDevice> OutputDevices => GetOutputDevices();

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    public event EventHandler? DevicesChanged;

    public VolumeService()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
        AttachToDefaultDevice();
        Trace.WriteLine("[Harbor] VolumeService: Initialized.");
    }

    private void AttachToDefaultDevice()
    {
        lock (_lock)
        {
            DetachFromDevice();

            try
            {
                _activeDevice = _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_activeDevice is null) return;

                _endpointVolume = _activeDevice.AudioEndpointVolume;
                _endpointVolume.OnVolumeNotification += OnEndpointVolumeNotification;

                ActiveDeviceName = _activeDevice.FriendlyName;
                UpdateState(_endpointVolume.MasterVolumeLevelScalar, _endpointVolume.Mute);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] VolumeService: Failed to attach to default device: {ex.Message}");
            }
        }
    }

    private void DetachFromDevice()
    {
        if (_endpointVolume is not null)
        {
            try { _endpointVolume.OnVolumeNotification -= OnEndpointVolumeNotification; } catch { }
            _endpointVolume = null;
        }

        if (_activeDevice is not null)
        {
            try { _activeDevice.Dispose(); } catch { }
            _activeDevice = null;
        }
    }

    private void UpdateState(float scalar, bool muted)
    {
        var percent = (int)Math.Round(scalar * 100);
        VolumePercent = Math.Clamp(percent, 0, 100);
        IsMuted = muted;
        IconState = ComputeIconState(VolumePercent, IsMuted);
    }

    private static VolumeIconState ComputeIconState(int percent, bool muted)
    {
        if (muted || percent == 0) return VolumeIconState.Muted;
        if (percent <= 33) return VolumeIconState.Low;
        if (percent <= 66) return VolumeIconState.Medium;
        return VolumeIconState.High;
    }

    public void SetVolume(int percent)
    {
        lock (_lock)
        {
            if (_endpointVolume is null) return;
            var clamped = Math.Clamp(percent, 0, 100);
            _endpointVolume.MasterVolumeLevelScalar = clamped / 100f;
        }
    }

    public void ToggleMute()
    {
        lock (_lock)
        {
            if (_endpointVolume is null) return;
            _endpointVolume.Mute = !_endpointVolume.Mute;
        }
    }

    public void SetActiveDevice(string deviceId)
    {
        // NAudio doesn't support programmatic default device switching directly.
        // This would require PolicyConfig COM interop which is undocumented.
        // For now, log the request — a future phase can add PolicyConfig support.
        Trace.WriteLine($"[Harbor] VolumeService: SetActiveDevice requested for {deviceId} (not yet implemented).");
    }

    private IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var devices = new List<AudioOutputDevice>();
        try
        {
            if (_enumerator is null) return devices;

            var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var activeId = _activeDevice?.ID;

            foreach (var device in collection)
            {
                devices.Add(new AudioOutputDevice
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsActive = device.ID == activeId,
                });
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] VolumeService: Failed to enumerate devices: {ex.Message}");
        }
        return devices;
    }

    private void OnEndpointVolumeNotification(AudioVolumeNotificationData data)
    {
        lock (_lock)
        {
            UpdateState(data.MasterVolume, data.Muted);
        }

        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs
        {
            VolumePercent = VolumePercent,
            IsMuted = IsMuted,
            IconState = IconState,
        });
    }

    // IMMNotificationClient
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || role != Role.Multimedia) return;

        AttachToDefaultDevice();

        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs
        {
            VolumePercent = VolumePercent,
            IsMuted = IsMuted,
            IconState = IconState,
        });
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceRemoved(string deviceId)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Not needed
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            DetachFromDevice();
        }

        if (_enumerator is not null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            _enumerator.Dispose();
            _enumerator = null;
        }

        Trace.WriteLine("[Harbor] VolumeService: Disposed.");
    }
}
