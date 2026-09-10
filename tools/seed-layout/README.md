# Base layout seed generator

One-off scaffolding, NOT the authoring tool.

It produced the first `Assets/Data/base-layout.json` (32 slots, 21.6% density, verified
0 overlaps / 0 out of bounds) so we would not hand-write 32 coordinate pairs and get one
wrong. The building roster it carries now lives in `Assets/Data/building-defs.json`, which
is the real source from here on.

The actual authoring path is a Unity scene tool where slots are dragged by hand and saved
back to the same `Assets/Data/base-layout.json`. See `docs/project-layout.md` rule 1 for
why the tool must write the runtime file directly rather than an intermediate format.

Run: `powershell -File tools\seed-layout\gen-layout.ps1`
It overwrites both JSON files, so do not run it after hand-editing the layout.

ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI (CP949 on
Korean Windows) and mangles Hangul badly enough to break parsing.