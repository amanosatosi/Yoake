$ErrorActionPreference = 'Stop'
# The hosted desktop otherwise clamps the real Window to roughly 1040x780.
# This CI-only temporary display mode lets us verify the shipped 1440x900 layout.
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
  public static void Configure() {
    var mode = new Mode {Size=(ushort)Marshal.SizeOf<Mode>()};
    if (!EnumDisplaySettings(null,-1,ref mode)) throw new Exception("Cannot read CI display mode.");
    mode.Width=1920; mode.Height=1080; mode.Fields=0x80000|0x100000;
    var result=ChangeDisplaySettings(ref mode,4); // CDS_FULLSCREEN: do not persist.
    if(result!=0) throw new Exception("Cannot set CI display to 1920x1080: "+result);
  }
}
'@
[VerificationDisplay]::Configure()
Write-Host 'CI desktop configured for real 1440x900 workspace verification.'
