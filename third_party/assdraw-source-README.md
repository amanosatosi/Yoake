# Corresponding source for bundled ASSDraw

The separate ASSDraw3.exe is built without source changes from
https://github.com/amanosatosi/assdraw at the commit in assdraw.json.
Its historical AGG-derived spline files retain GPL version 2 or later notices.
The separately published corresponding-source artifact contains:
assdraw-source.zip, agg-source.zip and the official wxWidgets-3.2.11.zip (including
its bundled third-party sources and their licenses). When redistributing the executable,
provide these exact sources alongside it and preserve licenses/assdraw. No additional restriction is imposed
on redistribution or modification of ASSDraw under its applicable licenses.

To rebuild with CMake 3.24+ and the Visual Studio 2022 x64 C++ tools:

1. Extract assdraw-source.zip into assdraw, agg-source.zip into agg, and the
   wxWidgets archive into wx. Identify its inner wxWidgets-3.2.11 directory.
2. From a developer PowerShell, run:

```powershell
cmake -S assdraw -B build -G "Visual Studio 17 2022" -A x64 -DBUILD_TESTING=ON -DFETCHCONTENT_SOURCE_DIR_AGG="$PWD/agg" -DFETCHCONTENT_SOURCE_DIR_WXWIDGETS="$PWD/wx/wxWidgets-3.2.11"
cmake --build build --config Release --parallel 2
ctest --test-dir build -C Release --output-on-failure
```

The supplied dependency source overrides avoid network downloads. The unchanged
fork's CMakeLists.txt controls static wxWidgets/AGG and /MT linkage. build_assdraw.ps1
records Yoake CI's acquisition, checks, build and source packaging; the adjacent
manifest records the actual compiler, executable hash and source hashes. Windows
SDK, MSVC and CMake are normal external build tools and are not redistributed here.

Historical ASSDraw BSD attribution, GPL-2.0 text, AGG copying notice and wxWidgets
licenses are under licenses/assdraw in the portable distribution. The original
license headers remain in all source archives. No BSD-only claim is made for the
combined ASSDraw executable. Yoake remains a separate application and communicates
with ASSDraw only through the user's existing Copy ASS clipboard workflow.
