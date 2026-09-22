# Third-party notices

## SayAll / remote-mic-app

The public macOS sources derive from [HD838A/remote-mic-app](https://github.com/HD838A/remote-mic-app), revision `838db98b781a7039347158a0eda838e5c639910d`. Copyright (C) 2026 SayAll contributors. Software license: GPL-3.0-only. Voice Anything modifications: Copyright (C) 2026 Voice Anything contributors.

The upstream proprietary SayAll application icons are excluded. The application icons included here come from the preceding VibeControl development workspace, not from those proprietary SayAll assets. Original upstream third-party attributions are preserved in [docs/UPSTREAM-NOTICES.md](docs/UPSTREAM-NOTICES.md); that document describes upstream components, including optional components that this build does not bundle.

## ATVV implementation references

The ATVV protocol, ADPCM decoder and HID mappings reference [xxb26553663-star/remote-bridge-hub](https://github.com/xxb26553663-star/remote-bridge-hub), revision `8a93f321ac71a602300c6cd77f7256fa4b63068e`, GPL-3.0-only, through the SayAll adaptation.

## Frida Gadget

The Windows installer uses the official [Frida](https://github.com/frida/frida) Gadget 17.15.3 for Windows x86_64. The uncompressed DLL must have SHA-256 `6FCA4007B2284C765A6C15C967A741F536B5865BF83867326A54029A3B752748` and length 23,575,552 bytes. It is not checked into this source repository. The upstream wxWindows Library Licence 3.1, incorporating the GNU Library GPL and Frida's exception, is included in `docs/licenses/FRIDA.txt` and `docs/licenses/FRIDA-LGPL.txt`. The packaging script includes these texts with the runtime.

The Microsoft Windows driver is verified in place and is never redistributed by this repository.

## Sparkle and audio devices

The macOS public baseline links [Sparkle](https://github.com/sparkle-project/Sparkle) 2.9.4. Its upstream license and bundled notices are preserved in `docs/licenses/SPARKLE.txt` and copied into the application bundle. This fork disables the original SayAll update and hardware-announcement endpoints.

[VB-CABLE](https://vb-audio.com/Cable/) is a separately installed Windows audio device. [BlackHole](https://github.com/ExistentialAudio/BlackHole), GPL-3.0, is a separately installed macOS audio device. Neither installer nor driver is bundled here. There is no redistribution grant for third-party installers merely because they are compatible.

## Hardware imagery

The RC003 photograph originates in the prior development workspace/upstream hardware mapping resources. Rights in the photograph and depicted products remain with their owners; the program's GPL license does not grant additional image or trademark rights. Hardware pictures do not imply manufacturer endorsement.
