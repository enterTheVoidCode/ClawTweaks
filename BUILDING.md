# Building the ClawTweaks widget

This repository holds the **Game Bar widget** and the shared data model it uses. The ClawTweaks
background helper — the process that actually drives TDP, fan curves, LED and the controller — is a
separate, private project and is not here.

That has a consequence worth knowing before you start: **the widget builds and runs, but shows no
values.** Everything it displays arrives from the helper over a named pipe. What you can work on is
layout, styling, XAML, converters and controller navigation, which is the bulk of the UI work.

## Requirements

- Windows 10/11 x64
- Visual Studio 2022 or newer with the **Universal Windows Platform development** workload
- Windows SDK 10.0.26100.0

## Compile

```
msbuild XboxGamingBar.sln /t:Restore /p:Configuration=Release /p:Platform=x64
msbuild XboxGamingBar\XboxGamingBar.csproj /p:Configuration=Release /p:Platform=x64 /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=false
```

The two signing properties matter. Without them MSBuild runs the packaging step, which needs a
code-signing certificate this repository deliberately does not contain, and the build stops at
`MSB4044: RemoveDisposableSigningCertificate ... CertificateThumbprint`. That is not a broken
checkout — packaging simply is not part of compiling.

## Package and run it

To actually see the widget in the Game Bar you need a package, and a package needs to be signed.
Create your own test certificate, trust it locally, and build `XboxGamingBarPackage`. Visual Studio
does this for you: right-click the package project → **Publish → Create App Packages → Sideloading**,
and let it generate a test certificate.

Once installed, open the Game Bar (`Win+G`) and pick ClawTweaks from the widget menu.

## Two things that will look like bugs and are not

**The icons are missing.** `XboxGamingBar/Assets/ButtonIcons/` is not part of this repository, so
button glyphs render blank. The project file includes that folder as a wildcard precisely so its
absence does not break the build — if you need placeholders while working on a view, drop your own
PNGs in with the names the code asks for (see `Converters/GamepadButtonIconConverter.cs`).

**Everything reads empty or disabled.** No helper, no data. Sliders sit at their defaults, toggles do
nothing, the metrics tiles stay blank. Only the parts that need no live values — navigation, layout,
theming — behave the way they will in a full install.

## Warnings you can ignore

`MSB3277` about conflicting versions of `Microsoft.Win32.Registry` predates this repository split and
is harmless. Do not "fix" it by pinning a version without checking what UWP resolves at runtime.
