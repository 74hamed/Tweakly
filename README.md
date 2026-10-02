# Tweakly

**Portable, open-source Windows tuning. One option at a time.**

Tweakly is a bilingual English/Persian WPF desktop app for Windows 10 22H2 and Windows 11 x64. Open one EXE, explore a category, and explicitly apply a selected option. Starting the app does not change system settings. There is no optimize-all button.

## Features

- 90 individual options across Windows, processor, graphics, power, memory, storage, network, input, debloat, cleanup, extras and recovery.
- Hardware-aware options, visible technical changes and current-value inspection.
- Protected original-value journals, verified results and exact Undo for supported settings.
- Elevation only when needed, through the same EXE. No server, account, background service, telemetry or updater.
- Original artwork and power plan; independent implementation rather than a wrapped third-party batch script.

The [feature coverage table](docs/FEATURE-COVERAGE.md) documents equivalences and intentional corrections. Read [limitations](docs/LIMITATIONS.md) before public distribution.

## Build

Install the .NET 10 SDK on Windows. No third-party NuGet libraries are required.

```powershell
dotnet build Tweakly.csproj -c Release
dotnet publish Tweakly.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish/Tweakly.exe` is the only distributable file. The .NET desktop runtime and app resources are embedded. Native runtime files may be extracted to the current account's temporary directory while the app runs. Personal journals and preferences are written to Windows data directories, not alongside the EXE.

## Tests

```powershell
& .\publish\Tweakly.exe --self-test test-report.json
& .\publish\Tweakly.exe --qa qa-images
& .\publish\Tweakly.exe --inspect inspection.json
```

`--self-test` uses an in-memory backend and never writes live Windows settings. `--qa` renders the actual bilingual WPF UI with all action handlers disabled. These checks do not replace disposable-machine integration tests. Follow [the Windows validation matrix](docs/VALIDATION.md) before calling a release production-tested.

`--inspect` reads representative registry, power, BCD, network, task, memory and driver providers without applying changes. `--benchmark report.json` records process-start-to-first-render time and memory, then closes the read-only window. Diagnostic reports are local data and should be reviewed before sharing.

## Add an option

Add a bilingual definition to `Data/catalog.json`. Use existing typed operation providers when possible. Add a small handler only for a genuinely new operation. Record its original state before writes, verify the result, and document whether exact Undo is available. Keep changes local and easy to review; no generic script execution or plugin framework.

## Data

- `%ProgramData%\Tweakly\History`: machine-side, administrator-controlled journals, readable by users. The UI shows only the initiating account's records. Records can include local paths and package inventories; do not share them without review.
- `%LocalAppData%\Tweakly`: per-account language preference.
- `%LocalAppData%\Tweakly\Activity`: UI-only results, including UAC cancellation and external-tool launches. These records are never trusted by the administrator worker for restoration.
- Changing Windows settings requires the initiating account's own administrator token. Switching to another account in UAC is intentionally unsupported.

## Releases

The GitHub Actions workflow builds and tests on Windows. A `v*` tag produces the single EXE as an artifact. Uploading/tagging a repository does not silently create a public GitHub Release. Initial builds are unsigned.

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## فارسی

**Tweakly ابزار پرتابل و متن‌باز تنظیم ویندوز است؛ گزینه‌ها دانه‌دانه اجرا می‌شوند.**

یک فایل EXE را باز کن، دستهٔ موردنظر را انتخاب کن و فقط گزینهٔ دلخواهت را اجرا کن. بازشدن اپ تنظیمات ویندوز را تغییر نمی‌دهد. رابط فارسی و انگلیسی، ثبت وضعیت قبلی، بررسی نتیجه و بازگردانی تنظیمات پشتیبانی‌شده فراهم شده‌اند.

برای ساخت، SDK نسخهٔ ۱۰ از .NET را نصب و دستورهای بالا را در ویندوز اجرا کن. امکانات اصلی آفلاین هستند؛ تست‌های اینترنت، نصب مجدد برنامه و بعضی ابزارهای ویندوز نیازمندی‌های خودشان را دارند.

این نسخهٔ اولیه باید پیش از انتشار عمومی روی سیستم‌های آزمایشی بررسی شود. تنظیمات حساس امنیت، بوت و درایور افزایش کارایی تضمین‌شده ندارند. محدودیت‌ها و پوشش امکانات در پوشهٔ `docs` مستند شده‌اند.
