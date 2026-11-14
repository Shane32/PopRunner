# DOS formats and animation reference

PopRunner is intended for personal, recreational, noncommercial use. Users must supply their own licensed copy of DOS Prince of Persia and generate the required images locally. Original game artwork and other game materials remain the property of their respective rights holders. This project use policy does not grant a license to those materials.

The repository excludes `src/PopRunner.Core/Assets` from source control. Do not commit, upload, or redistribute original game files, generated game images, or application builds that embed those images. The extractor requires the user's local game data; the repository does not provide it.

## Local asset generation

`src/PopRunner.ExtractAssets` is a .NET 10 console tool that reads the DOS DAT resource and image formats. Its two arguments are the game directory and the destination Assets directory. See the [README](../README.md) for the command. It supports the data files used by DOS versions 1.0, 1.1, 1.3, and 1.4 without relying on a directory's name to identify its contents.

The tool prepares all 318 PNGs in memory before creating output directories or writing files. A resource parsing error therefore leaves the destination unchanged. The runtime projects do not depend on the tool.

| Output images | Source | Palette resource |
| --- | --- | --- |
| Prince 401–619 (219 images) | `KID.DAT` | 400 |
| Prince 751–782 (32 images) | `SHADOW.DAT` | 750 |
| Princess 801–817 | `PV.DAT` | 800 |
| Princess 901, 903–930 | `PV.DAT` | 900 |
| Princess 951–962 | `PV.DAT` | 950 |
| Princess 981 | `PV.DAT` | 980 |
| Potion body 163 | `PRINCE.DAT` | 150 |
| Red bubbles 166–172 | `PRINCE.DAT` | Transparent index 0; index 1 is RGB(224, 0, 48) |

DAT directories have a six-byte header, a count followed by eight-byte resource entries, and a checksum byte before each resource. Resource bytes including the checksum sum to 255 modulo 256. Image headers contain height, width, depth, and compression flags. Supported packing is raw, RLE, and 1 KiB-history LZSS, with horizontal and vertical traversal. VGA components are multiplied by four; palette index zero is transparent. The one-bit blank SHADOW images retain their actual 1×1 or 7×1 dimensions. Output PNGs use eight-bit RGBA, with white RGB under transparent potion pixels and black RGB under transparent Prince and Princess pixels.

Known SHA-256 values embedded in the extractor:

| Data file | Versions | SHA-256 |
| --- | --- | --- |
| `KID.DAT` | 1.0, 1.1, 1.3, 1.4 | `2EAA798041C090A6D54CD0DCE4AE770E3DA400EF95540CEC7ED0FF9DB5C2AF73` |
| `SHADOW.DAT` | 1.0, 1.1, 1.3, 1.4 | `5B6DED9399D13C3A877FF9EA003CBABDB222BBC1AE4A0FAC81EB4913340BF9AE` |
| `PV.DAT` | 1.0, 1.1, 1.3, 1.4 | `418BE33393E8AE25B724670D74F9B1FD6F1FD2EEF38639E2F679701DDB8A4DF2` |
| `PRINCE.DAT` | 1.0, 1.1 | `28D8B3A8C48ED6B7B280B16BCB3C2E1AC446F2A210A508D85937FBEB2196BF48` |
| `PRINCE.DAT` | 1.3, 1.4 | `0EC31EF20C8253530728E40FE78531FEE94CB1265C6E6F685C06E6D116E595D8` |

`KID.DAT`, `SHADOW.DAT`, and `PV.DAT` share the same bytes across these supported data sets. Although the complete early and later `PRINCE.DAT` files differ, the nine selected resources (palette 150 and eight images) are identical. The PNG output is consequently identical across these versions. The hashes above identify known data, not an exact executable version. An unexpected hash produces a warning; valid resource structure and checksums are still required.

Core embeds the locally generated images and `animation.json` in `PopRunner.Core.dll` and loads them through resource streams. Changing the configuration or regenerating images requires rebuilding and restarting the application. JSON image identifiers retain logical paths such as `Assets/Potion/res163.png`.

## Animation metadata

The animation configuration follows DOS frame, sequence, timing, and rendering definitions documented by SDLPoP. The following DOS v1.4 reference identifies the frame-table offsets used by the configuration. To tune the animations, edit `src/PopRunner.Core/animation.json`, then rebuild and restart the app.

| Reference executable item | Value |
| --- | --- |
| Game version | Prince of Persia DOS 1.4 |
| Executable length | 110,855 bytes |
| SHA-256 | `9D180A6FD59A2418AEE65B5804852E4B08C09D869BB97D4F2364D6CABB46F5FE` |
| Complete Prince frame table file offset | `0x18B94` |
| Prince frame table | 241 records, 5 bytes each |
| Complete Princess/Vizier cutscene frame table | 86 records, 430 bytes, at file offset `0x19118` |
| Noncombat timer divider | `5` at file offset `0x4E45` |
| Combat timer divider | `6` at file offset `0x4E3D` |

Each frame record contains an image index, sword/table flags, signed horizontal and vertical offsets, and alignment/collision flags. The resource ID is the image index plus 401 for the Prince's KID image table. Pose numbers and resource IDs differ: pose 53 maps to `res465.png`, and pose 191 maps to `res593.png`. The configuration includes the Prince records needed by the demo, 14 Princess cutscene records, and references to eight potion images.

The potion routes use a shortened version of the standing `turn` sequence in `seqtbl.c`: flip, then movement 6/1/2/-1/1/-2/0 in the new direction while displaying poses 45–51. The first frame's configuration uses movement -6 in the old direction followed by `flip: true`, producing the same position before drawing pose 45. Poses 45–48 map to `res445.png` through `res448.png`, with zero X/Y anchors and the odd-pixel flag set. The final original turn pose 52 is omitted, and acceleration follows immediately.

The greeting follows `end_sequence_anim()` in `seg001.c`: the Prince starts at original X 198 and the Princess at 136 (demo origins 140 and 78 after subtracting the game's 58-unit room offset). She begins responding after the first eight processed cutscene frames; the initial waiting pose therefore lasts nine displayed simulation ticks. The Prince begins braking after 13 processed frames, and his separate sprite is hidden after two braking frames when the Princess's first combined reunion sprite appears. Princess poses 20–33 map to `res903.png` through `res916.png`; demo IDs add 1000 to avoid collision with Prince pose numbers. `Pembrace` supplies the movement sequence. Cutscenes use the default six-base-tick frame duration, or 10 Hz. The configuration supplies the final hold before cycling.

Running entrances begin fully offscreen in the running cycle. The traversal and sliding-reversal routes use roots -24 (rightward) and 164 (leftward), retaining their run phase and reversal positions after entry. The fifth route's greeting starts at root 152; its Princess wait lasts 10 ticks to preserve the contact handoff. These routes are demo choreography; the pose movements and clocks follow the DOS definitions.

The sixth route mirrors the greeting across the 140-unit room: the Prince starts at -12 and runs toward 36, and the Princess starts at 62. Both actors face right. It shares the existing waiting, reunion, and greeting clips, including the combined embrace frames and the 10 Hz clock. Horizontal image mirroring supplies the opposite-facing artwork without another asset set. Both greeting routes finish at the embrace.

Routes nine and ten use the original normal careful step, `seq_39_safe_step_11`: poses 121–132, one tick each, with pre-frame movement `0, 1, 1, 3, 4, 3, -1, 0, 0, 0, 0, 0`, for a net 11 DOS units. The images are `res534.png` through `res545.png`; their anchors are zero, with the odd-pixel flag on poses 122, 123, 126, and 128. A separate standing-pose-15 hold lasts 48 ticks at 12 Hz. The return is demo choreography that rewinds poses 132–121, using movement `0, 0, 0, 0, 0, 0, 1, -3, -4, -3, -1, -1`, then returns to standing. These deltas undo the next forward pose's entry movement because the engine applies movement before displaying a pose. The original game's `stepback` sequence instead shifts a standing sprite; it does not supply this pose rewind. See the [original careful-step bytecode](https://github.com/NagyD/SDLPoP/blob/master/src/seqtbl.c#L719-L729) and [frame records](https://github.com/NagyD/SDLPoP/blob/master/src/seg006.c#L240-L251).

The sample starts these finite routes just beyond its left or right edge. The tray translates a cloned route using the initial and final sprite bounds, leaving about one source pixel outside the incoming monitor edge. It preserves the single step's distance and four-second hold rather than extending it into a screen traversal. Both directions retain their facing during retreat.

Potion and greeting scenes add a configurable introductory hold before the actor scripts begin: `startDelayTicks` is 24 for 12 Hz potion scenes and 20 for 10 Hz greetings. This is demo presentation, separate from the original per-pose timing. The potion's bubble clock advances throughout the hold; supporting actors retain their initial pose until the Prince's script starts, keeping the later greeting handoff synchronized. Zero or an omitted setting preserves immediate script entry.

Routes seven and eight retain all eight stopping poses 53/54/55/56/49/50/51/52 and the nine-unit braking displacement. Running pose 11 at root 57 (leftward) or 83 (rightward) therefore brakes to 48 or 92 before drinking pose 191. Starts 173 and -33 preserve the incoming run phase. The four-unit drink entry adjustment places the actor at 44 beside potion 46, or at mirrored root 96 beside potion 94. One standing tick precedes the 21-tick drink script. One recovery tick is followed by turn poses 45–51; turning pose 52 is omitted, and acceleration begins without a separate standing hold. The routes are world-coordinate mirrors; integer truncation and pixel-parity rules can differ by one rendered pixel between mirrored placements. Poses 51/52 have zero scripted root displacement, but their artwork and sprite anchors change as the Prince settles upright.

The large-potion layers retain their relative offsets: bubbles begin three native pixels to the vessel's right, with their bottom 15 pixels above its bottom. In `seg008.c`, the tile drawing baseline is seven pixels below the Prince's standing baseline, and the vessel's inclusive bottom is three pixels above that tile baseline. The vessel therefore extends four pixels below the Prince's baseline. The configured offsets are `bodyOffsetY: 4` and `bubbleOffsetY: -11`, measured from the exclusive bottom at `floorY + 1`. The supplied JSON enables the optional `alignWithFeet` presentation override, which shifts both layers upward by the bottle's Y offset. Disabling it restores those original offsets. The bubble phase is fixed for reproducibility. Bottle and bubbles share the pickup visibility event.

## Reference sources

- [SDLPoP seg006.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg006.c): frame metadata, direction-relative movement, sequence interpretation.
- [SDLPoP seqtbl.c](https://github.com/NagyD/SDLPoP/blob/master/src/seqtbl.c): startup, running, reversal, braking, careful stepping, turning, and drinking scripts.
- [SDLPoP seg003.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg003.c): base/fighting timer selection.
- [SDLPoP types.h](https://github.com/NagyD/SDLPoP/blob/master/src/types.h): 60 Hz base timer and record layouts.
- [SDLPoP seg008.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg008.c): frame offsets, parity correction, 280-to-320 coordinate conversion, bottom alignment, mirroring, and potion placement.
- [SDLPoP seg005.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg005.c): running-stop input on pose 7 or 11 and item pickup after stooping.
- [SDLPoP seg001.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg001.c): ending actor positions, greeting handoff, and cutscene timing.
- [SDLPoP seg007.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg007.c): seven-frame potion bubble cycle.
- [SDLPoP seg009.c](https://github.com/NagyD/SDLPoP/blob/master/src/seg009.c): DAT resource records, image packing, palettes, and transparency.
- [Princed Resources resource definitions](https://github.com/NagyD/PR/blob/master/src/xml/resources.xml): DAT resource IDs and palette assignments.
- [PoPSpritesConverter](https://github.com/lvcabral/PoPSpritesConverter): palette handling and red bubble RGB(224, 0, 48).

SDLPoP is a port/conversion of the DOS game, copyright Dávid Nagy and contributors, licensed under GPL version 3 or later. Its source is reference material for the DOS definitions described here. No SDLPoP C source is compiled into the application. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for attribution and the distinction between reference code and copyrighted game assets.
