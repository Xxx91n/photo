# ADR 0040: i18n Three-Layer Locale Resolver + Custom MarkupExtension

**Date**: 2026-08-13
**Status**: Accepted
**Related**: ADR 0001 (Serilog), ADR 0043 (Backup default dir)

## Context

The UI had a `LocalizationService` with 80% infrastructure (3 locale JSON files, runtime switch, CultureChanged event), but ``MainWindow.axaml`` had 40+ hardcoded Chinese/English strings with ``0`` bindings to the service. `LocalizationService` was imported nowhere. Switching language did nothing visible.

Existing `LocalizationService.LoadEmbeddedLocales()` loaded from disk only (no embedded resource), losing locale files on single-file publish or if users delete the directory.

## Decision

### 1. Custom MarkupExtension + JSON locale + INotifyPropertyChanged (Q1 — 方案 A)

Zero new NuGet dependencies. Reuse existing `LocalizationService`.
Write `LocalizeExtension : MarkupExtension` (~60 lines), AXAML: `Text="{ex:Localize nav.config}"`.
MarkupExtension subscribes `LocalizationService.CultureChanged` + holds `WeakReference` to target control, refreshes `Text`/`Content` on event.
Consistent with I18N.Avalonia and Lang.Avalonia.

### 2. Three-layer locale resolver (Q4/Q5 — 方案 A+ 修正版 C)

New `LocalePathResolver` static class (`src/PhotoPrivacy.Core/Constants/LocalePathResolver.cs`, same style as `DefaultPaths.cs`).
Three layers, ascending priority (later overrides earlier on same key):

1. **Embedded resource** (`Assembly.GetManifestResourceStream`) — always available, undeletable, bottom layer. csproj `<EmbeddedResource Include="Localization\Locales\*.json" />`
2. **exe-same-dir `Localization/Locales/`** (`Path.Combine(AppContext.BaseDirectory, "Localization", "Locales")`) — portable/installed both work, installed needs admin to modify
3. **User config dir `locales/`** — per-user override, normal permissions:
   - Windows: `%LOCALAPPDATA%\PhotoPrivacy\locales\`
   - Linux: `$XDG_DATA_HOME` or `~/.local/share/PhotoPrivacy/locales/`
   - macOS: `~/Library/Application Support/PhotoPrivacy/locales/`
   - Single function `GetUserAppDataDir()`, `RuntimeInformation.IsOSPlatform()` branching only once, centralized here

**No hardcoded OS-specific paths**: all platform branches in `LocalePathResolver` only.
**Portable/installed dual-mode auto-supported**: no explicit branching, `AppContext.BaseDirectory` handles naturally.

### 3. RTL support (Q7 — 方案 A)

New `ar.json` (ported from env-manager `frontend/src/lib/translations/ar.json` 29KB).
`LocalizationService` internal `rtlLocales = ["ar"]` list. On locale switch hitting RTL, `CultureChanged` event carries `isRtl: true`.
`MainWindow.axaml` root `<Window>` adds `FlowDirection="{Binding FlowDirection}"` binding.
`MainWindowViewModel.FlowDirection` auto-computed from locale.

### 4. Flat namespace keys (Q6 — 方案 A)

Keep existing flat keys (`nav.*`, `btn.*`, `settings.*`, `service.*`, `status.*`, `mode.*`, `msg.*`).
Add per-page prefixes for full coverage: `config.section.*`, `config.row.*`, `config.desc.*`, `log.btn.clear`, `service.section.*`, `theme.option.*`, `loglevel.option.*`, `btn.open_config_dir`, `btn.browse_ellipsis`.
`Dictionary<string, string>` stays simple, O(1) lookup.

### 5. No pluralization yet (Q8 — 方案 A)

`string.Format` interpolation covers count-display cases. Add `GetPluralString(key, count)` API later when UI surfaces count-based strings needing ICU MessageFormat. No current key needs migration.

## Consequences

- All AXAML hardcoded strings (~40+) replaced with `{ex:Localize key}` bindings
- `LocalizationService` gains three-layer loader logic (~50 lines), existing API unchanged
- New `LocalePathResolver` (~30 lines) centralizes cross-platform path computation
- csproj adds `EmbeddedResource` entries, locale JSONs compiled into DLL

## References

- I18N.Avalonia: https://github.com/Nepitwin/I18N.Avalonia
- Lang.Avalonia: https://www.nuget.org/packages/Lang.Avalonia
- Avalonia localization docs: https://docs.avaloniaui.net/docs/app-development/localizing
- env-manager i18n ref: `D:\Aworker\env-manager\frontend\src\lib\i18n.ts`
- pwm gpt56 research: `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` cross-platform behavior varies by OS, needs centralized resolver
