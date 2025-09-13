# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Sortarr is a Windows Forms application (.NET Framework 4.7.2) that automates media file organization using FileBot. It processes video files from downloads folders and sorts them into organized directory structures for movies and TV shows, supporting both HD and 4K content.

## Build and Development Commands

### Building the Project
```bash
# Build using MSBuild (from Visual Studio Developer Command Prompt)
msbuild Sortarr.sln /p:Configuration=Release
msbuild Sortarr.sln /p:Configuration=Debug

# Or use Visual Studio to build (recommended)
# Open Sortarr.sln in Visual Studio and build via IDE
```

### Running the Application
```bash
# Run normally (GUI mode)
Sortarr\bin\Release\Sortarr.exe

# Run in automated mode (headless)
Sortarr\bin\Release\Sortarr.exe --auto

# Use provided batch script for automated runs
run_sortarr.bat
```

### Package Management
The project uses NuGet packages managed via packages.config. Dependencies are automatically restored when building in Visual Studio.

## Architecture Overview

### Core Components

**Main Application (`Sortarr.cs`)**
- Single-form Windows application with tabbed interface
- Handles both GUI and automated execution modes
- Manages profile persistence and configuration

**Media Processing Pipeline**
1. **File Discovery**: Scans downloads folder for media files (.mp4, .mkv, .avi, .mov, .m4v)
2. **Classification**: Uses regex patterns to detect TV shows (`[sS]\d{2}[eE]\d{2}`) and 4K content (`2160[pP]`)
3. **FileBot Integration**: Executes FileBot AMC script for proper naming/organization
4. **Temporary Staging**: Uses `FilebotMedia/` subfolder structure for intermediate processing
5. **Final Organization**: Moves processed files to configured destination folders
6. **Cleanup**: Removes original files and empty directories

**Configuration System**
- Profile-based settings stored as key-value text files in `profiles/` directory
- Four media categories: HD Movies, 4K Movies, HD TV Shows, 4K TV Shows
- Each category supports up to 5 destination folders
- Override formats for custom FileBot naming patterns

**Automation Features**
- Windows Task Scheduler integration for periodic execution
- HTTP server (localhost:6969) for remote configuration via web interface
- Command-line automation mode with `--auto` flag

### Key Data Structures

**mediaControls Dictionary**: Maps media types to their UI controls
```csharp
Dictionary<string, (CheckBox CheckBox, NumericUpDown UpDown, TextBox[] TextBoxes, Button[] BrowseButtons, Label LocationLabel)>
```

**fileMappings List**: Tracks original to processed file mappings for cleanup
```csharp
List<(string Original, string Renamed)>
```

### External Dependencies

**FileBot Integration**: Requires FileBot executable for media processing
- Uses AMC (Automated Media Center) script
- Executes with custom format strings for movies and TV shows
- Handles both duplicate action and non-strict matching

**Windows Task Scheduler**: Uses Microsoft.Win32.TaskScheduler library (v2.12.1) for automation
- Creates scheduled tasks running as highest privilege
- Supports repetition patterns for periodic execution

## Important Implementation Details

### File Processing Logic
- TV show detection: `[sS]\d{2}[eE]\d{2}` pattern matching
- 4K detection: `2160[pP]` pattern matching
- Duplicate handling: Compares filename without extension across all destination folders
- TV shows matched by base folder name, movies by filename

### Error Handling and Logging
- All operations logged to `filebot_log.txt` with timestamps
- UI updates only in non-automated mode using `BeginInvoke`
- Extensive try-catch blocks with detailed error reporting
- Process validation before FileBot execution

### Profile System
- Text-based configuration files with key=value format
- Supports all media types, folder counts, paths, and advanced options
- Default placeholders ("Default") for unset folder paths
- Automatic profile dropdown population from files

### Remote Configuration
- HTTP listener on port 6969 serving configuration web interface
- HTML form generation with dark theme styling
- POST handling for configuration updates
- Automatic server start/stop based on checkbox state

## Project Structure Notes

- **Single Project Solution**: Contains main application plus installer project (SortarrSetup)
- **Windows Forms Designer**: UI defined in `Sortarr.Designer.cs` (extensive control definitions)
- **Resource Files**: Includes favicon.ico, HTML/JS files for web interface
- **Batch Scripts**: `run_sortarr.bat` for easy automated execution
- **Package Dependencies**: Modern .NET libraries backported to .NET Framework 4.7.2

## Development Considerations

When modifying this codebase:
- Maintain separation between GUI and automated modes using `isAutomated` flag
- Use `BeginInvoke` for all UI updates to handle cross-thread operations
- Preserve FileBot command-line argument structure for compatibility
- Test both profile save/load and web interface configuration paths
- Validate all folder paths before processing to prevent runtime errors