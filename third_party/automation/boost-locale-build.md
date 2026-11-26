# Boost.Locale build compatibility

Yoake's Automation manifest resolves Boost.Locale 1.91.0 from its pinned vcpkg
baseline. The Boost Software License 1.0 applies to the Boost CMake source. The
vcpkg patch is distributed under the [Microsoft MIT license](../licenses/vcpkg-MIT.txt).

CMake 4.5 rejects the upstream source-group tree because the umbrella header
`include/boost/locale.hpp` sits beside the `include/boost/locale` directory.
Windows Automation builds backport the source-group correction from
[vcpkg PR 55069](https://github.com/microsoft/vcpkg/pull/55069), merged as
`5ad42ec00d7723dde08c0df0910d9044e54ef032`. The build creates an overlay from
the pinned port recipe and applies the patch during source extraction.

The patch only changes IDE source grouping. The port's archive hash, version,
compiled files and ICU features stay the same. Updating the complete vcpkg
baseline would change other pinned packages.
