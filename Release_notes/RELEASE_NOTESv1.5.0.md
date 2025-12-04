🌐 Languages: [English(current)](RELEASE_NOTESv1.5.0.md) | [Русский](RELEASE_NOTESv1.5.0.ru.md)

# Release Notes - v1.5.0

## S.T.A.L.K.E.R. 2 Mod Manager - Multi‑version Mods & File Warnings

### Major Changes

#### Multi‑Version Mod Handling
- **Grouped Mods by Base Name** – Mods with the same base name (different folders/versions) are now grouped into a single entry in the main list.
- **Primary Version Display** – Only one row is shown in the main list for a group; the displayed name depends on enabled versions:
  - **0 enabled versions**: shows only the base mod name.
  - **1 enabled version**: shows base name plus selected version in parentheses, e.g. `Quests (Quests - v1.0.1)`.
  - **>1 enabled versions**: shows base name with a note that multiple versions are selected.
- **Multiple Versions Indicator** – A dedicated icon now indicates that multiple versions of a mod are available and/or enabled.

#### Per‑Version File Management Enhancements
- **Version List in Mod Files Window** – The Mod Files window now shows a list of all versions of the selected mod (when multiple folders share the same base name).
- **Per‑Version Enable/Disable** – Each version has its own checkbox:
  - Enabling a version enables all its files.
  - Disabling a version disables all its files.
- **Per‑File Control per Version** – File checkboxes still work per version; if all files for a version are disabled, that version is automatically turned off.

#### Improved Warning & Empty Mod Handling
- **Global Disabled Files Warning** – The warning icon (⚠) in the main list is now based on **all enabled versions** of a mod:
  - Shown only when at least one enabled version has disabled files.
  - Tooltip text is localized via `SomeFilesDisabled`.
- **Empty Mod Folders** – Mods whose folders contain **no files at all** now:
  - Are **not enabled by default** on load from the Vortex directory.
  - Show a warning icon with a localized tooltip `NoFilesFound` (“No files found in this mod folder”).
  - Cannot be enabled for installation from the main list (the checkbox will not turn on).

#### Installation Safety
- **Install Blocking for Invalid Mods** – A mod cannot be enabled for installation if:
  - It has no files, or
  - All of its files (for all versions) are disabled.
- **Consistent State Between Windows** – Changes made in the Mod Files window (per‑file and per‑version checkboxes) are reflected in the main list’s enable state and warning indicators.

### Technical Improvements

- **`ModInfo` Model Extensions**
  - Added properties: `DisplayName`, `IsPrimaryVersion`, `HasMultipleVersions`, `MultipleVersionsTooltip`, `InstalledVersionsCount`, `HasAnyFiles`, `HasAnyEnabledFiles`, `GroupIsEnabled`, `DisabledFilesTooltip`, `AggregatedHasDisabledFiles`.
  - `IsEnabled` now guards against enabling mods without any installable files.
- **`ModManagerService` Improvements**
  - While loading mods from the Vortex path, the service scans folders recursively to detect whether they contain any files.
  - Empty mods are loaded but marked as disabled and flagged via `HasAnyFiles = false`.
- **UI Updates**
  - The main mods list now binds to `DisplayName`, `GroupIsEnabled`, `HasMultipleVersions`, `HasMultipleInstalledVersions`, `DisabledFilesTooltip`, and `AggregatedHasDisabledFiles`.
  - Added `BooleanToBrushConverter` to visually distinguish multi‑version warnings.
  - The Mod Files window was updated with a versions list, shared styles, and proper localization of the title and buttons.
- **Localization Updates**
  - Added/extended keys: `MultipleVersionsAvailable`, `SomeFilesDisabled`, `NoFilesFound` for EN/RU/FR.

### Usage Notes

- **To manage multiple versions of a mod**:
  - Open the Mod Files window and use the versions list to switch between different folders/versions.
  - Use the version checkbox to quickly enable/disable all files for that version.
- **To understand warnings in the main list**:
  - ⚠ + “Some files are disabled” – at least one file is disabled in at least one enabled version.
  - ⚠ + “No files found in this mod folder” – the mod’s folder(s) contain no files at all.
- **To ensure a mod is installed**:
  - At least one version of the mod must be enabled **and** have at least one enabled file.

See [README.md](README.md) for detailed usage instructions.


