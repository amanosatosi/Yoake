$ErrorActionPreference = 'Stop'
# GitHub-hosted Windows starts with a small interactive desktop which clamps
# Avalonia's real MainWindow below the 1440x900 layout we intentionally verify.
# Change the current session mode without writing it to the registry.
#
# Do NOT use CDS_FULLSCREEN here. That flag is intended for temporary
# full-screen application mode and can disappear when the configuring process
# exits, while the UI smoke test runs in the following workflow step.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class VerificationDisplay {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct Mode {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Device;
    public ushort Spec, Driver, Size, Extra;
    public uint Fields;
    public int X, Y;
    public uint Orientation, FixedOutput;
    public short Color, Duplex, YResolution, TTOption, Collate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Form;
    public ushort LogPixels;
    public uint Bits, Width, Height, Flags, Frequency, IcmMethod, IcmIntent,
      MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
  }

  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  public static extern bool EnumDisplaySettings(string device, int index, ref Mode mode);

  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  public static extern int ChangeDisplaySettings(ref Mode mode, uint flags);

  public static string Configure() {
    var mode = new Mode {Size=(ushort)Marshal.SizeOf<Mode>()};
    if (!EnumDisplaySettings(null,-1,ref mode))
      throw new Exception("Cannot read CI display mode.");

    mode.Width=1920;
    mode.Height=1080;
    mode.Fields=0x80000|0x100000; // DM_PELSWIDTH | DM_PELSHEIGHT

    // Flags == 0 applies the mode to the current desktop session but does not
    // persist it to the registry. The hosted runner is ephemeral, so there is
    // nothing to restore after the job.
    var result=ChangeDisplaySettings(ref mode,0);
    if(result!=0)
      throw new Exception("Cannot set CI display to 1920x1080: "+result);

    // Do not trust DISP_CHANGE_SUCCESSFUL alone: verify what the next process
    // in the workflow will actually inherit.
    var current = new Mode {Size=(ushort)Marshal.SizeOf<Mode>()};
    if (!EnumDisplaySettings(null,-1,ref current))
      throw new Exception("Cannot verify CI display mode after changing it.");
    if (current.Width < 1440 || current.Height < 900)
      throw new Exception(
        "CI display remained too small for 1440x900 verification: "+
        current.Width+"x"+current.Height+".");

    return current.Width+"x"+current.Height;
  }
}
'@

$actual = [VerificationDisplay]::Configure()
Write-Host "CI desktop configured to $actual for real 1440x900 workspace verification."
