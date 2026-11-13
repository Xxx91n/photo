# ExifTool 13.61 对 MP4 / MOV / M4V / 3GP / 3G2 的支持与擦除风险评估

> §1–§11 主体是 2026-11-09 那轮的记录。#268 在 2026-11-12 又下载样本在本机补测了这四个缺口（其中两项只能造样本、一项删除路径仍没测成），结果见 §12；前后说法不一致的地方，以 §12 为准。

资料核对日期：2026-11-09

实测环境：WSL（bash + Perl 5.38.2）+ ExifTool 13.61（源码取自 tag `13.61`）+ ffmpeg 7.0.2 static（johnvansickle 构建）。样本全部是我用 ffmpeg 生成的（h264+aac，3gp/3g2 用 h263，3g2 音轨为 AMR-NB），共 6 个：`sample.mp4` / `sample.mov` / `sample.m4v` / `sample.3gp` / `qt3g2.3g2` / `qt-test.qt`（.qt 为注入后 .mov 的复制）。文中字节数都是这些样本的值，**换样本会变**。官方 `exiftool.org` 当前线上版本正好是 13.61，与本地源码同版（[exiftool.org 主页](https://exiftool.org/)：Download Version 13.61 - Nov. 5, 2026）。

## 1. 一句话结论

**五个扩展名（加 .qt 共六个）都值得留在 allowed_extensions**：真实参数串 `-All= -Time:All=` 把我注入的 ItemList/XMP/GPS/loci 全删净，**时间戳是原地置零而不是删字段**，timeScale/duration/采样表原样，mdat 载荷六个样本字节级 md5 全部不变，全量解码通过。真正的残留风险不在"删坏文件"，而在**"删干净"三个字说满**：官方自己的说法就是 MOV/MP4 "Most top-level metadata is removed"（部分清理），EXIF 只能在已有结构上改、C2PA 只能删（且未实测）。

| 格式 | 13.61 读写 | 实测结论 | 最大风险 |
|---|---|---|---|
| MP4 | R/W | 注入全删净，mdat 不变，解码 OK | 官方说法"部分清理"；EXIF 只能改已有结构（未实测删除） |
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

用逐条命令注入（与图片族文档同一套做法，`-overwrite_original`）：

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

源码里也对得上：日期 tag 在 `MediaHeader` 等表里标 `Groups => { 2 => 'Time' }`（[QuickTime.pm](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/QuickTime.pm)），而 **TimeScale/Duration 没有这个组标**（TimeScale 在 Movie 表，类目是 Video），所以 `-Time:All=` 物理上碰不到 timeScale。**结论：`-Time:All=` 不会伤播放必需时戳**，代价是日期值归零——部分播放器会把 0 显示成 1904 或空日期，属显示问题不属播放问题。

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
- 官方 Writer Limitations 的原文（[exiftool.org](https://exiftool.org/)）：**"MOV/MP4 - Most top-level metadata is removed."** ——"部分清理"是官方明说的。

## 7. 静态核对

- `-listf`：3G2/3GP/3GP2/3GPP/M4V/MOV/MP4/MQV 全部可识别；`-listwf`：全部可写。
- 官方支持表（[exiftool.org](https://exiftool.org/)，2026-11-09 打开）：上述每行都是 `R/W`，EXIF `R/W3`、IPTC `R/W3`、XMP `R/W/C`、ICC `-`、C2PA `R/D`、Other `R/W/C QuickTime`。
- 写入引擎：[WriteQuickTime.pl](https://raw.githubusercontent.com/exiftool/exiftool/13.61/lib/Image/ExifTool/WriteQuickTime.pl) 的 `%movMap`/`%mp4Map` 定义 XMP/ItemList 落点；EXIF 只有 HEIC 映射有目标。
- 错误扩展名：实测 ftyp 优先于扩展名（MP4 改名 .3gp 仍报 MP4）；反向也有（MOV 改名 .mp4 报 MP4、改名 .mqv 报 MQV）。对 fake.3gp 跑真实参数串：擦除成功、仍报 MP4、解码 OK——**扩展名错不会把擦除带偏**，但审计日志别拿 FileType 字符串当真（与图片族文档同一教训）。
- ICC：官方列 `-`，QuickTime 族没有 ICC 剥离语义（代码里 `family is not WipeFormatFamily.Video` 才追加 `-icc_profile:all=`，逐组模式同）——与我们实现一致。

## 8. 横向风险清单

> 表里「未实测」是 2026-11-09 当时的状态。分片 MP4、C2PA、相机视频后来在本机补测过，见 §12；这张表保留原样。

| 风险项 | 现状 | 说明 |
|---|---|---|
| 播放安全 | 实测安全 | 六样本 mdat md5 不变、时长/轨道不变、全量解码 OK；日期置零不碰 timeScale |
| 时间戳语义 | 置零而非删除 | 字段保留、值为 0；部分播放器显示 1904/空日期，是显示问题 |
| 整体重写 | 无条件发生 | 连无元数据的 3g2 都报 updated 且重排盒序；不能拿"文件没变"当审计依据 |
| 删不干净的 | EXIF 仅已有结构可改、IPTC 未实测、C2PA R/D 未实测、编码器指纹保留 | 官方说法"Most top-level metadata is removed"，UI/审计文案不能写"全部清除" |
| loci/GPS | 实测可删 | 3gp loci 归 Location 组，靠 `-All=` 删，不靠 `-Time:All=` |
| 大文件/分片 MP4（moof）、DRM、HEIC-in-MOV | **未实测** | 本次只有小样本；分片 MP4 的 moof/mdat 路径没验证 |
| 错误扩展名 | 不影响擦除 | ftyp 优先；FileType 字符串会漂（MP4/MQV），别做分支判断 |

## 9. 落地建议

> 下面第 1、2、3 条后来由 #268 补测和定稿了，过程、样本和结果见 §12（有两项没拿到设备直出的真样本，一处删除路径仍没测成）。这里保留当时写下的原样，不改。

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

## 12. 四个证据缺口的本机实测（#268，2026-11-12）

§9 那几件「还欠的」，这一节用下载来的真样本、本机造的样本和 CI 产物跑了一遍。四项里有两项只拿到构造样本、一处删除路径仍没测成，下面逐项写明；先交代清楚拿什么测、样本哪来的，别把构造样本当成设备直出。

### 12.0 怎么测的

- **擦除命令**：`exiftool -All= -Time:All= -overwrite_original FILE`，参数跟 `WipeRuleEngine` 的 Video 分支和 `ExifToolCommandBuilder` 拼出来的一字一样。**通道不同**：§12.1–§12.4 跑的是单次命令行调用，产品走 `-stay_open` 块协议（`-execute` + `{ready}`）。这个差别在分片 MP4 上很要紧，§12.3 单独按产品的块格式复验过。
- **ExifTool 13.61，两条通道**：一条是 CI 发布产物 `release-linux-x64`（Release run `43169200573`，main 分支，2026-11-12 跑成功）里自带的 `<包根>/ExifTool/exiftool`；另一条是 WSL 里的 13.61 源码版，跟 §1–§11 那轮实测同款。两条通道在关键结论上一致；CI 通道只复验了四个样本、MQV 那轮没注入元数据，逐项数字见 §12.7。
- **ffmpeg / ffprobe 7.0.2 static**（johnvansickle），用来造样本、看轨道、全量解码。
- **判法**：擦前擦后各读一次 `-G1 -a -s -n` 比标签；把所有顶层 `mdat` 载荷拼起来算 md5，看画面数据动没动；`ffprobe` 看双轨和时长；`ffmpeg -v error -i F -f null -` 全量解码看文件坏没坏。
- **应用本身没在本机编译**，用的是 CI 编译好的发布产物；本机实测的是产品拼出来的那条 ExifTool 命令。样本和实测用的脚本放在仓外 `C:\photo-verify\t268\`，没进仓库（体积大，出处是别人的公开样本）。
- 样本清单（SHA256 用来对照，想复现照着下）：
  - `c2pa-truepic-zoetrope.mp4`，15,456,823 字节，`23db1d7891973ad0eed6f87967abc2e748663afddacfb0ceb839aa20848cfff3` —— C2PA 官方公开测试文件仓库 `c2pa-org/public-testfiles` 的 `legacy/1.4/video/mp4/truepic-20230212-zoetrope.mp4`。
  - `meta.mov`（iPhone X，769,836 字节，`f5fa5c0b0b920f3280bc8ad01c407506d2c52ef9ca3490f48b90eca7f36aac6d`）、`meta.mp4`（2,170,486 字节，`224ab1bf37fd4030531ccba266bce0153f847481204cb65e9b30f8000de82aa2`）、`embedded-in-heic.mov`（iPhone 15 Pro，7,455,132 字节，`df7d777dc4c1a27782363a4b8c280786980d017c33c470ea23eceb1744d18d54c`）—— `mindeng/nom-exif` 仓库 `testdata/` 里的真实设备录像。另外还下过 `auth.mov` 和 `sony-a7-xavc.MP4` 一起看。
  - `fragmented.mp4`，56,240 字节，`66e4891ae410e9fafecff03fd593a56a1dacc99f475501aa3423ddd310bd538c` —— 本机 ffmpeg 造的分片 MP4，见 §12.3。
  - `constructed.mqv`，27,662 字节，`efe9b2f31d0ca72e0fc8b15a50c97510382f7501d1cba365fa965c3c19d524c7` —— 本机 ffmpeg 造 MOV 后改 ftyp 品牌，见 §12.4。

一句话先给结果：**C2PA 实测能删；分片 MP4 实测写不了（产品通道还会把它记成成功，见 §12.3）；MQV 按官方品牌构造的样本实测能擦；相机视频的 GPS 和设备信息实测删净，这五个样本用 family 0 复核 ExifTool 未读到 EXIF 标签，EXIF 删除路径仍没测成。**下面逐项摆数字。

### 12.1 相机直出视频：找能读出 EXIF 标签的样本，没找到

- **实测了什么**：`-G1 -a -s -n` 下五个真实设备样本（iPhone X、iPhone 15 Pro、Sony A7 等）没有 `IFD0` / `ExifIFD` 这类 EXIF 位置组，隐私信息放在 `[Keys]` / `[ItemList]` / `[UserData]` / GPS 里。`-G1` 不显示 family 0 的 `[EXIF]` 组名，光数 `[EXIF]` 零行不够，所以又用 `-G0 -a -s -n -EXIF:All` 复核：五个样本的 stdout 都是 **0 行标签**，`-G0` 全量组分布只有 `[Composite] [ExifTool] [File] [QuickTime]`，没有 `[EXIF]`（先前看到的那一行是 stderr 的 `ExtractEmbedded` 警告，不是标签）。复核的原始输出（`-G0` / `-G1` 组分布、stdout、stderr）在仓外 `work/g0-recheck.txt`。也就是说 ExifTool 未读到 EXIF 标签；它读不出的私有结构是否存在，这里不打包票。
- **擦除结果**：对 `meta.mov` / `meta.mp4` / `embedded-in-heic.mov` 跑真实命令，全部 `1 image files updated`；GPS 标签 6 / 4 / 6 行全部归零，`[Keys]` 里的 Make、Model、Software、CreationDate、GPSCoordinates 全删，日期置零；三个样本的 mdat 载荷 md5 前后一模一样（`a6cb8053…` / `99a214c8…` / `1636bb40…`）；ffprobe 双轨和时长不变，全量解码 exit 0。
- **「已有 EXIF 结构时删不删得掉」这一条仍没测成**：往干净 MP4 写 `-EXIF:Make` 被拒（§3 复验：`0 image files updated`）；想按 HEIF 品牌造一个带 EXIF 结构的视频也没成——给 ftyp 加字节会让 stco 里的偏移全错位，ExifTool 报 `Chunk offset in stco atom is outside media data`；只换品牌不换大小则进不了 HEIC 的写入路径，报 `unchanged`。
- **结论**：这五个真实设备样本里 ExifTool 未读到 EXIF 标签，真正要删的隐私信息实测删净了；「EXIF-in-video 的删除路径」在能拿到的样本上触发不了，文档不写「EXIF 已删净」，但也不用把它当日常风险。这里用的是 `-G0` 复核，不是 `-G1` 数 `[EXIF]` 行。以后拿到一份用 `-G0 -EXIF:All` 能读出标签的相机视频，照 §12.0 那套跑一遍补上。

### 12.2 C2PA（JUMBF）—— 实测能删

- **样本**：Truepic 给 C2PA 官方公开测试文件造的 MP4。擦前 ExifTool 读出 24 行 `[JUMBF]`、50 行 `[CBOR]`，顶层有个 `uuid` 盒装 manifest；顶层盒依次是 ftyp / uuid / free / mdat / moov。
- **擦除**：`1 image files updated`。擦后 `[JUMBF]` 0 行、`[CBOR]` 0 行，那个 uuid 盒整个没了，文件小 30,310 字节，顶层盒重排成 ftyp / free / moov / mdat。
- **画面**：mdat 载荷 md5 `3bd69eeedf1329efd1fb2c86164657c3` 前后一致；ffprobe 双轨、7.408489 s 不变；全量解码 exit 0。
- **结论**：官方支持表标的 R/D 属实，`-All=` 能把 JUMBF 删掉。之前「不承诺删 C2PA」的说法撤回，README 已改。别把 GIF 那边 `GifC2paPreservationTests` 盯着的「C2PA 删不掉」套到视频上——两个容器引擎不一样，实测结果也相反。

### 12.3 分片 MP4（moof）—— 实测写不了，不是部分清理

- **样本**：本机 ffmpeg 用 `-movflags frag_keyframe+empty_moov+default_base_moof -frag_duration 500000` 造的 4 秒文件，9 组 moof + mdat，末尾 mfra；ffprobe 双轨、4.133333 s。
- **擦除**：exit 1，报 `Error: Can't yet handle movie fragments when writing`，`0 image files updated`。文件一个字节没动：9 个 mdat 载荷 md5 前后一致（`60781b03fcb7ab8ad6bfb4934724759d`）、114 个标签一行不少、顶层盒序不变，解码正常。
- **按产品的块格式复验**：照 `ExifToolCommandBuilder.BuildWipeTaskBlock` 的真实内容（`-All=` / `-Time:All=` / `-overwrite_original` / 路径 / `-echo1` / `TASK_DONE_1` / `-execute`）喂给 `-stay_open True -@ -`，CI 产物那份 ExifTool 的 stdout 是 `TASK_DONE_1`、`0 image files updated`、`1 files weren't updated due to errors`、`{ready}`——**`{ready}` 照样到了**，错误只在 stderr；文件 md5 `1a8ab577…` 前后一致、大小 56,240 不变。同样的块喂 C2PA 样本是 `1 image files updated`、文件确实变小，说明块格式本身没问题。原始输出在仓外 `work-stayopen/result.json`。
- **产品里会怎样（重要）**：`ExifToolBridge` 只拿 `{ready}` 当块结束（`OnStdoutLine` → `CompleteBlockJustEchoed` → `TrySetResult`），stderr 那句错误不会变成异常；`WipeMetadataAsync` 之后只看文件有没有变，没变就记一句诊断警告，最后**无条件 `return WipeResult.Cleaned_Modified`**（`ExifToolBridge.cs:402`、`:411`）。于是 `FileTaskPipeline` 会写 `file_processing_succeeded` 加 `partial_clean`，**文件其实一个字节没动，也不会进隔离区**。`file_processing_failed` 只在抛异常时才写，这条路径不抛。这一段是按代码路径推出来的，没有跑完整的 Worker 端到端；块协议的实测只证明了 `{ready}` 照样到、文件没动。
- **结论**：单次命令行调用是 exit 1，直接报错；产品的常驻通道按代码路径会把同一个文件**记成清理成功**，这是审计谎报，既不是「部分清理」也不是「清理失败」。本票只把证据和结论写清楚，**没改产品代码**——修 `ExifToolBridge` 让这类报错变成失败是另一个活，要另开票并经确认。升级 ExifTool 时要重看「写不了」这一条；修好之前，分片 MP4 先转成普通 MP4 再放进监控文件夹。

### 12.4 MQV —— 品牌合规的构造样本能擦，设备直出的真样本没找到

- **真样本**：网上搜到的几个 `.mqv` 全是占位货——4 字节、11 字节的文本，TrID 的 XML 定义，还有一个 C# 源码文件，没一个带 `ftypmqt ` 头。Sony CLIÉ 是 2000–2005 年的设备，公开录像基本绝迹。
- **构造样本**：ffmpeg 造 2 秒 MOV，只把 ftyp 的主品牌和一个兼容品牌改成 `mqt `（盒大小不变，后面的偏移不用重算），扩展名 `.mqv`。ExifTool 读出来的 MajorBrand 原文是 `Sony / Mobile QuickTime (.MQV) US Patent 7,477,830 (Sony Corp)`，FileType 报 MQV；注入 Title / Artist / Comment / GPSCoordinates 成功。
- **擦除**：`1 image files updated`。`[ItemList]` 的 Title / Artist / Comment / GPSCoordinates、`[XMP-*]`、Composite 的 GPS 全删，`uuid` 盒消失，日期置零；mdat 载荷 md5 `509a18dabf3659da11ef081eb4490f5a` 前后一致；ffprobe 双轨、全量解码 exit 0。
- **结论**：MQV 这条 QuickTime 分支的擦除路径跑得通。但这不是设备直出的老 MQV，只算「品牌合规、有视频轨」的构造样本；`.mqv` 本来也不在我们认的 37 个扩展名里，不为它改名单。

### 12.5 几句产品说法，按实测定下来

- **不许说「全部清除」**：审计成功事件已经带 `partial_clean` 说明（#271），日志页能读到并显示，鼠标悬停看全文、复制也带上（#283 / PR #285）。讲清理结果的地方没有「全部清除」这四个字。规则页有个开关叫「全部清除」（`strip_all` 的中文标签），那是开关名字，意思是「打开全删模式」，不是在讲结果；十种语言各有自己的开关名，跟这张票的视频证据不是一回事，这回不动。真要改这个开关名，得连十种语言和规则页冒烟一起做，另开票，别塞进这张票。
- **`-Time:All=` 的置零语义，写进文档**：日期字段不删，只把值改成 0；少数播放器会显示 1904 年或空白；播放用的 timeScale、时长、音画轨道、画面数据都不受影响（§5 六个样本实测）。完整说明写进 README（含 1904 那一句）；日志里那行说明把日期置零写成带条件的说法（「开了时间清理时，拍摄日期改成 0」），不把 1904 那一串塞进去，字多了反而看不清；鼠标悬停能看全文、复制也带得上。规则页能关掉时间清理，固定写「日期改成 0」会在那种配置下谎报；按实际规则动态拼说明要改 Worker 到审计的接口，超出本票范围，所以取条件式文案。理由很实在：用户看到日期变成 1904 会以为文件坏了，得提前讲明白。
- **分片 MP4 擦不掉、还可能被记成成功，要跟用户说清楚**：§12.3 两条通道都跑了；产品里记成成功是读代码推的，没跑 Worker 端到端。README 写明「擦不掉、先转普通 MP4」，别让用户以为文件被清过了。修 bridge 是另一个活，本票没动。
- **C2PA 能删，别再说「不承诺删」**：§12.2 用官方真样本实测 JUMBF 全删，README 里「带 C2PA 的视频还没测过」那句已删掉；但测的是 13.61，ExifTool 升级要重看。

### 12.6 最后落成什么

六个扩展名继续留着，命令一行不改（`-All= -Time:All=`），视频继续算部分清理。

四条缺口的落点：C2PA 实测能删；分片 MP4 实测写不了（产品通道按代码路径会误记成功，没跑 Worker 端到端，见 §12.3）；MQV 构造样本实测能擦，设备直出的真样本仍缺；相机视频实测删净 GPS 和设备信息，这五个样本用 family 0 复核 ExifTool 未读到 EXIF 标签，那条删除路径还是没样本。现在不能说的话有三句：「全部清除」「EXIF 已删净」「分片 MP4 只是部分清理」。

日志里那句说明换成不看英文原文也能懂的话（`PartialCleanNote`）。票评论里补的「日志页要显示 `partial_clean`」，#283 已经做完（PR #285 合进这张票的基线，代码在 `ui/crates/photoprivacy-logs/src/model.rs` 和 `ui/crates/photoprivacy-ui/src/app.rs`），本票核对过，不重做。

### 12.7 用 CI 发布产物自带的那份 ExifTool 复验

Release run `43169200573` 出的 `release-linux-x64` 里有 `<包根>/ExifTool/`，本机 `perl ExifTool/exiftool -ver` 报 **13.61**。四个样本再用这份走一遍同一条命令，关键结论和 §12.1–§12.4 一致（MQV 这一轮没注入元数据，删标签的效果见 §12.4）：

| 样本 | 擦除 | 关键标签 | mdat 载荷 md5 | 全量解码 |
| --- | --- | --- | --- | --- |
| C2PA 官方 MP4 | `1 image files updated` | `[JUMBF]` 24 → 0 | `3bd69e…` 前后一致 | exit 0 |
| 分片 MP4 | exit 1，`Can't yet handle movie fragments when writing`，0 updated | `[QuickTime]` 47 行一行没动 | `60781b…` 前后一致（9 段） | exit 0 |
| 构造的 MQV | `1 image files updated` | 这一轮没注入元数据，删标签的效果见 §12.4 | `509a18…` 前后一致 | exit 0 |
| iPhone X 的 MOV | `1 image files updated` | `[Keys]` 5 → 0 | `a6cb80…` 前后一致 | exit 0 |

另外按产品真实的 `-stay_open` 块格式（`{ready}` 结束）单独复验过一次，分片 MP4 和 C2PA 的逐行输出在仓外 `work-stayopen/result.json`；结论见 §12.3。

产物和实测用的脚本都没进仓库：产物在仓外 `C:\photo-verify\t268\ci\`，记录有四份——`work/report.json`（第一轮，mdat 指标有缺陷：C2PA 和 meta.mp4 数出 0 段 mdat，那两个数别用；标签数和文件大小仍可用）、`work/final-report.json`、`work-ci/ci-report.json`、块协议那份 `work-stayopen/result.json`。正文数字散在这四份里，按各自那一轮的输出对照。

这些数字是一次实测的记录，不是产品的功能宣称。仓库里没有能自动复算它们的测试——样本是别人的公开文件和本机造的文件，不适合当固定夹具塞进仓库。这是本票的一处不足：词表要求「文档里写的数字，测试要能证明」，§12 现在只能靠样本哈希和命令手工复现，换 ExifTool 版本不会自动提醒，得有人重跑。README 那类功能宣称（37 种扩展名由 `FormatCoverageReconciliationTests` 钉着）和 `PartialCleanNote` 的行为测试是另一回事，本票没动它们。
