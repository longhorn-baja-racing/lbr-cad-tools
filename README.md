<p align="center"><img src="docs/assets/banner.svg" alt="LBR CAD Tools - SolidWorks add-in for Longhorn Baja Racing, The University of Texas at Austin" width="880"></p>

# LBR CAD Tools

A SolidWorks add-in for Longhorn Baja Racing that automates repetitive CAD tasks, checks Baja SAE rule book values, and streamlines assembly work across sub-teams. Everything shows up on the **LBR CAD Tools** tab in SolidWorks.

## Tools

The tab is split into sections, each with its own icon color. The same tools are also in the **LBR CAD Tools task pane** (the LBR icon on the right side of the SolidWorks window), with a heading and a short explanation for each section and tool. Click a tool there to run it, or its **?** to read how it works. Tools that do not apply to the open document are grayed out and say what they need.

**Tool Guide** (in Help and Setup) explains every tool in SolidWorks itself: what it does, what to open first, step-by-step use, and tips. Hovering over a button on the tab shows a one-line description.

### Vehicle Design
| Button | What it does |
|---|---|
| **Mounting Tab** (Ergonomics) | Makes a single or double shear welded tab part from the Baja SAE B.12.2 table (lap belt, anti-sub belt, seat, fire extinguisher, fuel tank): bolt size, close-fit centered hole, thickness, tube OD as the double shear spacing, optional base contoured to the tube. Checks the rules live. See `docs/rules-reference.md`. |
| **Suspension Check** (Dynamics) | Reads reference points named `HP_<point>_<corner>` (for example `HP_UBJ_FL`: UBJ, LBJ, WC = wheel center, SA = a point on the spin axis inboard of WC) and reports camber, toe, caster, KPI, scrub radius, and trail per corner, plus Ackermann % and turning radius when the fronts are steered (needs `HP_WC_RL` or `HP_WC_RR` for the rear axle). Move or steer the assembly and run it again. |

### Sketch Points
| Button | What it does |
|---|---|
| **Import Points** | Reads X, Y, Z rows from a CSV file into the open sketch, or into a new 3D sketch. Header, units (mm / in / m), and extra columns are handled. |
| **Export Points** | Writes the points of the open or selected sketch to CSV in model coordinates. |

### Assembly
| Button | What it does |
|---|---|
| **Fix All / Float All** | Fixes or floats every top-level component of the active assembly in one click. Subassembly files are not touched. |
| **Restore Fix States** | Undoes the last Fix All or Float All (Ctrl+Z cannot), including a mixed starting state. |

### Files
| Button | What it does |
|---|---|
| **Batch Export** | Exports the active part, assembly (plus every unique component and their drawings), or drawing to STEP AP214, IGES, Parasolid, STL, PDF, DXF, and sheet metal flat pattern DXF. Files are named by part number. For large unattended folder jobs, the built-in SolidWorks Task Scheduler "Export Files" task also works. |
| **Auto-Save** | Saves modified documents on a timer (SolidWorks Auto-recover only writes recovery copies). Skips never-saved and read-only files, and waits while a command or sketch is open. |

### Help and Setup
| Button | What it does |
|---|---|
| **Tool Guide** | Every tool, by section: what it does, what it needs, numbered steps, and tips. Open a tool straight from its page. |
| **Settings** | Hide buttons you do not use, and load or unload other add-ins at startup. See [Settings](#settings). |
| **About** | Version, credits, the GitHub link, and the log folder to attach to a bug report. |

Already built into SolidWorks, so not planned: Hide All Sketches, Merge Sketches.

## Coming up

| Tool | Status |
|---|---|
| Parametric Part Numbering | Needs the team's numbering scheme (#16) |
| Chassis: Ordered Tube Notcher | Needs the frame modeling method (#17) |
| Dynamics: live Suspension Monitor | Suspension Check in a Task Pane that updates while you drag (#12); point names to be agreed with Dynamics |
| Surface Copier | Waiting to hear what Offset Surface at 0 distance is missing (#10) |
| Manufacturing: Auto Drawing Generator | Flat pattern DXFs are in Batch Export; drawings are a stretch goal (#13) |

See `docs/PLAN.md` for the API approach per feature, and the GitHub issues for details and decisions.

## Repository Structure

```text
lbr-cad-tools/
|-- src/
|   |-- BajaAddIn/        # The SolidWorks add-in (.NET Framework 4.8): commands, dialogs, SolidWorks calls
|   |-- BajaAddIn.Core/   # SolidWorks-free logic (CSV, naming, rules, geometry, settings), unit tested
|   |-- Setup/            # LBR-CAD-Tools-Setup.exe: one-file installer with the add-in embedded
|-- tests/                # Unit tests for BajaAddIn.Core (run in CI)
|-- tools/SwSmokeTest/    # Drives a real SolidWorks to test the add-in's feature code (Windows only)
|-- tools/IconGen/        # Build helper: draws the Setup icon from src/BajaAddIn/Util/Brand.cs
|-- scripts/              # register.ps1 / unregister.ps1 (developers), release.ps1 (build + publish)
|-- docs/
|   |-- PLAN.md             # Architecture, API approach, per-feature notes, open questions
|   |-- rules-reference.md  # Baja SAE rule values used by the tools (B.12.2 tab table, etc.)
```

## Install (team members)

1. Close SolidWorks.
2. Download `LBR-CAD-Tools-Setup-<version>.exe` from the [latest release](https://github.com/longhorn-baja-racing/lbr-cad-tools/releases/latest) and run it. Click Yes when Windows asks for permission. If Windows shows "Windows protected your PC", click **More info**, then **Run anyway**.
3. Click **Install**, then start SolidWorks: the **LBR CAD Tools** tab appears in parts, assemblies, and drawings.

To update, run the newer Setup and click **Update** (settings are kept). To remove, use Windows Settings > Apps > LBR CAD Tools > Uninstall.

## Requirements

- Windows with SolidWorks installed (built and tested on SolidWorks 2026)
- To build: .NET SDK 8 or newer and Visual Studio 2022 (optional). The add-in targets .NET Framework 4.8, which SolidWorks requires.
- Administrator rights to register the add-in

## Building from source

1. Clone this repository and close SolidWorks.
2. Build: `dotnet build` (or open `LbrCadTools.sln` in Visual Studio 2022). The SolidWorks interop DLLs are taken from your SolidWorks install; set `SwInteropDir` if SolidWorks is not in the default folder.
3. Register from an Administrator PowerShell: `.\scripts\register.ps1` (once; rebuilds update in place).
4. Start SolidWorks. The **LBR CAD Tools** tab appears in parts, assemblies, and drawings.
5. To remove it: `.\scripts\unregister.ps1`.

If Windows refuses to run the scripts, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once. If the tab does not show up, read `%LOCALAPPDATA%\LBR CAD Tools\BajaAddIn.log`.

Releases: bump `<Version>` in `Directory.Build.props` (in a PR), then on an up-to-date `main` run `.\scripts\release.ps1`. It builds `LBR-CAD-Tools-Setup.exe`, tags `v<version>`, and publishes the GitHub Release with release notes. `-DryRun` only builds into `dist\`. GitHub Actions cannot build it, because the build needs the SolidWorks interop assemblies from a local SolidWorks install. Setup.exe embeds the ones the add-in was built against, since the add-in binds to those exact versions.

Tests: `dotnet test LbrCadTools.CI.slnf` runs the unit tests (also in CI). With SolidWorks closed, `tools\SwSmokeTest\bin\Debug\net48\SwSmokeTest.exe` runs the SolidWorks checks.

## Settings

The add-in keeps its settings in `%LOCALAPPDATA%\LBR CAD Tools\settings.json`. Open it from the **Settings** button on the LBR CAD Tools tab, which also lists problems in the file and the add-in titles on your computer.

```json
{
  "autoSave": {
    "enabled": true,
    "intervalMinutes": 10
  },
  "suspension": {
    "forwardAxis": "+Z",
    "upAxis": "+Y",
    "tireRadiusInches": 11.5
  },
  "commands": {
    "hidden": ["Import Points", "Export Points"]
  },
  "addIns": {
    "load": ["Presentation Manager"],
    "unload": ["3DEXPERIENCE Marketplace"]
  }
}
```

- `commands.hidden`: buttons to leave off the tab. Takes effect at the next SolidWorks start. Settings cannot be hidden.
- `addIns.load` / `addIns.unload`: other add-ins to load or unload when SolidWorks starts, by title (as in Tools -> Add-Ins) or by DLL path (write `\\` for each `\`).
- `autoSave`: also set by the **Auto-Save** button.
- `suspension`: which model axes point forward and up (default: the car's nose shows in the Front view, Y up), and the loaded tire radius used for the contact patch.

## Contributing and Requests

Sub-team leads and members can request new automation tools or report bugs by opening an Issue in this repository. Include a sample part or assembly file and list the exact manual steps you want automated.

---

<p align="center"><img src="docs/assets/monstera.svg" alt="Monstera leaf" width="28"><br><sub>Built by Cyrus for Longhorn Baja Racing, The University of Texas at Austin.</sub></p>
