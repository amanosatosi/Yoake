#!/usr/bin/env python3
from pathlib import Path
import json, re, subprocess, sys, xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]

def fail(msg):
    print(f"source-policy: {msg}", file=sys.stderr); raise SystemExit(1)

for path in ROOT.rglob('*.json'):
    if any(part in {'bin','obj','.git'} for part in path.parts): continue
    try: json.loads(path.read_text(encoding='utf-8'))
    except Exception as exc: fail(f"invalid JSON {path.relative_to(ROOT)}: {exc}")
for pattern in ('*.csproj', '*.axaml'):
    for path in ROOT.rglob(pattern):
        try: ET.parse(path)
        except Exception as exc: fail(f"invalid XML {path.relative_to(ROOT)}: {exc}")

versions=json.loads((ROOT/'third_party/versions.json').read_text())
for pin in (versions['vcpkg']['commit'], versions['ffmpeg']['tagCommit'], versions['ffms2']['commit'], versions['mangetsu']['commit']):
    if not re.fullmatch(r'[0-9a-f]{40}', pin): fail(f"native dependency ref is not immutable: {pin}")

for svg in (ROOT/'assets/icons/functional').glob('*.svg'):
    text=svg.read_text(encoding='utf-8').lower()
    if '<image' in text or 'base64' in text or 'data:image' in text: fail(f"raster payload in {svg}")
    node=ET.fromstring(text)
    if node.attrib.get('viewbox') != '0 0 24 24': fail(f"functional icon lacks canonical viewBox: {svg}")

forbidden = ['Assembly.Load(', 'Assembly.LoadFrom(', 'Reflection.Emit', 'DynamicMethod(', 'System.Reflection.Emit']
for path in (ROOT/'src').rglob('*.cs'):
    text=path.read_text(encoding='utf-8')
    for token in forbidden:
        if token in text: fail(f"forbidden AOT pattern {token!r} in {path.relative_to(ROOT)}")
    if 'IntPtr' in text and 'Yoake.Native' not in str(path): fail(f"native pointer leaked outside Yoake.Native: {path.relative_to(ROOT)}")

ui=(ROOT/'src/Yoake.UI/MainWindow.axaml').read_text(encoding='utf-8')
for marker in ('Name="UpperWorkspace"','Name="VisualColumn"','Name="TemporalTextColumn"','Name="SubtitleGridRegion"','VIDEO','AUDIO','EDIT PANEL','SUBTITLE GRID'):
    if marker not in ui: fail(f"required workspace marker missing: {marker}")
if 'SystemDecorations="None"' in ui or 'WindowDecorations="None"' in ui:
    fail('window chrome must retain native system/snap/maximize behavior')
window_code=(ROOT/'src/Yoake.UI/MainWindow.axaml.cs').read_text(encoding='utf-8')
if 'WindowDecorations.None' in window_code or 'SystemDecorations.None' in window_code:
    fail('code-behind must not replace native window behavior with borderless/fake chrome')
if 'ExtendClientAreaToDecorationsHint = true' in window_code and 'WindowDecorations.Full' not in window_code:
    fail('extended Windows titlebar must retain Full native decorations')

if not (ROOT/'LICENSE').is_file(): fail('top-level Yoake LICENSE is missing')
if not (ROOT/'third_party/licenses/Avalonia-MIT.txt').is_file(): fail('Avalonia MIT license text is missing')
app_project=(ROOT/'src/Yoake.App/Yoake.App.csproj').read_text(encoding='utf-8')
for marker in ('ApplicationIcon', 'AvaloniaIncludeApplicationIconAsWindowIcon', 'generate-app-icon.ps1', 'yoake.ico'):
    if marker not in app_project: fail(f'Windows application icon pipeline marker missing: {marker}')
smoke=(ROOT/'ci/smoke_windows.ps1').read_text(encoding='utf-8')
if 'clean early exit' in smoke.lower() or 'if ($process.exitcode -ne 0)' in smoke.lower():
    fail('startup smoke must fail on any early desktop-app exit')
commands=(ROOT/'src/Yoake.Core/Commands/CommandRegistry.cs').read_text(encoding='utf-8')
registry_adapter=(ROOT/'src/Yoake.UI/Commands/RegistryCommand.cs').read_text(encoding='utf-8')
if 'ConfigureAwait(false)' in commands:
    fail('CommandRegistry must not force command-state continuation off the caller context')
for marker in ('ReportFailure', 'CommandFailed'):
    if marker not in commands: fail(f'central command failure handling missing: {marker}')
if 'Dispatcher.UIThread' not in registry_adapter:
    fail('RegistryCommand must marshal CanExecuteChanged through Avalonia dispatcher')
undo=(ROOT/'src/Yoake.Core/Undo/UndoManager.cs').read_text(encoding='utf-8')
for marker in ('void Commit()', 'void Cancel()', 'if (!_completed) Cancel();'):
    if marker not in undo: fail(f'rollback-by-default undo invariant missing: {marker}')
workspace=(ROOT/'src/Yoake.Core/Workspace/WorkspaceManager.cs').read_text(encoding='utf-8')
if 'DocumentSession : INotifyPropertyChanged' not in workspace:
    fail('DocumentSession must remain observable for tab title/dirty state')

package=(ROOT/'ci/package_windows.ps1').read_text(encoding='utf-8')
for marker in ('native\\win-x64', 'dumpbin.exe', 'runtime-manifest.json', 'Avalonia-MIT.txt', 'VCToolsRedistDir'):
    if marker not in package: fail(f'portable runtime staging marker missing: {marker}')
if "Copy-Item (Join-Path $NativeRoot 'ffmpeg')" in package or "Copy-Item (Join-Path $NativeRoot 'ffms2')" in package:
    fail('portable packager must not copy native development/install trees wholesale')

# The application owns the process-wide safe DLL search configuration.
# Keep that invariant separate from the CI verifier implementation so the
# verifier can use a stricter one-shot LoadLibraryExW probe without policy
# falsely requiring it to duplicate application startup behavior.
windows_runtime=(ROOT/'src/Yoake.Native/WindowsNativeRuntime.cs').read_text(encoding='utf-8')
for marker in ('SetDefaultDllDirectories', 'AddDllDirectory', 'LoadLibrarySearchDefaultDirs', 'native", "win-x64'):
    if marker not in windows_runtime: fail(f'packaged Windows runtime search marker missing: {marker}')

verify_runtime=(ROOT/'ci/verify_windows_runtime.ps1').read_text(encoding='utf-8')
for marker in ('LoadLibraryExW', 'LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR', 'LOAD_LIBRARY_SEARCH_SYSTEM32', 'runtime-manifest.json'):
    if marker not in verify_runtime: fail(f'safe runtime verification marker missing: {marker}')
if "NativeLibrary]::Load('ffms2.dll')" in verify_runtime:
    fail('runtime verifier must not fall back to NativeLibrary.Load by bare DLL name')

# A playable editor baseline requires all three pieces together: FFMS2 media,
# a real audio clock/output path, and Mangetsu compositing into decoded frames.
playback=(ROOT/'src/Yoake.UI/ViewModels/MainWindowViewModel.Playback.cs').read_text(encoding='utf-8')
renderer=(ROOT/'src/Yoake.Native/MangetsuSubtitleRenderer.cs').read_text(encoding='utf-8')
audio_output=(ROOT/'src/Yoake.Native/WindowsWaveOutStream.cs').read_text(encoding='utf-8')
for marker in ('StartPlaybackAsync', 'RefreshVideoFrameAsync', 'CompositeSubtitles'):
    if marker not in playback: fail(f'playable editor playback marker missing: {marker}')
for marker in ('ass_render_frame_rgba', 'ass_composite_images_bgra', 'mangetsu.dll'):
    if marker not in renderer: fail(f'Mangetsu preview marker missing: {marker}')
for marker in ('waveOutOpen', 'waveOutWrite', 'waveOutGetPosition'):
    if marker not in audio_output: fail(f'audio output marker missing: {marker}')
for marker in ("mangetsu\\\\bin", "entrypoints = @('ffms2.dll', 'mangetsu.dll')"):
    if marker not in package: fail(f'Mangetsu portable staging marker missing: {marker}')

result=subprocess.run([sys.executable, str(ROOT/'tools/generate-icons.py'), '--check'])
if result.returncode: fail('generated icon catalog is stale')
print('source-policy: OK')
