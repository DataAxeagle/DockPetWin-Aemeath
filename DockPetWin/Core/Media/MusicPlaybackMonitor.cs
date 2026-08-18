using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace DockPetWin.Core.Media;

public sealed class MusicPlaybackMonitor
{
    public bool IsQQMusicRunning()
    {
        return QQMusicProcesses().Count > 0;
    }

    public async Task<bool> IsQQMusicPlayingAsync()
    {
        var processes = QQMusicProcesses();
        if (processes.Count == 0)
        {
            return false;
        }

        var mediaSessionState = await TryGetQQMusicMediaSessionStateAsync();
        if (mediaSessionState is not null)
        {
            return mediaSessionState == QQMusicPlaybackState.Playing;
        }

        var targetProcessIds = processes.Select(process => process.Id).ToHashSet();
        try
        {
            if (HasActiveAudioSession(targetProcessIds))
            {
                return true;
            }
        }
        catch
        {
        }

        return processes.Any(process => LooksLikeTrackTitle(process.MainWindowTitle));
    }

    private static async Task<QQMusicPlaybackState?> TryGetQQMusicMediaSessionStateAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            foreach (var session in manager.GetSessions())
            {
                if (!IsQQMusicSource(session.SourceAppUserModelId))
                {
                    continue;
                }

                var playbackStatus = session.GetPlaybackInfo().PlaybackStatus;
                return playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    ? QQMusicPlaybackState.Playing
                    : QQMusicPlaybackState.NotPlaying;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsQQMusicSource(string? sourceAppUserModelId)
    {
        var text = sourceAppUserModelId ?? "";
        return text.Contains("qqmusic", StringComparison.OrdinalIgnoreCase)
            || text.Contains("qq音乐", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tencent.qqmusic", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<QQMusicProcessInfo> QQMusicProcesses()
    {
        var processes = new List<QQMusicProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!IsQQMusicProcess(process))
                {
                    continue;
                }

                processes.Add(new QQMusicProcessInfo(
                    (uint)process.Id,
                    SafeMainWindowTitle(process)));
            }
        }

        return processes;
    }

    private static bool IsQQMusicProcess(Process process)
    {
        try
        {
            if (process.ProcessName.Contains("qqmusic", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return process.MainWindowTitle.Contains("QQ音乐", StringComparison.OrdinalIgnoreCase)
                || process.MainWindowTitle.Contains("QQ Music", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string SafeMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool LooksLikeTrackTitle(string title)
    {
        var text = title.Trim();
        if (text.Length < 3)
        {
            return false;
        }

        if (string.Equals(text, "QQ音乐", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "QQ Music", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "QQMusic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "腾讯QQ音乐", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Contains(" - ", StringComparison.Ordinal)
            || text.Contains(" — ", StringComparison.Ordinal)
            || text.Contains(" – ", StringComparison.Ordinal);
    }

    private static bool HasActiveAudioSession(IReadOnlySet<uint> processIds)
    {
        object? enumeratorObject = null;
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            enumeratorObject = new MMDeviceEnumerator();
            deviceEnumerator = (IMMDeviceEnumerator)enumeratorObject;
            deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);

            var sessionManagerId = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref sessionManagerId, ClsCtx.All, IntPtr.Zero, out var managerObject);
            sessionManager = (IAudioSessionManager2)managerObject;
            sessionManager.GetSessionEnumerator(out sessionEnumerator);
            sessionEnumerator.GetCount(out var count);

            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? session = null;
                try
                {
                    sessionEnumerator.GetSession(i, out session);
                    if (session is not IAudioSessionControl2 session2)
                    {
                        continue;
                    }

                    session.GetState(out var state);
                    session2.GetProcessId(out var processId);
                    if (state == AudioSessionState.Active && processIds.Contains(processId))
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComObject(session);
                }
            }

            return false;
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(device);
            ReleaseComObject(deviceEnumerator);
            ReleaseComObject(enumeratorObject);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private enum EDataFlow
    {
        Render = 0
    }

    private enum ERole
    {
        Multimedia = 1
    }

    private enum ClsCtx
    {
        All = 23
    }

    private enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2
    }

    private enum QQMusicPlaybackState
    {
        NotPlaying,
        Playing
    }

    private sealed record QQMusicProcessInfo(uint Id, string MainWindowTitle);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, ClsCtx clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, out IntPtr sessionControl);

        int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags, out IntPtr audioVolume);

        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);

        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out AudioSessionState state);

        int GetDisplayName(out IntPtr displayName);

        int SetDisplayName(string value, Guid eventContext);

        int GetIconPath(out IntPtr iconPath);

        int SetIconPath(string value, Guid eventContext);

        int GetGroupingParam(out Guid groupingId);

        int SetGroupingParam(Guid groupingId, Guid eventContext);

        int RegisterAudioSessionNotification(IntPtr client);

        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2 : IAudioSessionControl
    {
        int GetSessionIdentifier(out IntPtr sessionId);

        int GetSessionInstanceIdentifier(out IntPtr sessionInstanceId);

        int GetProcessId(out uint processId);

        int IsSystemSoundsSession();

        int SetDuckingPreference(bool optOut);
    }
}
