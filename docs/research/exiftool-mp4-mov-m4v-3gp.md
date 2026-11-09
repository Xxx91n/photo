# ExifTool 13.61 对 MP4 / MOV / M4V / 3GP / 3G2 的支持与擦除风险评估

资料核对日期：2026-11-09

实测环境：WSL（bash + Perl 5.38.2）+ ExifTool 13.61（源码取自 tag `13.61`）+ ffmpeg 7.0.2 static（johnvansickle 构建）。样本全部是我用 ffmpeg 生成的（h264+aac，3gp/3g2 用 h263，3g2 音轨为 AMR-NB），共 6 个：`sample.mp4` / `sample.mov` / `sample.m4v` / `sample.3gp` / `qt3g2.3g2` / `qt-test.qt`（.qt 为注入后 .mov 的复制）。文中字节数都是这些样本的值，**换样本会变**。官方 `exiftool.org` 当前线上版本正好是 13.61，与本地源码同版（[exiftool.org 主页](https://exiftool.org/)：Download Version 13.61 - Nov. 5, 2026）。

## 1. 一句话结论

**五个扩展名（加 .qt 共六个）都值得留在 allowed_extensions**：真实参数串 `-All= -Time:All=` 把我注入的 ItemList/XMP/GPS/loci 全删净，**时间戳是原地置零而不是删字段**，timeScale/duration/采样表原样，mdat 载荷六个样本字节级 md5 全部不变，全量解码通过。真正的残留风险不在"删坏文件"，而在**"删干净"三个字说满**：官方自己的口径就是 MOV/MP4 "Most top-level metadata is removed"（部分清理），EXIF 只能在已有结构上改、C2PA 只能删（且未实测）。

| 格式 | 13.61 读写 | 实测结论 | 最大风险 |
|---|---|---|---|
| MP4 | R/W | 注入全删净，mdat 不变，解码 OK | 官方口径"部分清理"；EXIF 只能改已有结构（未实测删除） |
| MOV / QT | R/W | 同上；©swr 写 UserData | 同上 |
| M4V | R/W | 同上 | 同上 |
| 3GP | R/W | 同上，**3gp loci 也能删** | loci 归 Location 组，`-Time:All=` 不管它，靠 `-All=` 兜住（实测已验证） |
| 3G2 | R/W | 结构重排但解码 OK | 无元数据也会整体重写文件 |
| MQV | R/W | **无样本未实测**（无 muxer），仅静态证据 | 只有 `-listwf` 与官方表背书 |

## 2. 我们现在的擦法

- 扩展名映射：`src/PhotoPrivacy.Core/ExifTool/WipeStrategies/WipeStrategyResolver.cs:41-43` —— `mov/mp4/m4v/qt/3gp/3g2` 全部映射到 `Video` 族。
- 允许清单：`src/PhotoPrivacy.Core/Configuration/AppConfig.cs:84` —— `".mov", ".mp4", ".m4v", ".qt", ".3gp", ".3g2"`（六个都已在 34 个清单里，本次是**去留验证**，不是新扩）。
- 安全默认：`src/PhotoPrivacy.Core/Rules/WipeRuleEngine.cs:47` —— Video 族 `(stripAll: true, preserveIcc: false, stripExif: false, stripXmp: false, stripIptc: false, stripTime: true)`。
- 参数串拼装：`WipeRuleEngine.cs` 的 `BuildEffectiveArgs`，Video 分支（`case WipeFormatFamily.Video:`）—— strip_all 时输出 **`-All=`**，strip_time 再追加 **`-Time:All=`**。默认开合下真实参数串就是 **`-All= -Time:All=`**。大小写与 `-all=` 等价（官方 Tag Names Explained：tag 名"case is not significant"，组名同理，[exiftool.org](https://exiftool.org/)）。
- 执行方：`src/PhotoPrivacy.Core/ExifTool/ExifToolCommandBuilder.cs:72-79` —— EffectiveArgs 逐行写入 stay_open 通道，再追加 `-overwrite_original` 与目标路径。所以**每文件的真实完整命令是 `exiftool -All= -Time:All= -overwrite_original FILE`**，本次实测逐字复刻。
- `-Time:All=` 里的 `Time` 是 family 2 **类目组**（官方组表：`2 (Category) | Audio, Author, Camera, ..., Time, ..., Video`，[exiftool.org](https://exiftool.org/)），覆盖所有日期/时间类 tag；`-listd` 的可删组列表里也有 `Time`。

## 3. 注入实测（每项成败，先立靶子再打）

用逐条命令注入（与图片族文档同口径，`-overwrite_original`）：

| 注入项 | mp4 | mov | m4v | 3gp | 说明 |
|---|---|---|---|---|---|
| `-Title/-Artist/-Comment/-Genre`（ItemList ©nam/©ART/©cmt/©gen） | ✓ | ✓ | ✓ | ✓ | 1 image files updated |
| `-Software=TestSoftware` | ✗（只进 XMP） | ✓（UserData SoftwareVersion/©swr） | ✗（只进 XMP） | ✗（只进 XMP） | ItemList 无 Software 写入目标，mov 走 ©swr |
| `-XMP-dc:Description` | ✓ | ✓ | ✓ | ✓ | mp4 XMP 在顶层盒，mov 在 moov-udta-XMP_（`%mp4Map`/`%movMap`，[WriteQuickTime.pl](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteQuickTime.pl)） |
| `-GPSCoordinates`（©xyz） | ✓ | ✓ | ✓ | ✓ | 值必须是 DMS、两个浮点、或 ISO6709（`+35.5000+139.5000+10.0000/`）；我先用 `35.5+139.5+10.0` 和 `35.5 139.5 10.0` 都被 `PrintConvInv` 拒（[WriteQuickTime.pl:149](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteQuickTime.pl)） |
| `-LocationInformation`（3gp loci） | - | - | - | ✓ | 字符串格式 `Role=shooting Lat=35.5 Lon=139.5 Alt=10.0`；`Writable => 'undef'`、Avoid、归 Location 类目（[QuickTime.pm](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/QuickTime.pm)） |
| `-EXIF:Make/-EXIF:Model/-EXIF:DateTimeOriginal` | ✗ | ✗ | ✗ | ✗ | **0 image files updated，无任何警告**。`%movMap`/`%mp4Map` 根本没有 EXIF 写入目标（只有 HEIC 映射有，`EXIF => 'ItemInformation'`）——EXIF 只能在文件里已存在 EXIF 结构时改，官方表 R/W3 的脚注语义本次未在页面上核到原文，以实测为准 |

注入后体积（字节数会随样本变）：mp4 30391→33876、mov 30442→33955、m4v 30379→33864、3gp 71909→75498。

## 4. 擦除实测（真实参数串 `-All= -Time:All= -overwrite_original`）

六个文件全部 `1 image files updated`，前后对比：

**删掉了（`-G1 -a -s -u` 前后对比，全部消失）**：

- ItemList 整组：Title / Artist / Comment / Genre / Encoder（ffmpeg 的 ©too）/ GPSCoordinates
- XMP 整组（dc/tiff/xmpDM/x）
- UserData SoftwareVersion（mov）、UserData LocationInformation（3gp loci）
- mp4 的 HandlerVendorID（Apple，mov 级 tag）

**时间戳（下一节单独说）**：六个日期字段全部置零。

**留下（结构必需 + 非个人元数据）**：TimeScale / Duration / MediaTimeScale / MediaDuration / TrackDuration、stts/stsc/stsz/stco 采样表、AVCConfiguration、HandlerType、CompressorName（Lavc61.3.100 libx264，编码器指纹保留）、Track 级 VendorID（FFmpeg）、MediaDataSize/Offset。

**mdat 字节级**：六个样本擦前后 mdat 载荷 **md5 全部一致**（mp4/mov/m4v/qt 均为 `45f166…da3c`，27904 字节；3gp `9d34f…95fb`，69852 字节；3g2 `a2d4b…11dd`，69886 字节）。mdat 的**文件内偏移变了**（moov 缩小/重排），载荷不变。

**文件大小**：mp4 33876→30294、mov 33955→30402、m4v 33864→30282、3gp 75498→71909、qt 33955→30402；3g2（本来无注入元数据）71692→71692 但报 `1 image files updated`——**ExifTool 对 QuickTime 族总是整体重写**，3g2 的顶层盒顺序从 mdat-first 被重排为 moov-first。重排后解码正常，但"没元数据就没动文件"这句话对视频族不成立，审计事件里会看到 updated。

**播放性（ffprobe + 全量解码）**：六个文件 `-show_entries` 的 format/双轨（video+audio）/时长 2.0s 全部不变；`ffmpeg -v error -i F -f null -` 全部 decode OK。3g2 出现的 `non monotonically increasing dts` 警告是**生成基线固有**（刚生成的 fresh.3g2 同样警告、exit 0），与擦除无关。

## 5. `-Time:All=` 对时间戳的影响（视频格式最大风险点，已实证）

先把日期注入成真值再擦，验证是不是"伤时戳"：

- 注入：`-QuickTime:CreateDate/-ModifyDate`（mvhd）+ `-TrackCreateDate/-TrackModifyDate`（tkhd）+ `-MediaCreateDate/-MediaModifyDate`（mdhd），mp4 与 mov 各一，全部 1 updated、`-G1 -a -s` 读回确认为 2026:01:0x。
- 擦除（同真实参数串）后：六个字段全部还在盒子里，值**置零为 `0000:00:00 00:00:00`**；TimeScale=1000、MediaTimeScale=15360/44100、Duration/MediaDuration=2.00s **原样**；mdat md5 不变；ffprobe duration=2.0；全量解码 OK。

源码口径一致：日期 tag 在 `MediaHeader` 等表里标 `Groups => { 2 => 'Time' }`（[QuickTime.pm](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/QuickTime.pm)），而 **TimeScale/Duration 没有这个组标**（TimeScale 在 Movie 表，类目是 Video），所以 `-Time:All=` 物理上碰不到 timeScale。**结论：`-Time:All=` 不会伤播放必需时戳**，代价是日期值归零——部分播放器会把 0 显示成 1904 或空日期，属显示问题不属播放问题。

## 6. 逐格式

### 6.1 MP4 —— 留
R/W（官方表），实测注入全删净、mdat 不变、解码 OK。残留：CompressorName/编码器指纹；EXIF 路径见 6.7。

### 6.2 MOV / QT —— 留
官方表同一行（MOV, QT）。实测同 MP4；区别是 Software 有专门写入目标（©swr→UserData）。`.qt` 是同一引擎的别名扩展（我们的样本 .qt 由 .mov 复制而来，FileType 仍报 MOV），无独立风险。

### 6.3 M4V —— 留
官方表与 M4A/M4B/M4P 同行（MPEG-4 Audio/Video, QuickTime-based）。实测同 MP4。M4A/M4P 等音频变体同引擎（未实测，静态同源）。

### 6.4 3GP —— 留
官方表 3GP,3GPP R/W。实测额外多一项：**loci（LocationInformation）能写也能删**（`-All=` 兜住，因为它归 Location 类目而非 Time 类目——`-Time:All=` 不管它，这是参数串语义的一个细节，别误以为 `-Time:All=` 会删位置）。

### 6.5 3G2 —— 留（带一个注记）
官方表 3G2,3GP2 R/W。样本 h263+AMR-NB 实测：擦除触发整体重写 + moov 前置重排，大小不变、解码 OK。因样本没注入元数据（3g2 的 ItemList 写入与 3gp 同引擎，静态同源），"3g2 删净元数据"这一步是**推定**（同引擎），结构安全是实测的。

### 6.6 MQV —— 无样本未实测
官方表 R/W，`-listwf` 在列；无 ffmpeg muxer 造不出真 MQV。旁证：把 .mov 复制改名 .mqv，ExifTool 报 FileType=MQV 并完成擦除、解码 OK（但内容其实是 MOV，不能当 MQV 实测）。建议保留映射但标注"未实测"。

### 6.7 全族共同残留
- **EXIF**：官方表 R/W3，但干净文件**注入被拒**（0 updated、无警告）。相机直出的 mov 若已带 EXIF 结构，删除路径未实测——这是本报告最大的证据缺口。
- **IPTC**：官方 R/W3，未实测（无法注入，同因）。
- **C2PA/JUMBF**：官方 R/D；`-listd` 里 JUMBF 在可删组列表；**未实测**（无样本）。
- 官方 Writer Limitations 的原文口径（[exiftool.org](https://exiftool.org/)）：**"MOV/MP4 - Most top-level metadata is removed."** ——"部分清理"是官方明说的。

## 7. 静态核对

- `-listf`：3G2/3GP/3GP2/3GPP/M4V/MOV/MP4/MQV 全部可识别；`-listwf`：全部可写。
- 官方支持表（[exiftool.org](https://exiftool.org/)，2026-11-09 打开）：上述每行都是 `R/W`，EXIF `R/W3`、IPTC `R/W3`、XMP `R/W/C`、ICC `-`、C2PA `R/D`、Other `R/W/C QuickTime`。
- 写入引擎：[WriteQuickTime.pl](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteQuickTime.pl) 的 `%movMap`/`%mp4Map` 定义 XMP/ItemList 落点；EXIF 只有 HEIC 映射有目标。
- 错误扩展名：实测 ftyp 优先于扩展名（MP4 改名 .3gp 仍报 MP4）；反向也有（MOV 改名 .mp4 报 MP4、改名 .mqv 报 MQV）。对 fake.3gp 跑真实参数串：擦除成功、仍报 MP4、解码 OK——**扩展名错不会把擦除带偏**，但审计日志别拿 FileType 字符串当真（与图片族文档同一教训）。
- ICC：官方列 `-`，QuickTime 族没有 ICC 剥离语义（代码里 `family is not WipeFormatFamily.Video` 才追加 `-icc_profile:all=`，逐组模式同）——与我们实现一致。

## 8. 横向风险清单

| 风险项 | 现状 | 说明 |
|---|---|---|
| 播放安全 | 实测安全 | 六样本 mdat md5 不变、时长/轨道不变、全量解码 OK；日期置零不碰 timeScale |
| 时间戳语义 | 置零而非删除 | 字段保留、值为 0；部分播放器显示 1904/空日期，是显示问题 |
| 整体重写 | 无条件发生 | 连无元数据的 3g2 都报 updated 且重排盒序；不能拿"文件没变"当审计口径 |
| 删不干净的 | EXIF 仅已有结构可改、IPTC 未实测、C2PA R/D 未实测、编码器指纹保留 | 官方口径"Most top-level metadata is removed"，UI/审计文案不能写"全部清除" |
| loci/GPS | 实测可删 | 3gp loci 归 Location 组，靠 `-All=` 删，不靠 `-Time:All=` |
| 大文件/分片 MP4（moof）、DRM、HEIC-in-MOV | **未实测** | 本次只有小样本；分片 MP4 的 moof/mdat 路径没验证 |
| 错误扩展名 | 不影响擦除 | ftyp 优先；FileType 字符串会漂（MP4/MQV），别做分支判断 |

## 9. 落地建议

**六个都留**（.mov/.mp4/.m4v/.qt/.3gp/.3g2），命令保持 `-All= -Time:All=`，不必改。理由：真实参数串实测删净全部可注入元数据、mtime 置零不伤播放、mdat 字节级不变、官方与源码双向印证。

**还欠的决策（按优先级）**：
1. 审计/UI 文案定调：写"已删除可移除的元数据"而不是"全部清除"（官方 Writer Limitations 背书"部分清理"）。
2. `-Time:All=` 的置零语义要不要在产品里说明（日期显示 1904/空 是否可接受）。
3. 证据缺口择期补：已有 EXIF 结构的视频（相机直出）删除路径；IPTC 同；C2PA 样本；分片 MP4（moof）；MQV 真样本。补齐前别在文档/文案里对这几项打包票。
4. 若 issue #8 顺带考虑 m4a/m4b/m4p 音频：同引擎，静态结论可复用，但没实测，别直接抄"已验证"。

## 10. 我实际跑过的验证（可复现）

```bash
# 环境
perl /mnt/c/tmp/ppv/work/exiftool-13.61/exiftool -ver          # 13.61
~/ppv/tools/ffmpeg-static/ffmpeg -version | head -1             # 7.0.2-static

# 造样本（各 2 秒；3gp/3g2 需 h263 的 176x144）
ffmpeg -f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=frequency=440:duration=2 \
  -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest sample.mp4
# mov:  追加 -f mov ；m4v: -f ipod
ffmpeg -f lavfi -i testsrc=duration=2:size=176x144:rate=15 -f lavfi -i sine=frequency=440:duration=2 \
  -c:v h263 -c:a aac -shortest -f 3gp sample.3gp
# 3g2: -c:a libopencore_amrnb -strict experimental -f 3g2

# 注入（逐项，记录成败）
exiftool -overwrite_original -Title=SampleTitle -Artist=TestArtist -Comment=TestComment -Genre=TestGenre FILE
exiftool -overwrite_original -XMP-dc:Description=TestXMPDescription FILE
exiftool -overwrite_original -GPSCoordinates=+35.5000+139.5000+10.0000/ FILE
exiftool -overwrite_original -LocationInformation='Role=shooting Lat=35.5 Lon=139.5 Alt=10.0' sample.3gp
exiftool -overwrite_original -EXIF:Make=TestMake FILE            # 0 updated（拒绝创建）

# 擦除（仓库 video 族真实参数串，与 ExifToolCommandBuilder 一致）
exiftool -All= -Time:All= -overwrite_original FILE

# 对比
exiftool -G1 -a -s -u FILE            # 前后各跑一次
ffprobe -v error -show_entries format=duration -show_entries stream=codec_name,codec_type,duration FILE
ffmpeg -v error -i FILE -f null -     # 全量解码
# mdat 载荷 md5（自写 perl：定位顶层 mdat，取 size-8 载荷做 Digest::MD5）
```

## 11. 出处

全部于 2026-11-09 实际打开核对。

**ExifTool 官方文档**
- 主页 · 支持表（3G2/3GP/M4A-M4V/MOV,QT/MP4/MQV 行的 R/W 与 R/W3 标记）、family 2 类目表（Time/Location）、Tag Names Explained（大小写不敏感）、Writer Limitations（"MOV/MP4 - Most top-level metadata is removed."）、`-listd` 说明 — https://exiftool.org/
- TagNames: QuickTime（ItemList/©nam/©ART/©cmt、GPSCoordinates、loci）— https://exiftool.org/TagNames/QuickTime.html

**ExifTool 13.61 源码（tag `13.61`）**
- 源码包 — https://github.com/exiftool/exiftool/archive/refs/tags/13.61.tar.gz
- `lib/Image/ExifTool/QuickTime.pm`（MediaCreateDate 的 `Groups => {2=>'Time'}`、TimeScale 无 Time 组、loci `Writable=>'undef'`、GPSCoordinates/©xyz、ftyp 表）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/QuickTime.pm
- `lib/Image/ExifTool/WriteQuickTime.pl`（`%movMap`/`%mp4Map` 无 EXIF 目标、HEIC 才有、PrintInvGPSCoordinates/ConvInvISO6709 的取值格式）— https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteQuickTime.pl

**工具**
- ffmpeg 7.0.2 static（johnvansickle 官方静态构建）— https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz

**我们自己的代码**
- `src/PhotoPrivacy.Core/ExifTool/WipeStrategies/WipeStrategyResolver.cs:41-43`（video 六扩展名映射）
- `src/PhotoPrivacy.Core/Rules/WipeRuleEngine.cs:47`（Video 族默认 strip_all=true、strip_time=true）与 `BuildEffectiveArgs` 的 Video 分支（`-All=` + `-Time:All=`）
- `src/PhotoPrivacy.Core/Configuration/AppConfig.cs:84`（六扩展名已在 allowed_extensions）
- `src/PhotoPrivacy.Core/ExifTool/ExifToolCommandBuilder.cs:72-79`（真实执行 = EffectiveArgs + `-overwrite_original`）
