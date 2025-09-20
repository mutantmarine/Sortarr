# Repository Guidelines

## Project Structure & Module Organization
- Sortarr/ hosts the WinForms client, including Sortarr.cs for logic and bundled web assets (index.html, sortarr-web.*).
- Sortarr/Properties/ stores auto-generated designer, settings, and resource files; modify UI via the designer to keep these in sync.
- SortarrSetup/ is the Visual Studio setup project; rebuild the Release configuration when publishing installers.
- packages/ contains NuGet dependencies restored by Visual Studio; avoid editing them manually.

## Build, Test, and Development Commands
- msbuild Sortarr.sln /t:Build /p:Configuration=Debug compiles the application for interactive debugging.
- msbuild Sortarr.sln /t:Build /p:Configuration=Release produces optimized binaries consumed by the setup project.
- Sortarr\run_sortarr.bat launches the latest build with the --auto flag for unattended runs.
- Sortarr\bin\Debug\Sortarr.exe (or ...\Release) starts the UI directly for manual verification.

## Coding Style & Naming Conventions
- Follow 4-space indentation, PascalCase for types and events, and camelCase for locals and parameters.
- Keep using directives alphabetized and separated by blank lines, matching the existing layout.
- Store UI logic in Sortarr.cs, leave layout changes to Sortarr.Designer.cs, and let MSBuild copy new static assets alongside the existing web files.

## Testing Guidelines
- No automated tests exist; document manual coverage before merging.
- For scheduler flows, run Sortarr.exe --auto and confirm an updated ilebot_log.txt plus the expected Task Scheduler entry.
- Smoke-test UI updates by navigating the main form and verifying embedded pages such as web-interface.html render correctly.

## Commit & Pull Request Guidelines
- Continue the observed X.Y.Z release tagging; use imperative, descriptive commit subjects between releases (e.g., Add queue retry dialog).
- Reference related issues, note the manual tests you performed, and attach screenshots or GIFs for UI-facing changes.
- Ensure the Release build succeeds and mention installer verification in the PR description.

## Release & Packaging Notes
- After a Release build, open SortarrSetup and bump the product version to match the new tag before rebuilding.
- Publish the MSI from SortarrSetup\Release with the changelog and any upgrade instructions.
