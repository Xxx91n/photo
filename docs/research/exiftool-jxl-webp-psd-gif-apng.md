# ExifTool 13.61 对 JXL / WebP / PSD / GIF / APNG 的支持与擦除风险评估

资料核对日期：2026-11-09

实测环境：Windows + Perl 5.38，ExifTool 13.61（源码取自 tag `13.61`）。样本来自 13.61 自带测试图 `t/images/{JXL.jxl,JXL2.jxl,RIFF.webp,Photoshop.psd,GIF.gif}`、gstatic 的动图 WebP，以及我按规范手工构造的 2 帧 APNG 和 2 帧 GIF。文中具体字节数都是这些样本的值，换样本会变。本文结论以 13.61 的源码和行为为准；`exiftool.org` 当前线上文档正好也是 13.61，两边能对上（唯一对不上的一处见 3.3）。

## 1. 一句话结论

五种格式里，**WebP 最值得扩，而且风险最低**；**APNG 命令结构没事，不用改，但 ICC 保不保要单独定**；**GIF 能扩，但只能算"部分清理"**（有些东西它真删不掉）；**PSD 只能有限支持**；**JXL 建议继续跳过**，因为它的 `jbrd` 里可能还藏着原始 JPEG 的元数据，删不干净。

| 格式 | 13.61 读写 | 建议 | 最大风险 |
|---|---|---|---|
| WebP | R/W；EXIF / XMP / ICC 能写能删，C2PA 只能读/删（R/D） | **扩进支持列表** | 默认删 ICC 会偏色；扩展格式(simple/extended)会被自动转换 |
| APNG | R/W，走 PNG 引擎，`FileType` 单独报 APNG | **命令结构保持现状；ICC 是否保单独定** | 默认连 `iCCP` 一起删，广色域图会偏色 |
| GIF | R/W，Comment / XMP / ICC 能写能删 | **可扩，但只能部分清理** | C2PA、Plain Text 扩展、MIDI 块删不掉，且是静默不动 |
| PSD | R/W，但只重写 image resource block | **有限支持，或先不上** | IRB 里的版权/URL/分辨率/打印参数删不掉；缩略图是 Protected |
| JXL | R/W，**只能删 EXIF / XMP** | **继续跳过** | `jbrd` 里可能有原 JPEG 的 APP13/COM；ICC 删不掉；裸码流写入要重新封装成容器 |

## 2. 我们现在的擦法

现在的擦除是"格式族 → 参数串"：

- 扩展名映射：`src/PhotoPrivacy.Core/ExifTool/WipeStrategies/WipeStrategyResolver.cs:38`（`.apng` 和 `.png` 都映射到 `Png` 族）；允许清单 `src/PhotoPrivacy.Core/Configuration/AppConfig.cs:82`（`".png", ".apng"`）。
- 安全默认：`src/PhotoPrivacy.Core/Rules/WipeRuleEngine.cs:47` —— Png 族是 `(stripAll: true, preserveIcc: false, ...)`，也就是**直接 `-all=`，不保 ICC**。
- 命令拼装：`WipeRuleEngine.cs:114-121`（JPEG 族才是 `-all= --icc_profile:all -tagsfromfile @ -colorspacetags`），PNG 族落到 `default: parts.Add("-all=")`（`WipeRuleEngine.cs:164-165`）。
- 声称：`README.md:29-31` 写了 34 个扩展名、"PNG/APNG"，以及 `.webp`/`.gif`/`.psd` 明确跳过并记 `wipe_skipped_unknown`。
- 版本：`scripts/publish.sh:8` `EXIFTOOL_VERSION="13.61"`。

**结论：`.apng` 现在就是被当成 PNG，用裸 `-all=` 在擦。结构上是安全的**，理由见 3.2（我用真实 2 帧 APNG 验过，动画块一个字节没动）；ICC 会不会被删是另一回事，见 3.2 末尾。

`-all=` 的官方定义是"删掉所有可删的元数据"，并且明确说了"不是所有组都能删"、色彩信息删了颜色可能变（见 [exiftool_pod](https://exiftool.org/exiftool_pod.html) 的 `-TAG[+-^]=[VALUE]` 段）。这也解释了为什么我们 JPEG/HEIC 要 `--icc_profile:all` 把 ICC 捞回来。

## 3. 逐格式

### 3.1 WebP —— 建议扩，风险最低

**能读能写什么。** 13.61 里 WebP 归 `RIFF` 模块，`WEBP` 在可写类型表里，而且 `%onlyWriteFile = ( RIFF => [ qw(WEBP) ] )` —— RIFF 家族只有 WebP 能写，别的（AVI/WAV）会直接报 `Can't currently write RIFF XXX files`（[ExifTool.pm:208-222](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool.pm)、[WriteRIFF.pl](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteRIFF.pl)）。官方支持表给的是 EXIF R/W/C、XMP R/W/C、ICC R/W/C、C2PA R/D（[exiftool.org 支持表](https://exiftool.org/)）。读的方面还有 VP8/VP8L/VP8X/ANIM/ANMF/ALPH（[TagNames/RIFF](https://exiftool.org/TagNames/RIFF.html)）。

**`-all=` 删什么、留什么。** 元数据层面只动 EXIF / `XMP ` / `ICCP` / C2PA 四个 chunk（结构上还可能在最后去掉不再需要的 `VP8X`，见下）。删的时候整个 chunk 拿掉，重算 RIFF 长度（两趟写：先算大小再写头），并同步 VP8X 标志位：源码是 `$flags &= ~0x2c` 清掉 XMP(0x04)/EXIF(0x08)/ICCP(0x20) 三位，再按实际剩下的置位（[WriteRIFF.pl](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteRIFF.pl)）。`ANIM`/`ANMF`/`ALPH`/`VP8`/`VP8L` 和所有不认识的 chunk 原样按字节拷贝。

**实测（我这台机器真跑的）。**

- 静态样本 586 字节 → `-all=` 后 82 字节，`VP8X flags 0x1c → 0x10`（XMP/EXIF 位清掉，Alpha 位保留），`ALPH`/`VP8` 原样。
- 简单格式（只有 `VP8 `）加一条 XMP 变成 extended（`VP8X flags=0x04`），再 `-all=` 回去，**文件 md5 与原始完全相同**（30320 字节）。
- 动图（gstatic 同一份 100 帧样本，380850 字节）：加 XMP/EXIF 后 `flags 0x12 → 0x1e`，`-all=` 后 `flags` 回到 `0x12`，100 个 `ANMF` 拼起来的 md5 前后一致，文件大小回到 380850。

**结构风险。** 低。RIFF 头长度重算过；`ICCP` 会被写在 `VP8X` 后面（规范要求 ICCP 必须在图像数据之前）；EXIF/XMP 允许乱序（[WebP 容器规范](https://developers.google.com/speed/webp/docs/riff_container)：重建相关的 chunk 必须按 `VP8X/ICCP/ANIM/ANMF/ALPH/VP8`/`VP8L` 顺序，元数据 chunk 可以乱序）。唯一的"结构变化"是：元数据删光后如果 `VP8X` 不再需要（没动画、没 alpha、没 ICC），ExifTool 会把 `VP8X` 也删掉，把扩展格式退化成简单格式——这是合法的，字节也会回到原样（上面第三组实测就是）。

**格式特有的坑。**

- `FileType` 会报 **`Extended WEBP`** 而不是 `WEBP`。别拿这个字符串做分支判断（我们的探测用的是 `-json` 全量，不依赖它，暂时没事，但审计日志里会出现这个值）。
- Google 自家软件写坏过的 `XMP\0` chunk，ExifTool 认作"写错的 XMP"，删 XMP 组时会一起删（源码里 `%deletableGroup`）。
- 能写的是 EXIF/XMP/ICC；C2PA 官方标的是 R/D（能读能删，不能写）。想删别的 RIFF chunk（比如某些私有块）`-all=` 不会碰——但这些一般不是元数据。

**建议：扩。** 复用一个和 PNG 一样的 `-all=` 就够，动画和无损数据实测安全。要注意 C2PA 是 R/D（只能删不能写）；要不要保 ICC 需要产品拍板（见第 4 节）。

### 3.2 APNG —— 结构安全，命令别动；ICC 策略另说

**ExifTool 怎么认。** APNG 走 PNG 引擎：读 `acTL` 时会把 `FileType` 覆盖成 `APNG`（`PNG.pm` 里 `AnimationFrames` 的 `RawConv`：`$self->OverrideFileType("APNG", undef, "PNG")`），`-listf` / `-listwf` 里 APNG 也是单独列出的类型。但它没有单独的表，`fcTL`/`fdAT` 干脆不在 tag 表里——源码注释就一行：`# fcTL - animation frame control for each frame / # fdAT - animation data for each frame`（[PNG.pm](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/PNG.pm)）。

**`-all=` 删什么、留什么。** 删的是 PNG 的可写块：`tEXt`/`zTXt`/`iTXt`（含 XMP）、`eXIf`、`iCCP`、JUMBF 等。`acTL`/`fcTL`/`fdAT` 原样逐字节拷贝，CRC 不动。注意 `%isDatChunk` 只有 `IDAT/JDAT/JDAA`，**`fdAT` 不算图像数据块**，所以 ExifTool 不会在动画帧之间搬东西。

**实测。** 我按规范手搓了一个 1x1、两帧的 APNG（`acTL` + `fcTL` + `IDAT` + `fcTL` + `fdAT`，外加两个 `tEXt`，其中一个故意放在 IDAT 之后）：

- `-all=` 前：`IHDR, acTL, fcTL, tEXt, IDAT, fcTL, fdAT, tEXt, IEND`，259 字节。
- `-all=` 后：`IHDR, acTL, fcTL, IDAT, fcTL, fdAT, IEND`，195 字节，**所有 CRC 校验通过**，块顺序保持 `acTL → fcTL → IDAT → fcTL → fdAT`。
- 再加 ICC + XMP 再擦一遍：`iCCP`（273 字节）和 `iTXt`（435 字节）全没了，动画块还是一样。

样本不同，字节数会变，但块序列和 CRC 结论一样。

**结构风险。** 低，但有两个点要知道：

1. ExifTool 11.63 起会把 IDAT 之后的文本/EXIF 块**搬到 IDAT 之前**（[TagNames/PNG](https://exiftool.org/TagNames/PNG.html) 明写了这个行为，源码里是 `@txtOffset` 那套 leapfrog 逻辑）。APNG 规范（现在是 [W3C PNG 3rd Edition 第 4.9 节](https://www.w3.org/TR/png-3/)）说得很清楚：`acTL` 必须在任何 IDAT 之前，`fcTL`/`fdAT` 靠序号而不是位置来定顺序，"本规范不对辅助块施加顺序限制"。所以把文本块搬到 IDAT 前是合规的，序号没动，帧不会错。
2. **默认连 `iCCP` 一起删**（我们 Png 族就是 `preserve_icc: false`），ExifTool 会给 `ICC_Profile deleted. Image colors may be affected` 的警告。动画没事，颜色可能变。

**建议：命令结构保持现状，ICC 策略单独定。** 现役的 `-all=` 对 APNG 结构无害；但 Png 族默认 `preserve_icc=false`，带 iCCP 的广色域 APNG 会偏色。要改只改这个策略问题，不要动命令结构。

### 3.3 GIF —— 能扩，但只能算"部分清理"

**先回答"GIF 是不是只读"：不是。** 13.61 里 GIF 在可写类型表里（`@writeTypes` 有 `GIF`），`-listwf` 也列了 GIF。可写的是三样：`Comment`（0x21 0xFE 注释扩展）、`XMP Data/XMP`（XMP 应用扩展）、`ICCRGBG1/012`（ICC 应用扩展），见 [TagNames/GIF](https://exiftool.org/TagNames/GIF.html) 和 `GIF.pm` 的 `@appExtensions`。

**`-all=` 删什么、留什么。** 上面三样全删（注释整块拿掉；添加注释时按 255 字节一块写回，ICC/XMP 整块删）。**删不掉的**：`NETSCAPE2.0` 循环块、Plain Text 扩展（`Text` tag，只读）、`MIDICTRL`/`MIDISONG`、以及 **C2PA JUMBF**（`C2PA_GIF/` 扩展在表里没有 `Writable` 标记，写回时走 `$justCopy = 1`，整块原样拷）。

**实测（这条值得注意）。**

- 我用 1x1、2 帧的 GIF 加 Comment + XMP + ICC，再 `-all=`：文件 md5 与原始**完全一致**（66 字节），`FrameCount` 还是 2、`Duration` 还是 0.20s。帧数据没动。
- 我手工塞了一个 `C2PA_GIF/` 应用扩展，跑 `-jumbf:all=`：输出是 **`0 image files updated / 1 files unchanged`，文件大小没变**。也就是**静默不删**，不报错。

**和官方文档对不上的一处：** 官网支持表把 GIF 的 C2PA 列成 `R/D`（可读可删），但 13.61 源码里这个扩展没有 `Writable`，`Jpeg2000.pm` 的 Notes 列举"可删 JUMBF 的格式"时也没提 GIF，我的实测也删不掉。以源码和实测为准：**GIF 的 C2PA 删不掉**。这条建议报给 upstream。

**结构风险。** 低。GIF 写入是把整个文件重写一遍，图像描述符、GCE、LZW 数据块逐字节拷贝。两个小坑：GIF87a 在**加 Comment/XMP/ICC 时**会被写成 GIF89a（只删不改版本；实测无元数据的 GIF87a 跑 `-all=` 是 0 updated）；注释超过 255 字节会被拆成多个子块（合法的）。

**建议：可以扩，但描述要诚实。** 只能承诺"删注释/XMP/ICC"，不能承诺"删干净"。如果产品上不允许"部分清理还标成已清理"，那就继续跳过。

### 3.4 PSD —— 只能有限支持

**能读能写什么。** PSD/PSB/PSDT 在可写类型表里。但写入范围很窄：`ProcessPSD` 只重写 image resource block（IRB），头、color mode data、图层/蒙版段、图像数据都原样拷贝；trailer 会单独识别处理，`-all=` 可能把它删掉（[Photoshop.pm](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/Photoshop.pm) 的 `ProcessPSD`）。官方支持表给 PSD 的列是 EXIF/IPTC/XMP/ICC 都 R/W/C，"Other" 那列只写 `R Photoshop` —— 也就是 Photoshop 自己那堆 IRB 标量标签**只读**。

**`-all=` 删什么、留什么（实测 IRB 清单）。**

删掉：`0x0404` IPTC、`0x0422` EXIF、`0x0424` XMP、`0x040F` ICC（资源 ID 定义见 [Adobe PSD 文件格式规范](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/)）。

留下：`0x03ED` ResolutionInfo（分辨率/Dpi）、打印相关（`0x03F3` PrintFlags 等）、`0x040A` CopyrightFlag、`0x040B` URL、`0x041A` Slices、`0x0421` VersionInfo、`0x0425` IPTCDigest，以及那个 7.6KB 的 `0x03EA` 块。**这不是 bug，是 `%psdMap` 里没有给 Photoshop 组留写入目标**，你甚至显式跑 `-Photoshop:all=` 也会得到 `0 image files updated / unchanged`。

另外 `0x040C` 缩略图是 `Protected => 1`（和 MakerNotes 一个待遇），`-all=` **不会删**；缩略图本身还可能带着它自己的 EXIF/缩略图链，这部分也算没清掉。

**结构风险。** 低。我比对了 image data 段：`-all=` 前后前 98 字节（8x8 RGB RLE）md5 完全相同；末尾那 75 字节的 PhotoMechanic IPTC trailer 被删了（这是好事）。这个样本图层数是 0，所以"图层保留"只到源码级证据（`ProcessPSD` 把 IRB 之后的剩余字节整段原样拷贝），**带图层的 PSD 没实测**。

**隐私面要注意：** 图层本身、图层名、文字图层、智能对象、蒙版都原样留着。它们不算传统元数据，但可能带着不想公开的内容；这是"PSD 有限支持"的另一个理由。

**格式特有的坑。**

- 用户以为"PSD 已清理"，其实版权标记、URL、分辨率、打印参数还在。这些算不算隐私见仁见智，但"全部清除"这句话肯定不能写。
- PSD 里 EXIF 的位置是嵌套的（EXIFInfo→IFD0），`WritePhotoshop.pl` 里有一段注释说单独改 EXIF 在 PSD 里走的是特殊分支（我实测单独 `-EXIF:all=` 是能删的，但这条路径比较绕，值得进测试）。
- 大文件（PSB）没测。

**建议：有限支持，或者先不上。** 如果要上，UI 必须写清楚"只清 EXIF/XMP/IPTC/ICC"，并且 `-all=` 后要真的验证这一条，别让审计日志说"已清除全部元数据"。

### 3.4.1 真实带图层样本实测（用户提供，2026-11-09）

补上了 3.4 节缺的那块：真带图层的 PSD。样本是用户桌面的 `集装箱彩平图-新.psd`，**31,638,173 字节（换样本会变）**，Adobe Photoshop 21.0 存的 RGB/8bit/RLE，3508×2480，`LayerCount=42`（图层名中英混杂：底、墙体、沙发2、线稿……从 `LayerUnicodeNames` 读出来），带 `0x040C` 缩略图（4496 字节）、IPTC、EXIF（IFD0/ExifIFD/IFD1）、XMP、ICC（sRGB IEC61966-2.1）。隐私面上这文件挺典型：IPTC `ObjectName` 和 XMP `dc:title` 都存着本机路径（`C:\Users\Administrator\Pictures\job\...`），XMP 里有 xmpMM 历史（HistoryWhen/HistorySoftwareAgent）和 DocumentAncestors GUID。这个样本**没有 trailer，也没有 `0x040A`/`0x040B`**（只有一个空的 `0x041E` URL_List），所以源码结论里"trailer 会删、版权/URL 留着"这两条还是没有实测对象。

**候选命令（若 PSD 进名单就按这个）**：`exiftool -All= -overwrite_original`（ExifTool 13.61）。实测 `1 image files updated`，唯一警告是预期内的 `ICC_Profile deleted. Image colors may be affected`。文件 31,638,173 → 31,619,481，**少的 18,692 字节正好等于 IRB 段缩小的量（25,674 → 6,982）**。

**删掉的（对照预期）**：`0x0404` IPTC、`0x0422` EXIF（IFD0+ExifIFD+IFD1 整个嵌套段连 IFD1 缩略图头一起）、`0x0424` XMP、`0x040F` ICC；`-v3` 里这四个 ID 擦后都不再出现，`CurrentIPTCDigest`/`ExifByteOrder` 等派生标签跟着消失。本机路径、创建/修改时间、软件名（Adobe Photoshop 21.0 (Windows)）都没了。

**留下的（对照预期）**：其余 IRB 资源全部原样——`0x03ED` 分辨率（300dpi）、`0x0400/0x0402` 打印参数、`0x0408` 网格参考线、`0x040C` 缩略图（擦前后 sha256 同为 `d70e0d09…`，4496 字节一字节没动，`Protected` 名不虚传）、`0x040D` GlobalAngle、`0x041A` Slices（`SlicesGroupName` 还是文件名「集装箱彩平图-新」）、`0x041E` URL_List、`0x0421` VersionInfo（ReaderName: Adobe Photoshop 2020）、`0x0425` IPTCDigest（还是旧值 `6bf3cce1…`，IPTC 已删，它成了指向空气的死值）、`0x0426/0x0428/0x042D/0x0430/0x043A/0x043B`。图层级的隐私尾巴也全在：`LayerNames`/`LayerUnicodeNames`（图层名）、`LayerModifyDates`（2025:06:18）都没动。

**图层完好性（字节级）**：手工解析 PSD 段边界后对 md5——图层与蒙版段擦除前后长度同为 20,493,916 字节、md5 同为 `21a280cf…`；合成图像数据段 md5 同为 `10550759…`（11,118,545 字节）。3.4 节"图层/蒙版段整段原样拷贝"从源码推断升级成了实测。读回 `exiftool -G1 -a -s -u` 无报错无警告；`LayerCount` 和全部 `Layer*` 标签擦除前后逐字节一致；`identify`（ImageMagick）43 帧（合成+42 图层）几何逐帧一致。

**按宽线的初步结论：够格进名单。** 结构不坏（图层段/图像数据字节级不变），EXIF/XMP/IPTC/ICC 全删，IPTC/XMP 里的本机路径、时间、软件名这类主要隐私信息都清掉了。但按 3.4 节说好的，文案必须写「部分清理」：缩略图 `0x040C`、图层名、`LayerModifyDates`、打印参数、IPTCDigest 死值还留着；trailer 和 `0x040A/0x040B` 这样本没带，仍停在源码级。

### 3.5 JXL —— 建议继续跳过

**能读能写什么。** JXL 归 `Jpeg2000` 模块，`JXL` 在可写类型表里。官方支持表给 JXL 的列是：EXIF R/W/C、XMP R/W/C、IPTC `-`、ICC `-`、C2PA `-`（[exiftool.org 支持表](https://exiftool.org/)）。源码的 `%jxlMap` 也只有 `Exif`（→ IFD0）和 `xml `（→ XMP）两个写入目标（[Jpeg2000.pm](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/Jpeg2000.pm)）。Brotli 压缩的 `brob` 盒能读能写，但读要 `IO::Uncompress::Brotli`、写要 `IO::Compress::Brotli`；没装会警告，不过整盒删除仍然能做（实测没有 Brotli 模块也能把 `brob` 删掉）。另外**默认新建的是不压缩盒**；要新建 `brob` 得开 API 的 `Compress` 选项，命令行通过 `-api Compress=1` 传（[Jpeg2000.pm 的 Notes](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/Jpeg2000.pm)）。

**`-all=` 删什么、留什么。** 删 `Exif` 盒、`xml ` 盒、`brob`（里面装 Exif/xml 的那些）。留 `jxlc`/`jxlp`（图像数据）、`jbrd`、`colr`（ICC）、`jumb`（C2PA）、以及所有不认识的盒——不认识的一律原样拷贝。

**实测。**

- 裸码流（只有 `\xff\x0a` 开头，没有容器）：**`-all=` 直接报错** `Error: [minor] Will wrap JXL codestream in ISO BMFF container for writing`，`0 image files updated`，退出码 1。有东西要写时，加 `-ignoreMinorErrors` 才会写，但那样会把整个文件**重新封装成 ISO BMFF 容器**（Jpeg2000.pm 的 `ProcessJXL` 里就是这么干的）。
- 容器 JXL：加 EXIF+XMP 后 `-all=`，`Exif` 和 `xml ` 盒被删，`jxlp` 保留，66 字节恢复原状。
- 我手工往容器里插了一个 `jbrd` 盒再 `-all=`：**`jbrd` 盒和内容都留着**（位置可能被挪到 `jxlp` 后面）。

**`jbrd` 这条是关键。** `jbrd`（JPEG Bitstream Reconstruction Data）不在 ExifTool 的 tag 表里——[TagNames/Jpeg2000](https://exiftool.org/TagNames/Jpeg2000.html) 根本没有 jbrd 这一行，源码里它只出现在 `%isImageData` 里（算 ImageDataHash 用）。libjxl 的官方说明是：`jbrd` 存的是"重建出与原文件比特一致的那份 JPEG 所需的信息"，重建时还会用到 Exif/XMP/JUMBF 这些盒（[libjxl format_overview.md](https://raw.githubusercontent.com/libjxl/libjxl/main/doc/format_overview.md)）。光看文档还不够，我翻了 libjxl 源码：`EncodeJPEGData()` 会把 ICC/Exif/XMP 三类 APP 标记挑出来（`DetectIccProfile`/`DetectBlobs`），其余**所有不认识的 APPn 标记、COM 注释、扫描间的数据、文件尾数据**都 Brotli 压进同一个 blob，而这个 blob 就是 `jbrd` 盒的内容（[enc_jpeg_data.cc](https://raw.githubusercontent.com/libjxl/libjxl/main/lib/jxl/jpeg/enc_jpeg_data.cc)、[encode.cc 里 `AppendBoxWithContents("jbrd", ...)`](https://raw.githubusercontent.com/libjxl/libjxl/main/lib/jxl/encode.cc)）。

翻译成大白话：**一张 JPEG 无损转成 JXL 之后，原 JPEG 的 APP13（Photoshop/IPTC）、APP0(JFIF)、APP14(Adobe)、COM 注释，大概率还躺在 `jbrd` 里。ExifTool 看不懂也删不掉。** 这条是源码级结论，我没找到官方文档明说，**已于 2026-11-10 实测证实（见 3.5.1）**。

**ICC 也删不掉。** JXL 的 ICC 要么在码流头里（libjxl 文档说"DCT 系数之外还可能带 ICC profile 或 Exif 方向"），要么在容器的 `colr` 盒里；官方支持表 JXL 的 ICC 列是 `-`，`%jxlMap` 里也没有 ICC，实测 `-colr:all=` 直接报 `Not a deletable group`。

**结构风险。** 除了"裸码流会被重新封装"这一条，盒级改写本身是安全的（不认识的盒原样拷）。

**建议：继续跳过（fail-closed）。** 理由不是"不能写"，而是"写了也达不到我们承诺的清理效果"：`jbrd`、ICC、C2PA 都留着。如果将来一定要支持，最低限度是：只处理容器格式、明确提示"ICC/C2PA/JPEG 重建数据未清理"，而且绝不能靠 `-ignoreMinorErrors` 去偷偷重封装用户的文件。

### 3.5.1 jbrd 实测（2026-11-10）

**环境与样本。** WSL（Ubuntu 24.04）里 ExifTool 13.61 源码直跑（perl 5.38.2），cjxl/djxl 用 apt `libjxl-tools` 的 **v0.7.0**。样本取 `t/images/Writer.jpg`（8×8、无 EXIF），注入 `-IPTC:Keywords=privacy-test -Comment=privacy-comment -XMP-dc:Description=privacy-xmp`。

**cjxl 默认参数就出容器。** 日志明说 `Container | JPEG, lossless transcode, effort: 7 | JPEG reconstruction data`。盒序列：`ftyp` / `jxlp`×2（码流分两段）/ `jbrd`（133 字节，jxlinfo 报 "JPEG bitstream reconstruction data available"）/ `brob`。两个版本细节：cjxl 0.7 默认把 XMP **Brotli 压进 `brob`**，没有明文 `xml ` 盒；djxl 0.7 **没有** `--reconstruct_jpeg` 旗标——只要容器里有 `jbrd` 且输出 .jpg，比特级还原就是默认行为，反义开关是 `-j/--pixels_to_jpeg`（后来的版本才把强制还原改叫 `--reconstruct_jpeg`）。

**ExifTool 读不到，但隐私就在 jbrd 里。** 转换后 `exiftool -G1 -a -s` 只剩容器级 tag，IPTC/Comment/XMP 全部不可见；`-v3` 的 `jbrd` 十六进制里能直接看到 `4Photoshop 3.0`、`8BIM`（APP13 的头）。

**对照组：不擦、直接还原——APP13/COM 实测抠得回来。** `djxl orig.jxl out.jpg` 报 "Reconstructed to JPEG"，`out.jpg` 与注入后的原 JPEG **`cmp` 逐字节一致**（`CurrentIPTCDigest` 相同），IPTC Keywords、Comment、XMP Description 全在。上面的源码推断实测证实。

**只写 IPTC+COM 的样本：`-all=` 擦了个寂寞。** 这种容器里没有 Exif/`xml `/`brob` 可删，`-all=` 报 `0 image files updated / 1 image files unchanged`，文件一个字节没动，djxl 还原照样逐字节一致——隐私全数生还。根因就是 `jbrd` 不在 ExifTool 的删除清单里。

**带 XMP 的样本：`-all=` 真删了盒，还原却失败，隐私仍在。** `-all=` 删掉 `brob`（708→421 字节，恰好一个 brob 盒的大小），`jbrd` 保留（-v3 里 tag 尺寸不变）。`djxl 擦过的.jxl out.jpg` 报 `Input file is truncated ... could not decode losslessly to JPEG. Retrying with --pixels_to_jpeg`，退化成默认 q95 的有损重编码，`out.jpg` 里 IPTC/Comment/XMP 一个都没有。原因：比特级还原要拿 Exif/XMP 盒拼回原 JPEG 的对应 APP 段（libjxl 文档原话），`brob` 被删就凑不齐了；删 Exif 盒同理（推断，未单测）。**但这不等于清干净**：`strings -a` 直接命中 `privacy-test`、`privacy-comment`（jbrd 对这种小 blob 基本原样存储——推断是 Brotli 的原样存储块；真要抠，Brotli 解压 `jbrd` 即可）。

**意外发现：`-all=` 会把可还原性弄坏。** 擦过的 JXL 连 djxl 都无法再无损还原成原 JPEG——cjxl 承诺的"无损 JPEG 转码"被 ExifTool 顺手打破。这不是清理，是损坏：用户想从"清理后"的 JXL 换回 JPEG，只能接受有损。

**ICC/C2PA。** 本样本无 ICC/C2PA，容器里没有 `colr`/`jumb` 盒；`-all=` 设计上也不删这两类（见上），本次未新增实测。

**结论：维持跳过，理由加码。** 宽线验收（主要隐私信息删得掉才算能清）不过关：`jbrd` 里的 APP13/COM 删不掉、抠得回来（逐字节级），连 `strings` 都能读出关键词；`-all=` 唯一做到的是让 djxl 的正规还原路径断掉——隐私没少，可还原性没了。

复现（样本在 /mnt/c/tmp/ppv/work/samples-jxl/）：

```bash
ET=/mnt/c/tmp/ppv/work/exiftool-13.61/exiftool
$ET -overwrite_original -IPTC:Keywords=privacy-test -Comment=privacy-comment \
    -XMP-dc:Description=privacy-xmp privacy.jpg
cjxl privacy.jpg privacy.jxl          # 默认即容器 + jbrd
$ET -All= -overwrite_original privacy.jxl
djxl privacy.jxl out.jpg              # 0.7.0 默认比特级还原；缺盒则退化有损
$ET -G1 -a -s out.jpg; cmp privacy.jpg out.jpg
strings -a privacy.jxl                # 擦完仍见 privacy-test / privacy-comment
```

## 4. 横向风险清单

| 风险项 | 现状 | 说明 |
|---|---|---|
| **ICC 偏色** | PNG/APNG 现役默认全删；GIF/PSD/WebP 若按 PNG 族复用也会全删；JPEG/HEIC 保留 | `-all=` 删 ICC 时 ExifTool 会警告 `ICC_Profile deleted. Image colors may be affected`。FAQ #32 的 JPEG 示例就是 `-all= --icc_profile:all -tagsfromfile @ -colorspacetags`，明确要留 ICC（[FAQ 32](https://exiftool.org/faq.html#Q32)）。我们 Png 族默认 `preserve_icc=false`，跟 JPEG 族不一致，值得单独拍板 |
| **动画** | WebP / GIF / APNG 实测安全 | 帧块逐字节拷贝；WebP 的 `ANMF` md5、GIF 整文件 md5、APNG 块序列都对得上 |
| **PSD 图层** | 源码级安全，未实测 | 只重写 IRB，图层/蒙版段整段原样拷贝；建议拿真带图层的 PSD 补测 |
| **嵌入缩略图** | PSD 缩略图删不掉 | `0x040C` 是 `Protected`；JPEG 的 IFD1 缩略图 `-all=` 会删 |
| **压缩盒 / 重建数据** | JXL `jbrd` 删不掉 | 可能含原 JPEG 的 APPn/COM；这是 JXL 的核心风险 |
| **未知 chunk/盒** | 一律原样保留 | 这是好事（不破坏结构），也是坏事（未知元数据清不掉） |
| **只读格式/块** | GIF 的 C2PA/Text/MIDI、JXL 的 C2PA/ICC、PSD 的 IRB 标量 | 需要按格式写清楚"删不掉什么" |
| **重写体积** | WebP/GIF 往返可回到原字节；PSD 因删 IRB 变小；JXL 删盒变小 | 写元数据时 ExifTool 可能补 padding，文件会变大；删元数据不会 |
| **静默无操作** | GIF 的 `-jumbf:all=` 返回 0/unchanged | 不报错也不删，审计日志最容易骗人。凡是我们不打算真删的组，别在命令里塞进去装作删了 |

## 5. 落地建议

**能加的（按优先级）**

1. **WebP**：`-all=` 就够，和 Png 族命令完全一样。加之前先定两件事：(a) 归到 `Png` 族还是新开 `Webp` 族（`rules.json` 六键、UI 规则行、`FormatCoverageReconciliationTests` 的数字都要跟着动）；(b) 保不保 ICC。
2. **APNG**：命令结构不动；ICC 是否保留按 3.2 节单独定。
3. **GIF**：可加，但审计事件/UI 文案要写"部分清理"，并且**别把 `-jumbf:all=` 塞进命令假装删 C2PA**。

**先别加的**

4. **PSD**：等有带图层的真实样本、且产品能接受"删不掉版权/URL/分辨率"这个前提再说。
5. **JXL**：继续跳过。等 `jbrd` 的实测结论出来（`cjxl` 转一张带 APP13/COM 的 JPEG，删完 EXIF/XMP 后 `djxl --reconstruct_jpeg` 看能不能把 IPTC/注释抠回来），再决定。

**加之前必须补的测试（把这次的手工验证固化成 CI，全部用 13.61 自带 `t/images` 或构造样本）**

- WebP 往返：简单格式 + 动图各一份，加 XMP/EXIF 再 `-all=`，断言**文件 md5 与原始相同**、`VP8X` 标志位正确、`ANMF` 拼起来的 md5 不变。
- APNG 块级：构造 `acTL/fcTL/IDAT/fcTL/fdAT`，断言擦完后的 chunk 序列、CRC、`AnimationFrames` 不变。
- GIF：注释/XMP/ICC 往返 md5 相同，`FrameCount`/`Duration` 不变；再断言 C2PA 扩展**没被删**（把已知限制锁进测试，防止哪天 upstream 改了行为我们还以为删了）。
- PSD：断言 image data 段 md5 不变、IRB 里该删的 ID 没了、该留的 ID 还在。
- JXL（如果哪天要支持）：断言裸码流**不写**、容器只删 `Exif`/`xml `，且 `jbrd` 保留（同样把"没删干净"锁进测试）。

**工程细节**

- 别用 `FileType` 字符串做分支：WebP 会报 `Extended WEBP`，JXL 会报 `JXL Codestream`，APNG 报 `APNG`。按扩展名/族判断更稳。
- 测试时别用错扩展名：ExifTool 会按扩展名判类型。我用 `.exif` 测 PSD 时它把文件当 EXIF 读，直接报 `Not a valid EXIF`；改成 `.psd` 就正常。同理，`.icc`/`.xmp` 这类临时文件名也会把目标文件带偏。
- 裸码流 JXL 只要走到写路径就会报错，哪怕没有任何 EXIF/XMP（实测 22 字节裸码流跑 `-all=` 也是 exit 1）。如果将来要支持 JXL，必须在探测阶段先按容器魔数把裸码流挑出去，不能指望"没元数据就不会写"这个兜底。

## 6. 我实际跑过的验证（可复现）

```bash
# 版本
perl exiftool-13.61/exiftool -ver           # 13.61
# 哪些格式可写
perl exiftool-13.61/exiftool -listwf | grep -Ei "apng|gif|jxl|psd|webp"

# 读：把该看的组都摊开（-u 显示未知 tag）
exiftool -G1 -a -s -u FILE
exiftool -v3 FILE                            # 看 chunk/盒/IRB 结构

# 擦：保守写法（先备份，别原地）
cp FILE work.bin && exiftool -all= work.bin
# 前后对比
exiftool -G1 -a -s -EXIF:all -XMP:all -IPTC:all -ICC_Profile:all -JUMBF:all work.bin
cmp FILE work.bin && echo "回到原样"
md5sum FILE work.bin                         # Windows 用 Get-FileHash
```

交叉验证（Windows 上能装到的静态构建都行，命令本身跨平台）：

| 格式 | 工具 | 看什么 |
|---|---|---|
| WebP | `webpinfo -summary f.webp`、`webpmux -info f.webp` | chunk 列表、VP8X 标志、帧数 |
| WebP 动画 | `magick compare -metric AE "a.webp[0]" "b.webp[0]" null:` | 逐帧像素差（0 才算没动） |
| PNG/APNG | `pngcheck -v f.png`、`apngdis f.png`、`apngasm` | CRC、块顺序、拆帧再合帧 |
| GIF | `gifsicle --info f.gif`、`gifsicle f.gif -e -o frames/` | 帧数/延迟/循环，拆帧比像素 |
| JXL | `djxl f.jxl out.png`、`djxl --reconstruct_jpeg f.jxl out.jpg` | 能否解码；重建出的 JPEG 里还有没有 IPTC/注释（`exiftool -a -G1 out.jpg`） |
| PSD | `magick identify -verbose f.psd`、Photoshop/GIMP 打开 | 图层数、尺寸、色彩模式；`exiftool -Photoshop:LayerCount` |
| 通用 | `magick compare -metric AE a.png b.png null:` | 像素级差异 |

## 7. 出处

全部于 2026-11-09 实际打开核对。

**ExifTool 官方文档**

- ExifTool 主页 · 支持的文件类型表（含 GIF / JXL / PSD / WEBP / PNG 行的 R/W 标记，以及脚注 1/3/7）— https://exiftool.org/
- TagNames: GIF（Comment / ICC / XMP 可写，JUMBF 只读）— https://exiftool.org/TagNames/GIF.html
- TagNames: PNG（11.63 起写入时把 IDAT 后的文本块前移、`acTL` 只读）— https://exiftool.org/TagNames/PNG.html
- TagNames: RIFF（"只能写 WebP 的 EXIF/XMP/ICC"）— https://exiftool.org/TagNames/RIFF.html
- TagNames: Jpeg2000（"JXL 只写 EXIF 和 XMP"、brob、没有 jbrd）— https://exiftool.org/TagNames/Jpeg2000.html
- TagNames: Photoshop（PSD IRB 标签）— https://exiftool.org/TagNames/Photoshop.html
- FAQ #32「How do I safely delete all metadata from a file?」— https://exiftool.org/faq.html#Q32
- exiftool_pod（`-All=` 语义、"不是所有组都能删"、颜色可能受影响）— https://exiftool.org/exiftool_pod.html

**ExifTool 13.61 源码（tag `13.61`）**

- 源码包（我下载解压后 grep 的）— https://github.com/exiftool/exiftool/archive/refs/tags/13.61.tar.gz
- `lib/Image/ExifTool.pm`（`@writeTypes`、`%onlyWriteFile`、JXL 魔数、`-listwf` 来源）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool.pm
- `lib/Image/ExifTool/PNG.pm`（`acTL` 与 `OverrideFileType("APNG")`、`%isDatChunk`、文本块前移、写循环）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/PNG.pm
- `lib/Image/ExifTool/WritePNG.pl`（PNG 写入辅助，无 APNG 特殊处理）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WritePNG.pl
- `lib/Image/ExifTool/GIF.pm`（可写应用扩展白名单、非可写扩展 `$justCopy`）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/GIF.pm
- `lib/Image/ExifTool/RIFF.pm`（WebP chunk 表）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/RIFF.pm
- `lib/Image/ExifTool/WriteRIFF.pl`（两趟写、`%webpMap`、VP8X 标志 `&= ~0x2c`、只写 WEBP）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteRIFF.pl
- `lib/Image/ExifTool/Photoshop.pm`（`%psdMap`、`ProcessPSD` 只重写 IRB）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/Photoshop.pm
- `lib/Image/ExifTool/WritePhotoshop.pl`（IRB 改写、PSD-EXIFInfo 的特殊分支）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WritePhotoshop.pl
- `lib/Image/ExifTool/Jpeg2000.pm`（`%jxlMap`、裸码流重封装、brob 读写）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/Jpeg2000.pm

**libjxl（jbrd / brob 的一手依据）**

- `doc/format_overview.md`（三种元数据、brob、jbrd 用途）— https://raw.githubusercontent.com/libjxl/libjxl/main/doc/format_overview.md
- `lib/jxl/jpeg/jpeg_data.h`（`app_data`/`app_marker_type`/`com_data`）— https://raw.githubusercontent.com/libjxl/libjxl/main/lib/jxl/jpeg/jpeg_data.h
- `lib/jxl/jpeg/enc_jpeg_data.cc`（`EncodeJPEGData`：ICC/Exif/XMP 之外的 APPn 与 COM 进 brotli blob）— https://raw.githubusercontent.com/libjxl/libjxl/main/lib/jxl/jpeg/enc_jpeg_data.cc
- `lib/jxl/encode.cc`（`AppendBoxWithContents("jbrd", jpeg_metadata)`）— https://raw.githubusercontent.com/libjxl/libjxl/main/lib/jxl/encode.cc

**格式规范**

- W3C PNG 3rd Edition（APNG 已是正式章节：`acTL` 必须在 IDAT 前、`fcTL`/`fdAT` 序号、辅助块无顺序限制）— https://www.w3.org/TR/png-3/
- WebP Container Specification（chunk 顺序、ICCP 必须在图像数据前、元数据可乱序、未知 chunk 建议保留）— https://developers.google.com/speed/webp/docs/riff_container
- Adobe Photoshop File Formats Specification（IRB 与 0x0404/0x040C/0x040F/0x0422/0x0424）— https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/
- GIF89a Specification（Comment Extension / Application Extension）— https://www.w3.org/Graphics/GIF/spec-gif89a.txt

**我们自己的代码**

- `src/PhotoPrivacy.Core/ExifTool/WipeStrategies/WipeStrategyResolver.cs:38`（`.apng` → Png 族）
- `src/PhotoPrivacy.Core/Rules/WipeRuleEngine.cs:47,114-121,164-165`（Png 族默认 `-all=`、不保 ICC）
- `src/PhotoPrivacy.Core/Configuration/AppConfig.cs:82`（允许清单含 `.apng`）
- `README.md:29-31`（34 个扩展名、不支持格式 fail-closed）
- `scripts/publish.sh:8`（ExifTool 钉 13.61）
