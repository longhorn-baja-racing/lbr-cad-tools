# Longhorn Baja SolidWorks Tools

Custom SolidWorks API macros and C# add-ins built to automate repetitive CAD tasks, enforce rule book standards, and streamline assembly workflows across sub-teams.

## Repository Structure

```text
baja-cad-tools/
|-- src/
|   |-- BajaAddIn/      # Main C# COM Add-in (PropertyManager pages, Task Panes)
|   |-- Macros/         # Standalone .swp and .cs utility scripts
|-- docs/               # Baja SAE rule book specs (Section B.4, B.12) and guides
|-- tests/              # Sample parts and assemblies for testing
|-- README.md
```

## Planned Features

### General and Utilities
- **Batch Exporter:** Export active assemblies or folders into STEP, IGES, DXF, and PDF formats automatically.
- **CSV Point Cloud I/O:** Import coordinates from a CSV file directly into a 2D or 3D sketch, and export existing sketch points back to CSV.
- **Parametric Part Numbering:** Automatically assign standardized part numbers and custom properties across assemblies.
- **Auto-Saver:** Background save utility triggered on timed intervals or specific rebuild events.
- **Fix All Top-Level Components:** One-click macro to lock/fix all floating top-level parts in an assembly.
- **Add-in Trigger Config:** Lightweight config file to enable or disable specific sub-team tools on startup.
- **Surface Copier:** Quick utility to offset/copy selected faces into surface bodies.

### Sub-Team Tools
- **Ergonomics - Mounting Tab Generator:** Generates single-shear or double-shear mounting tabs based on user inputs (tube diameter, bolt size, material thickness). Defaults to Baja SAE rule book dimensions (Sections B.4 and B.12) and places centered clearance holes.
- **Chassis - Ordered Tube Notcher:** Automates weldment tube trimming using a customizable priority hierarchy (for example, keeping floor rails intact while trimming secondary members into them) with per-node overrides.
- **Dynamics - Suspension Geometry Monitor:** Real-time kinematic dashboard inside the SolidWorks Task Pane that calculates and displays live camber, caster, toe, KPI, and steering radius as suspension assemblies articulate.
- **Manufacturing - Auto Drawing Generator:** Experimental tool to generate basic orthographic views and flat-pattern DXFs for sheet metal tabs.

## Development Milestones

### Phase 1: Foundation and Quick Macros
- [x] Set up Visual Studio C# class library project for SolidWorks COM Add-in
- [x] Build CSV Point Cloud Importer and Exporter macros
- [x] Build Fix All Top-Level Components macro
- [x] Build Batch Exporter (STEP, DXF, PDF)

### Phase 2: Assembly Management and Rule Book Automation
- [ ] Implement Parametric Part Numbering via CustomPropertyManager
- [x] Build Ergonomics Mounting Tab Generator (single/double shear + B.4/B.12 presets)
- [x] Add Auto-Saver and Trigger Config file support
- [ ] Build Surface Copier utility

### Phase 3: Complex Geometry and Kinematics
- [ ] Build Chassis Ordered Tube Notcher with priority rules
- [ ] Build Dynamics Suspension Geometry Monitor (live kinematic solver in Task Pane)
- [ ] Prototype Auto Drawing Generator for laser/waterjet parts

## Setup and Installation

1. Clone this repository to your local machine.
2. Open the solution file in Visual Studio.
3. Verify that the SolidWorks API references (`SolidWorks.Interop.sldworks`, `SolidWorks.Interop.swconst`) match your installed SolidWorks version.
4. Build the solution in Release mode to register the COM add-in, or run standalone `.swp` macros via `Tools -> Macro -> Run` inside SolidWorks.

## Settings

The add-in keeps its settings in `%LOCALAPPDATA%\LBR CAD Tools\settings.json`. Open it from the **Settings** button on the LBR CAD Tools tab, which also lists problems in the file and the add-in titles on your computer.

```json
{
  "autoSave": {
    "enabled": true,
    "intervalMinutes": 10
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

## Contributing and Requests

Sub-team leads and members can request new automation tools or report bugs by opening an Issue in this repository. Include a sample part or assembly file and list the exact manual steps you want automated.
