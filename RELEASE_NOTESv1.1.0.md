🌐 Languages: [English(current)](RELEASE_NOTES%20v1.1.0.md) | [Русский](RELEASE_NOTES%20v1.1.0.ru.md)

# Release Notes - v1.1.0

## S.T.A.L.K.E.R. 2 Mod Manager - Update Release

### Major Changes

#### Design Overhaul
- **UI Redesign** - Major part of the design has been reworked for better user experience
- **Consistent Styling** - Unified hover effects and button styles across all windows
- **Improved Layout** - Better organization of UI elements and controls

#### New Features

##### HerbatasDLCModLoader Integration
- **DLC Mod Loader** - Integrated HerbatasDLCModLoader functionality directly into the application
- **Seamless Integration** - Access DLC mod loader from the Install menu

##### Auto-Update System
- **Automatic Updates** - The application now checks for updates automatically
- **One-Click Installation** - Download and install updates with a single click
- **Progress Tracking** - Visual progress bar and status updates during update process

##### Mod Search
- **Search Functionality** - Added search feature to find mods by name in the mod list
- **Quick Filtering** - Instantly filter mods as you type

##### Path Configuration
- **Simplified Path Setup** - Now you need to specify the game root path instead of the ~mods folder path
- **Automatic Path Resolution** - The application automatically resolves the ~mods folder path from the game root

##### Localization Support
- **Multi-language Support** - Added comprehensive localization support for multiple languages
- **Custom Localization** - If your language is not in the list, download `/localization/localization.json` and add it
- **Custom Localization Loading** - In Settings menu, select "Load Custom Localization" and specify the path to your localization file
- **Currently Supported Languages** - English, Russian, French (with more languages available via custom localization)

##### Mod Management
- **Delete Mods** - Added ability to simply delete files from the ~mods folder
- **Improved Mod Control** - Better control over mod installation and removal

### Technical Improvements

- **Code Organization** - Better separation of concerns and service architecture
- **Window Management** - All child windows now open as part of the main application (no separate taskbar entries)
- **Dependency Management** - Replaced large dependencies with lighter alternatives where possible

### Migration Notes

#### Path Configuration Change
If you're upgrading from v1.0.0:
- **Old Behavior**: You specified the path to `~mods` folder directly
- **New Behavior**: You now specify the path to the game root (e.g., `C:\Games\S.T.A.L.K.E.R. 2 Heart of Chornobyl`)
- The application will automatically resolve the `~mods` path as `Stalker2\Content\Paks\~mods` relative to the game root

### Installation

1. Download the v1.1.0 release from GitHub
2. Extract the application
3. Run `Stalker2ModManager.exe`
4. Update your path configuration if needed (now specify game root instead of ~mods path)
5. Enjoy the new features!

### Usage

See [README.md](README.md) for detailed usage instructions.

