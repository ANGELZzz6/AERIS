using System;
using System.Runtime.InteropServices;

namespace IslasEscritorio.Core;

/// <summary>
/// Servicio ligero para consultar y ajustar el volumen maestro de Windows mediante la interfaz nativa CoreAudio (COM).
/// </summary>
public static class AudioVolumeService
{
    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    }

    [Guid("D6660638-47A4-4FEA-A256-0254063DB6F5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlNotification(IntPtr pNotify);
        int UnregisterControlNotification(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevelScalar, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevelScalar);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device) == 0 && device is not null)
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object obj) == 0 && obj is IAudioEndpointVolume volume)
                {
                    return volume;
                }
            }
        }
        catch
        {
            // Ignorar excepciones de COM si no se encuentra un dispositivo predeterminado
        }
        return null;
    }

    public static float GetVolume()
    {
        var vol = GetEndpointVolume();
        if (vol is not null)
        {
            try
            {
                if (vol.GetMasterVolumeLevelScalar(out float level) == 0)
                {
                    return level * 100f;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
        return 50f;
    }

    public static void SetVolume(float volumePercent)
    {
        float scalar = Math.Clamp(volumePercent / 100f, 0f, 1f);
        var vol = GetEndpointVolume();
        if (vol is not null)
        {
            try
            {
                Guid context = Guid.Empty;
                vol.SetMasterVolumeLevelScalar(scalar, ref context);
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
    }

    public static bool ToggleMute()
    {
        var vol = GetEndpointVolume();
        if (vol is not null)
        {
            try
            {
                Guid context = Guid.Empty;
                if (vol.GetMute(out bool isMuted) == 0)
                {
                    bool newMute = !isMuted;
                    vol.SetMute(newMute, ref context);
                    return newMute;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
        return false;
    }

    public static bool IsMuted()
    {
        var vol = GetEndpointVolume();
        if (vol is not null)
        {
            try
            {
                if (vol.GetMute(out bool isMuted) == 0)
                {
                    return isMuted;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
        return false;
    }
}
