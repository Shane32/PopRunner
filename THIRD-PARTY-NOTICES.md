# Third-party materials and references

## Prince of Persia game materials

Prince of Persia artwork, characters, game data, and other original game materials belong to their respective rights holders. PopRunner does not grant a license to those materials or permission to redistribute them.

This project is intended for personal, recreational, noncommercial use. Users must supply their own licensed copy of DOS Prince of Persia and use `src/PopRunner.ExtractAssets` to generate the required images locally. This is a project use policy, not a license grant for the game or any third-party materials.

Original game files and generated images are excluded from source control. `src/PopRunner.Core/Assets` is ignored and must be populated locally before building the applications. Do not commit, upload, or redistribute game files, generated game images, or application builds that embed those images. Core embeds the locally generated images in `PopRunner.Core.dll`; packaging the DLL or an application containing it also packages that artwork.

The extractor reads local `KID.DAT`, `SHADOW.DAT`, `PV.DAT`, and `PRINCE.DAT` files. No game executable or DAT file is included in the repository or required at runtime. [research/provenance.md](research/provenance.md) documents the resource names, data hashes, formats, and animation references.

## SDLPoP

[SDLPoP](https://github.com/NagyD/SDLPoP), copyright Dávid Nagy and contributors, is licensed under [GNU General Public License version 3 or later](https://github.com/NagyD/SDLPoP/blob/master/LICENSE). Its DOS-derived frame, sequence, timing, rendering, and data-format definitions serve as technical reference material for this project. No SDLPoP C source is compiled into the application.

## Other technical references

[Princed Resources](https://github.com/NagyD/PR) documents the game resource IDs and palette assignments. [PoPSpritesConverter](https://github.com/lvcabral/PoPSpritesConverter) provides reference information for palette handling and the red potion bubble color. Neither tool is required by, compiled into, or distributed with the application. Their respective repositories retain their copyright and license notices.
