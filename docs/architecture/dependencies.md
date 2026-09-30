# Managed dependency ledger

The runtime dependency surface is intentionally small.

| Dependency | Scope | Why it exists | AOT/trimming posture | License |
| --- | --- | --- | --- | --- |
| Avalonia 12.1.3 | UI/runtime | Native cross-platform desktop UI | Official NativeAOT guidance; compiled bindings default in v12 | MIT |
| Avalonia.Desktop 12.1.3 | app/runtime | Windows/Linux desktop platform backends | Same Avalonia AOT constraints | MIT |
| Avalonia.Themes.Fluent 12.1.3 | app/runtime | Baseline native desktop control theme | Static XAML resources; no reflection DI | MIT |
| Microsoft.NET.Test.Sdk 18.10.1 | tests only | `dotnet test` host | Not shipped in app | MIT |
| xunit 2.9.3 | tests only | Unit assertions/framework | Not shipped in app | Apache-2.0 |
| xunit.runner.visualstudio 4.0.0 | tests only | VSTest adapter for CI/IDE | Not shipped in app | Apache-2.0 |

No MVVM toolkit, DI framework, SVG runtime, logging framework, serializer package, docking framework, or media wrapper is added in M0. New dependencies need an entry here with a concrete justification.
