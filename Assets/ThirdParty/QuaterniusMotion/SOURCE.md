# Quaternius motion data

Author: Quaternius. License: CC0-1.0 (LICENSE.txt).
Original: https://quaternius.com/packs/universalanimationlibrary.html
Download mirror: https://github.com/J-Ponzo/gltf-universal-animation-library
Pinned source commit: e24c23cf2a1323488a3faa226ea7ea21f644b73e

Only Idle_Loop and Walk_Loop are baked into Resources/Characters/RemielleMotion.json.
Tools/bake-remielle-motion.cjs converts source poses, closes loop drift and records Unity/PMX coordinate conventions.
Source downloads are local ignored Artifacts files. No game-extracted animations are used.

Source SHA256:
AnimationLibrary.gltf: 0ff075c7ad6855c5c2c37a171592ee8f0d6ab2f58259e2be77a9b63dd8027765
AnimationLibrary.bin: 6e65377d81558333c4093dbb144a48fd19019343d82b1a3a7992a98ec0e0543c
CC0.txt: a2010f343487d3f7618affe54f789f5487602331c0a8d03f49e9a7c547cf0499

Validation: external C# compile and data integrity checks passed.
Unity retargeting, foot contacts and clothing intersections still need visual verification.
