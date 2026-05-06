# Changelog — DLSS Swapper+

All notable changes to DLSS Swapper+ will be documented in this file.

DLSS Swapper+ is a fork of [DLSS Swapper](https://github.com/beeradmoern/dlss-swapper) v1.2.4, licensed under GPL-3.0. All original credit goes to the DLSS Swapper team and contributors.

## [1.0.2] - 2026-05-06

### Changed
- App title bar now shows "DLSS Swapper+" to differentiate from upstream
- About section in Settings now shows combined version (upstream / fork) and removed build date
- Disabled auto update check on startup (pointed to upstream repo)
- Removed "Check for updates" button from Settings

### Fixed
- DLSS and Streamline backups now preserve original game files — re-running a batch update no longer overwrites `.dlsss` backups, ensuring Restore always returns to the true originals. The deploy summary confirms how many backups were preserved per DLSS type.

## [1.0.1] - 2026-04-28

### Added
- Batch Deploy feature — new "Select Games" button on the Games page toolbar opens a split-layout dialog for multi-game DLL management
- Game selection panel with scrollable checklist, Select All, and Deselect All controls
- DLL version pickers for all 9 DLL types (DLSS, DLSS Ray Reconstruction, DLSS Frame Generation, FSR 3.1 DirectX 12, FSR 3.1 Vulkan, XeSS, XeSS Frame Generation, XeSS DX11, XeLL) in a two-column layout
- Streamline update toggle for batch Streamline SDK deployment
- Deploy button applies selected DLL versions across all checked games sequentially with progress reporting
- Restore All button reverts DLLs to their backed-up originals across all checked games in one operation
- Hidden game filter toggle — eye icon button filters out hidden games by default, toggle off to include them
- Summary dialog on completion showing per-DLL-type success/skip/failure counts and any errors encountered
- Error resilience — a failure on one game does not block processing of remaining games
- Admin recommendation in summary when file access errors suggest elevated permissions would help
- Help text reminding users to download DLL versions from the Library page before they appear in the pickers

### Fixed
- Streamline SDK staging now extracts production DLLs from `bin/x64/`, excluding the `development/` subfolder which contains larger debug builds
- Games running Streamline 1.x (e.g. The Witcher 3) are automatically skipped during batch Streamline updates — 1.x is not compatible with 2.x staged DLLs

## [1.0.0] - 2026-04-27

### Added
- Streamline SDK update and restore support for individual games
- Update Streamline button in the game detail dialog applies staged Streamline SDK DLLs to a game's existing Streamline files
- Restore Streamline button reverts to the backed-up originals
- Automatic backup creation before overwriting Streamline DLLs (`.dlsss` backup files)
- Rollback on failure — if any file copy fails mid-update, already-replaced files are restored from backups
- Intersection-based updating — only Streamline DLLs that exist in both the game and the staging area are touched
- Game history entries recorded for each Streamline swap operation
- Database persistence for updated game assets and backup records

### Base
- Forked from DLSS Swapper v1.2.4
