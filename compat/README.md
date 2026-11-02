# compat

`amd-shim\` is the OpenGL compatibility layer for AMD and Intel graphics: fluphus's
[fgo-arcade-amd-shim](https://github.com/fluphus/fgo-arcade-amd-shim), MIT licence (the LICENSE
file beside it), taken verbatim from commit `1fbf3e4` of that repository. The game asks the driver
for NVIDIA-only extensions; `opengl32.dll` sits in `App\` in front of the system DLL, which it
forwards to as `opengl32real.dll`, and translates those calls onto what AMD and Intel drivers
provide. `amdcfg\amdOglpSettings.cfg` is the driver profile his installer writes beside it.

The launcher installs the three files fluphus's installer writes (`App\opengl32.dll`,
`App\opengl32real.dll` copied from `System32`, `App\amdcfg\amdOglpSettings.cfg`) when the player
picks this layer on the Display page, and removes them when the switch is off. A fresh install on a
PC with no NVIDIA adapter turns the switch on by itself on the first run and gets the older layer
below; this one is one click away. An `App\opengl32.dll` that is not the copy under `amd-shim\` -
his own installer's, or a newer build - is recognised as such and left alone.

His limits, as published: tested only on an RX 7900 XTX, 60 fps guaranteed only in PvP battles on
that card, 1920x1080 only, with a built-in 60 fps cap.

`fgoglcompat.dll` is the older layer that shipped with 1.1.0, and the package ships it again. It is
the one that works on RX 500, RX 6000 and RX 7600 cards and on desktop Ryzen graphics; the newer
layer fails on those at the first battle. A fresh install on a PC with no NVIDIA adapter gets this
one, and installs from before 1.1.1 already carry it and keep running it as they are; the Display
page switches between the two layers and back. It arrived through the community without a licence
file; it is shipped as received and is not modified here.

To try a newer build of either layer, replace the file under `compat\` - `amd-shim\opengl32.dll`,
with its `amdcfg\amdOglpSettings.cfg` if the build ships one, or `fgoglcompat.dll` - and click
Install the layer from the compat folder on the Display page. The launcher installs whatever file
sits there, so a new build of a layer does not need a launcher update. The table below stays as the
record of what the package ships.

| File | SHA-256 |
| --- | --- |
| `amd-shim\opengl32.dll` (458,240 bytes) | `a85042d91ea60a3108f28dfad4673265f2e1f2a402b252436e5d5757c368a0f0` |
| `amd-shim\amdcfg\amdOglpSettings.cfg` | `9cfe518d2ed064c856e0a2a8f0f8686f10ebbe63d0d5995117067bd8276066ae` |
| `fgoglcompat.dll` (659,456 bytes) | `4932eebf73d715949b04c74b7cf1ee9dbabcc6887f666f3524850d374156f66c` |
