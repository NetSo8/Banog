# Banog — folder automation for Windows

Watches folders and applies rules automatically (sorting, renaming, moving, cleaning) as
soon as a file appears or changes. A self-contained native executable: no runtime to
install, no account, no telemetry.

> **Beta version:** Banog is still under development. The application and its configuration
> format may change significantly before a stable release. Keep a copy of your rules if you
> try a new version.

> Working title. See "Name" below.

## Stack

| | |
|---|---|
| UI | Avalonia 11.3.18, MVVM, compiled XAML, compiled bindings |
| Runtime | .NET 10, Native AOT |
| Target | `win-x64` only (v1) |
| Deployment | self-contained, native executable |

## Structure

```
src/
  Banog.csproj  single application project
  App.Core/     rules engine, JSON-serializable models, business logic
  App.Watcher/  native watching (ReadDirectoryChangesW), debounce, stabilization
  App.UI/       Avalonia views, viewmodels and styles
  App.Host/     entry point, composition and Windows services
tests/
  App.Core.Tests  84 tests (conditions, actions, tokens, serialization, security)
```

The subfolders keep maintenance markers and clear namespaces, but they are all compiled by
the same `Banog.csproj`. There is a single application executable to build; the tests stay
separate and run entirely in memory.

## Build and run

```bash
dotnet test
```

```bash
dotnet run --project src/Banog.csproj
```

## Publish the native executable

Native AOT linking needs `link.exe`: install the **Desktop development with C++**
workload of Visual Studio (MSVC x64 + Windows SDK), then:

```bash
publish.cmd
```

The script initializes the MSVC environment and then calls `dotnet publish`. Output goes
to `publish/`.

## Releases

Downloadable releases are built entirely by GitHub Actions — no local build needed. The
[release workflow](.github/workflows/release.yml) runs on the `windows-latest` runner
(which already carries the MSVC C++ tools required by Native AOT and Chocolatey), then:

1. runs the test suite;
2. publishes the Native AOT executable;
3. packages an **Inno Setup installer** ([`installer/installer.iss`](installer/installer.iss)) and a **portable ZIP**;
4. writes a `SHA256SUMS.txt` checksum file;
5. creates a GitHub release with all three attached.

Pushing a tag triggers it:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The version is taken from the tag (the leading `v` is stripped). The workflow can also be
run manually from the **Actions** tab with an explicit version (e.g. `0.1.0`); a tag is
then created from it, `v` prefix included.

Installation is per-user (no administrator rights): the app installs to
`%LOCALAPPDATA%\Banog`, and its configuration stays in `%APPDATA%\Banog` — uninstalling
removes the program but not your rules file.

### A note on "single-file"

Native AOT produces `Banog.exe` (~23 MB) that embeds all the managed code and the runtime:
nothing to install on the user side, which is the goal. Avalonia, however, loads its
rendering engine from native libraries that cannot be merged into the AOT image:

```
Banog.exe  libSkiaSharp.dll  libHarfBuzzSharp.dll  av_libglesv2.dll
```

A literal "one file" would require embedding these DLLs as resources and extracting them
at startup — which reintroduces a disk extraction at launch, exactly the cost AOT was meant
to remove. Four files in one folder remain compatible with direct-download distribution
(ZIP, or a one-click installer).

## Rules file format

File: `%APPDATA%\Banog\rules.json`, written atomically.

```json
{
  "schemaVersion": 1,
  "debounceMilliseconds": 750,
  "theme": "System",
  "folders": [
    { "id": "…", "path": "C:\\Users\\me\\Downloads", "includeSubfolders": false, "enabled": true }
  ],
  "rules": [
    {
      "name": "Invoices",
      "match": "All",
      "conditions": [
        { "type": "extension", "match": "IsOneOf", "extensions": ["pdf"] },
        { "type": "name", "mode": "Contains", "value": "invoice" }
      ],
      "actions": [
        { "type": "rename", "template": "{created:yyyy-MM-dd}_{name}.{ext}" },
        { "type": "move", "destination": "D:\\Accounting\\{created:yyyy}" }
      ]
    }
  ]
}
```

### Conditions v1

`extension` · `name` (contains / starts with / ends with / equals / regex) · `date`
(creation or modification, older than / newer than / before / after) · `size` ·
`sourceFolder` · `group` (nested AND/OR).

Every condition accepts `"negate": true`.

### Actions v1

`move` · `copy` · `rename` · `recycle` (recycle bin) · `delete` · `runCommand`.

### Tokens

Usable in rename templates and destination paths.

| Token | French alias | Result |
|---|---|---|
| `{name}` | `{nom}` | name without extension |
| `{filename}` | `{fichier}` | name with extension |
| `{ext}` | `{extension}` | extension without the dot |
| `{folder}` | `{dossier}` | name of the parent folder |
| `{path}` | `{chemin}` | full path |
| `{counter:000}` | `{compteur:000}` | disambiguation counter |
| `{created:F}` | `{date:F}`, `{creation:F}` | creation date, .NET format `F` (default `yyyy-MM-dd`), local time |
| `{modified:F}` | `{modification:F}` | last modification date |
| `{now:F}` | `{aujourdhui:F}` | current date |

Both spellings are valid and will remain so. A French UI that forced writing `{name}` would
only be superficially simple — but existing rule files must keep working all the same.
Matching is case-insensitive. `{{` and `}}` produce a literal brace.

## Appearance

Three modes, selectable in **Settings → Appearance**: **Windows** (default), **light**,
**dark**. The choice is persisted in `theme` and applied immediately, without restarting.

In Windows mode the application neither reads nor watches the registry: it leaves its
variant on `ThemeVariant.Default`, and Avalonia repaints itself when the user switches the
system setting while the app is open.

A single palette, two values per key
([`Themes/Palette.axaml`](src/App.UI/Themes/Palette.axaml)); the styles
([`Themes/Theme.axaml`](src/App.UI/Themes/Theme.axaml)) only know the keys and are written
once. Every color goes through `DynamicResource` — with `StaticResource`, the hot switch
would repaint nothing.

The accent is darkened in light mode (`#0B72AE` instead of `#4CC2FF`): the dark-theme blue
falls below the contrast threshold on a white background.

## Background

Banog watches without a window. Closing the window does not quit: watching continues
under the notification-area icon, which opens the window, starts or pauses watching, and
really quits. The tray is the only face of the application when it runs in the background —
no view or viewmodel is constructed until the window is requested.

Only one instance runs at a time: two processes watching the same folder would process
every file twice. A second launch without options therefore asks the first instance to
reopen its window, then exits.

`Banog.exe --background` (alias `--daemon`) starts without a window and starts watching
immediately. This is how the startup entry launches Banog.

Starting with Windows is mandatory: the user `Run` key points to the current executable
with `--background`. It is realigned at every launch on the running executable, which
follows a moved installation.

The tray icon is not embedded: a blue square bearing a white folder, drawn pixel by pixel
at startup.

## Interface design decisions

The tool targets people drowning in their downloads, not developers. Four rules guide the
interface.

**One space, one intent.** A sidebar separates three spaces, and only one is visible at a
time: **Monitoring** (what is running, the rules in place, what happened), **Rules** (the
watched folders and rule authoring), **Settings** (appearance, detection, file location).
Looking is not editing: monitoring shows no editable field, only an "Edit" button that
switches to the editing space on the right rule. What applies to the whole application —
the running/paused state and "Organize now" — lives in the sidebar and nowhere else. Rules
and flowcharts are saved automatically after a short typing pause, even when incomplete;
the "Save now" button remains available to force a write.

**No implementation vocabulary on screen.** Model enum values (`IsOneOf`, `BaseName`,
`Any`, `GreaterThan`) stay stable in the JSON, but are never displayed as-is: a single
converter ([Labels.cs](src/App.UI/Localization/Labels.cs)) translates them, wired to all
selectors by a single style class.

**A rule reads like a sentence, not a form.** "IF *all* of these conditions are met: the
name — contains — invoice". Each editor carries its own linking words; the "invert" check
box became "unless". The rule list shows a generated summary ("the type is pdf and the
name contains « invoice » → move to D:\Accounting"), so you know what a rule does without
opening it.

**Nothing runs that you could not see beforehand.** The "Try on a file…" button takes a
real file, says whether the rule would apply and describes the result — final name,
destination folder — without touching the disk. This was the most costly gap: writing a
delete rule used to require testing it on real files. An irreversible action (delete
without recycle bin) additionally frames its card in red and announces it.

The rest comes down to details that avoid the blank page: every empty list explains the
next action instead of showing an empty frame, the columns are numbered (folders, then
rules), paths are chosen with "Browse…" rather than typed, and the status bar — shared by
the three spaces — says what to do while nothing is configured.

The monitoring space quantifies the ongoing exercise: files organized, errors, active
rules, watched folders, and a per-rule counter. A rule that never processed anything is
almost always a writing error; without that counter, nothing distinguishes it from a
working rule. These counters last for the session and are not persisted. The log shows one
line per triggered rule, with the actions chained in the message (« invoice.pdf —
Invoices: moved to D:\Accounting, then renamed to 2026-08-05_invoice.pdf »), rather than
one line per action.

## Threat model

The trust boundary lies between two things that would be easy to confuse:

- **a rule's template** is written by the user — trusted data;
- **the name of the processed file** is not. It comes from whatever someone dropped in
  the watched folder: a download, an email attachment, a network share.

Yet tokens inject the second into the first. Everything below protects that junction. The
corresponding tests live in
[SecurityTests.cs](tests/App.Core.Tests/SecurityTests.cs).

**Command-line argument injection.** `&`, `^`, `|` and spaces are valid characters in a
Windows file name. Concatenated into a command line, they would chain a second command.
The arguments template is therefore split **before** token expansion, and the arguments
are handed to the process one by one via `ArgumentList` — never concatenated. A token
value cannot create an extra argument, whatever its content. `UseShellExecute` stays
`false`: no file associations, no shell verbs.

> Pointing `Executable` at `cmd.exe` or `powershell.exe` reintroduces an interpreter that
> does its own parsing of `/c`. That is an explicit user choice, not a default of the
> engine — but it voids the protection above.

**Path escape.** Token values are constrained to a single segment in any path context
(`TokenScope.Path` / `FileName`): separators, colons and wildcards are neutralized, `.`
and `..` replaced. Without this, `{path}` in a destination produced a rooted path, and
`Path.Join` would simply have overwritten the target folder. Separators written literally
in the template, on the other hand, stay intact. A destination that expands to a relative
path is rejected: it would depend on the process's current directory.

**Junctions and symbolic links.** Rescans of a folder exclude reparse points
(`FileAttributes.ReparsePoint`). Without this, a junction dropped into a watched folder
would take the traversal out of the tree — as far as `C:\Windows` — and a circular link
would make it spin forever.

**Regular-expression denial of service.** Patterns are evaluated with a maximum timeout
of 250 ms, and an invalid pattern does not match instead of breaking the rule. A file
name built to blow up a pattern like `^(a+)+$` therefore cannot stall the engine.

**Memory bounds.** The stabilization queue is capped (100,000 entries) and the processing
channel is bounded (50,000). A folder dumping millions of entries slows the producer down
instead of making the process grow without limit.

### Accepted risks, not fixed

- **The rules file is code execution.** A `runCommand` rule runs with the user's rights.
  `%APPDATA%` is protected by the default ACLs; anyone who can write there can already do
  far worse. No configuration signing is planned.
- **The dry run covers one file at a time.** "Try on a file…" covers a rule you are
  writing, but there is no global preview of the "here are the 300 files this rule would
  move if I enabled it" kind.
- **TOCTOU on the destination.** Between the existence check and the move, a third party
  can create the target. Under `Rename` or `Skip` policy the operation fails cleanly; under
  `Overwrite` it overwrites — which is the requested behavior.

## Performance

What the engine does per file is not meant to be measurable next to the watcher's disk
I/O. The goal was therefore not raw speed but **GC pressure**: a resident utility chewing
through a 100,000-file folder must not trigger dozens of collections.

Measurements on 200,000 files, one rule, four conditions
(`GC.GetAllocatedBytesForCurrentThread`, Release):

| | before | after |
|---|---|---|
| Extension test alone | 96 B/file | **0 B** |
| Path component extraction | 213 B/file | **80 B** (the `FileContext` object itself) |
| Token expansion | 868 B/file, 13 gen0 GCs | **96 B, 1 gen0 GC** |

How:

- **`FileContext` splits the path once** at construction and exposes name, base,
  extension and folder as `ReadOnlySpan<char>`. Exposing them as `string` allocated on
  *every property read*, i.e. every condition on every file — and `Extension` added a
  `ToLowerInvariant` on top.
- **`ValueStringBuilder`** composes names and paths in a 320-character `stackalloc` buffer,
  falling back to `ArrayPool` on overflow. The common case allocates only the final
  string. Numbers and dates are written via `TryFormat` straight into the buffer.
- **Token dispatch by `switch` on span** against literals: the compiler turns it into a
  jump by length then by character. The chain of case-insensitive comparisons only serves
  the non-canonical spellings.
- **One `Regex` per condition**, kept in a weak-key table. The static `Regex` overloads go
  through a global cache — pattern hashing included — on every call.
- **O(1) rule resolution.** The controller indexes rules by watched folder and
  pre-sorts them when the configuration loads. Before, every file triggered a scan of the
  folders **then** a `Contains` over the list of rule identifiers, i.e. an
  O(folders + rules × identifiers) cost per event.
- **The engine no longer sorts per file**: it checks in O(n) that the rules are already
  ordered and only sorts if they are not — which never happens in production.

What was **rejected** after measurement:

- **Hash table for the extension list.** A rule has fewer than ten of them; the linear
  scan over spans wins (no case-insensitive hashing, no indirection, everything fits in
  cache lines) and allocates nothing.
- **Time gain on token expansion.** Allocations drop by a factor of nine, but the measured
  time stays at parity — the test machine is too noisy to claim better, and this path is
  only taken by matching files anyway.

## Architecture choices

**Native AOT from the first commit, not bolted on later.** The trimming/AOT analyzers are
active on the whole solution via `Directory.Build.props`. Consequences accepted in the
code: compiled XAML and bindings, source-generated JSON, P/Invoke via `LibraryImport`,
XAML converters exposed as statics rather than instantiated by reflection, interpreted
regex. The solution compiles today without a single IL2xxx/IL3xxx warning.

**JSON polymorphism by registry, not by `[JsonDerivedType]`.** The attributes would have
frozen the list of derived types into the business core. `RuleTypeRegistry` maps a
discriminant to a `JsonTypeInfo`: a future module (content conditions, OCR, AI
classification) registers its own types with its own generated context, without
recompiling the core or invalidating existing rule files. An unknown discriminant raises
an explicit error rather than being silently ignored — a file written by a later version
must never be loaded truncated.

**Condition evaluation is async although all v1 conditions are sync.** This is
deliberate: a content condition (reading, OCR, local LLM call) will be added as one more
`IConditionEvaluator`, without changing a signature or touching the engine.

**Event-driven watching, never polling.** `ReadDirectoryChangesW` as a blocking call on a
dedicated thread per folder, cancelled via `CancelIoEx`. A kernel buffer overflow is
detected and triggers a folder rescan, rather than losing files in silence.

**Debounce + stabilization.** Copying a file produces a burst of events. The
`FileStabilizer` coalesces by path, waits for a quiet period, then verifies that the file
is actually openable and that its size has stopped moving — otherwise an in-progress
download would be processed.

**Serialized processing.** Stabilized files go through a `Channel` with a single
consumer: two rules cannot manipulate the same file simultaneously.

## Out of scope for v1

No OCR or content reading, no tags, no cloud integration, no cross-platform. The
architecture is designed to welcome them without a rewrite; it does not anticipate them
with dead code.

## Known limitations

- The UI does not expose editing of nested condition groups. The format supports them and
  the engine evaluates them (`ConditionGroup`); the v1 editor sticks to AND/OR at the rule
  level.
- The rule → folder association exists in the model (`WatchedFolder.RuleIds`) but the
  editor does not offer it yet: all rules apply to all watched folders.
- The dry run takes one file at a time: no preview yet of a rule's effect on an entire
  folder.

## Name

`Banog` is the working name of the project. Candidates consistent with a
minimalist/cyberpunk identity and the idea of silent automation: **Quiet**, **Undertow**,
**Nocturne**, **Silt**, **Drift**. To be decided before the first release — the name is
present in `AssemblyName`, the `%APPDATA%` path, and the manifest.
