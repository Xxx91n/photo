# ExifTool upstream issue 草稿 — GIF C2PA 标 R/D 但实测不可删

- 状态：**已发布**：https://github.com/exiftool/exiftool/issues/515（2026-11-10，Windows 侧 gh 以账号 Xxx91n 代发；WSL 侧无 gh 认证，子代理当时停在了草稿）
- 目标仓库：https://github.com/exiftool/exiftool （GitHub issues 开启且有社区 issue 活动）
- 测试版本：ExifTool 13.61（2026-11-05 发布，exiftool.org 当前最新；GIF.pm 关键行 32/98 与 master 一致）
- 复现脚本与全部输出：`C:\tmp\ppv\work\latest\`（make_files.pl、run_all.sh、run_ctrl.sh 及产物）
- 查重（2026-11-10）：
  - GitHub issues（未认证 search API，is:issue 含已关闭）：`C2PA_GIF` 0 条、`C2PA GIF` 0 条、`JUMBF GIF` 0 条
  - 官方论坛 exiftool.org/forum：整站无限期关闭（首页公告，因 AI 爬虫滥用），无法站内检索；网页搜索未发现 GIF C2PA 删除相关历史帖
  - Changes：无相关修复记录（只有 C2PA read 支持条目）
- 贡献准则：仓库无 CONTRIBUTING.md、无 ISSUE_TEMPLATE，README（根目录无扩展名文件）无 bug 报告指引
- 发布后把 issue 链接补到这里：https://github.com/exiftool/exiftool/issues/515

---

以下是 issue 正文（英文，可直接张贴）。

**Title:** GIF: support table lists C2PA as R/D, but `-jumbf:all=` silently does not delete the C2PA JUMBF data

### Summary

The [supported formats table](https://exiftool.org/) lists C2PA for GIF as `R/D`:

> | GIF | R/W | Compuserve Graphics Interchange Format | - | - | R/W/C | R/W/C | R/D | R/W/C GIF |

with the footnote: *"C2PA metadata is not currently Writable, but may be Deleted from some file types by deleting the JUMBF group (ie. `-JUMBF:all=`)."*

In practice the C2PA JUMBF data in a GIF — the `C2PA_GIF/` application extension — is read but never deleted. `-jumbf:all=` reports `0 image files updated / 1 image files unchanged` and exits 0 with no warning, leaving the extension byte-for-byte intact. `-all=` deletes Comment/XMP/ICC but also leaves the C2PA extension in place.

### Environment

- ExifTool 13.61 (latest release, Nov 5, 2026; the two relevant GIF.pm lines, 32 and 98, are identical on master)
- Perl 5.38.2, Linux x86_64 (WSL2)

### How to reproduce

Start from `t/images/GIF.gif` (8x8, 256-entry global color table, already carries Comment/XMP/ICC application extensions). Insert a `C2PA_GIF/` application extension (application id `C2PA_GIF`, authentication code `01 00 00`, per the comment in GIF.pm and the C2PA GIF binding) right after the global color table, carrying a minimal JUMBF store:

```perl
#!/usr/bin/perl
use strict; use warnings;
open my $fh, "<", "t/images/GIF.gif" or die $!; binmode $fh;
my $d = do { local $/; <$fh> }; close $fh;
my $flags = ord(substr($d, 10, 1));
my $off = 13 + 3 * (2 << ($flags & 7));   # end of header + LSD + global color table
my $jumd_payload = ("\x00" x 16) . "\x02" . "c2pa\x00";  # UUID, toggles: Label
my $jumd = pack("N", 8 + length($jumd_payload)) . "jumd" . $jumd_payload;
my $jbox = pack("N", 8 + 2) . "json" . "{}";
my $jumb = pack("N", 8 + length($jumd) + length($jbox)) . "jumb" . $jumd . $jbox;
my $ext = "\x21\xff\x0b" . "C2PA_GIF" . "\x01\x00\x00";
$ext .= chr(length $jumb) . $jumb . "\x00";   # one sub-block + block terminator
open my $o, ">", "c2pa.gif" or die $!; binmode $o;
print $o substr($d, 0, $off) . $ext . substr($d, $off);
close $o;
```

### Reading works (R)

```
$ exiftool -G1 -a -s c2pa.gif | grep JUMBF
[JUMBF]         JUMDType                        : 00000000-0000-0000-0000000000000000
[JUMBF]         JUMDLabel                       : c2pa

$ exiftool -v3 c2pa.gif 2>&1 | grep "Application Extension"
Application Extension: C2PA_GIF/
Application Extension: XMP Data/XMP
Application Extension: ICCRGBG1/012
```

### Deleting does not work (D)

```
$ cp c2pa.gif t1.gif
$ exiftool -jumbf:all= t1.gif
    0 image files updated
    1 image files unchanged
$ echo $?
0
$ md5sum c2pa.gif t1.gif
79e9c45e66e18161e8ab34f675060f6d  c2pa.gif
79e9c45e66e18161e8ab34f675060f6d  t1.gif
```

No warning is printed (stderr empty). The `C2PA_GIF/` extension is still in the file (verified byte-wise). Same story with `-all=`: Comment/XMP/ICC are removed (file changes, with the usual ICC warning) but the C2PA extension remains, and it survives any subsequent rewrite of the file (e.g. adding and then removing a comment).

### Control: the same JUMBF store in a JPEG is deleted

Embedding the identical JUMBF store in an APP11 segment of `t/images/Writer.jpg`:

```
$ exiftool -jumbf:all= c2.jpg
    1 image files updated
```

and the JUMBF data is gone afterwards. So the documented delete command does work where the table says R/D (JPEG) — it just silently does nothing for GIF.

### Root cause (lib/Image/ExifTool/GIF.pm, tag 13.61)

- The `C2PA_GIF/` entry (line 98) is read-only: it has no `Writable` flag, and the writable application-extension whitelist does not include it (line 32: `my @appExtensions = ( 'XMP Data/XMP', 'ICCRGBG1/012' );`).
- On write, any application extension without `Writable` is copied verbatim (line 434): `$justCopy = 1 if $outfile and not $$extInfo{Writable};` — header, data sub-blocks and terminator are rewritten unchanged.
- `%gifMap` (line 26) only maps `XMP` and `ICC_Profile`, so a JUMBF group delete never even reaches the GIF writer.

This matches the Notes in `lib/Image/ExifTool/Jpeg2000.pm`, which say C2PA metadata *"may be deleted from writable JPEG, PNG, WebP, TIFF-based, and QuickTime-based files"* — GIF appears in that module's read list but not in its delete list. So the homepage table's `R/D` for GIF disagrees with both the code and the module documentation.

### Expected

One of:

1. `-jumbf:all=` (and `-all=`) actually delete the `C2PA_GIF/` application extension, matching the `R/D` marking; or
2. the support table lists C2PA as `R` for GIF, and `-jumbf:all=` emits a warning when a `C2PA_GIF/` extension is present but cannot be deleted.

The current silent no-op is the worst outcome for privacy workflows: a user who runs `-jumbf:all=` on a GIF gets no indication that Content Credentials are still embedded.
