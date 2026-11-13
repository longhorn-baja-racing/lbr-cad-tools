# Development Plan

Architecture decisions and per-feature implementation notes. API names are from
the SolidWorks 2025/2026 API help (help.solidworks.com) unless noted.

## 1. Platform and stack

| Decision | Choice | Reason |
|---|---|---|
| Runtime | .NET Framework 4.8 | SolidWorks 2025/2026 in-process add-ins are still .NET Framework. No official .NET 6/8 support has been announced. |
| Language | C# | Matches the official templates and most examples (codestack.net). |
| Project format | SDK-style `.csproj`, `TargetFramework=net48` | Builds with `dotnet build` on Linux/macOS/Windows via `Microsoft.NETFramework.ReferenceAssemblies`. |
| Interop references | `SolidWorks.Interop.sldworks`, `swconst`, `swpublished` as plain assembly references, `EmbedInteropTypes=false` | Do not use `COMReference` items: they fail under the dotnet SDK (MSB4803). See open question 1 for the source of the DLLs. |
| UI | PropertyManager pages for input forms, Task Pane for dashboards, CommandManager tab for buttons | All current in the 2026 API. WinForms first; WPF only if needed, hosted in `ElementHost`. |
| Wrapper library | None at first (raw interop plus a small internal helper layer) | Every official example and codestack recipe uses raw interop, which makes it easier for new members to learn. xCAD.NET (MIT, 0.8.3, supports SW 2026) is the fallback if the boilerplate gets heavy. |
| Macros | VBA `.swp` for tiny one-offs only | New tools go in the add-in so they share UI, settings, and versioning. |

### Registration (Windows only)

- The add-in class implements `ISwAddin` and carries `[ComVisible(true)]` and `[Guid("...")]`.
- A `[ComRegisterFunction]` writes the following keys:
  - `HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}`: Default=1, Title, Description.
  - `HKCU\Software\SolidWorks\AddInsStartup\{GUID}`: 1 to load at startup.
- A matching `[ComUnregisterFunction]` removes them.
- Register with `RegAsm.exe /codebase` from an Administrator prompt. The HKLM write needs admin rights.
- Provide `scripts/register.ps1` and `scripts/unregister.ps1` so members do not hand-type RegAsm paths.
- Registration code must never run during a Linux build.

### Proposed layout

```text
src/BajaAddIn/
  AddIn.cs                 # ISwAddin entry: ConnectToSW / DisconnectFromSW, command tab
  Commands/                # One class per command button
  Features/
    BatchExport/
    PartNumbering/
    AutoSave/
    FixComponents/
    SketchPointsCsv/
    AddInToggle/
    MountingTab/
    TubeNotcher/
    SuspensionMonitor/
  Ui/                      # PMP page builders, Task Pane controls
  Util/                    # Traversal, selection, units, settings helpers
scripts/                   # register.ps1, unregister.ps1
tests/                     # Sample .SLDPRT/.SLDASM files (Git LFS)
```

Unit conventions: the SolidWorks API always works in meters and radians.
Convert at the UI boundary only.

## 2. Feature notes

### Fix All Top-Level Components (Phase 1, small)
- Call `IAssemblyDoc.GetComponents(true)` to get top-level components only.
- Select each one with `IComponent2.Select4(append: true, ...)`, then call `IAssemblyDoc.FixComponent()`. FixComponent takes no arguments and acts on the selection.
- Skip components where `IComponent2.IsFixed` is already true. Offer "Float All" through `UnfixComponent`.
- Caveat: FixComponent applies to all configurations. Fixing only the current configuration needs an undocumented command ID (see codestack fix-float), so leave it out of v1.

### CSV Point Cloud I/O (Phase 1, small)
- Import:
  - Call `ISketchManager.Insert3DSketch` (or `InsertSketch` on a selected plane), then `CreatePoint(x, y, z)` for each row.
  - Set `ISketchManager.AddToDB = true` and `DisplayWhenAdded = false` for speed, and restore both afterwards.
  - Accept a header row and a units option (mm/in), and convert to meters.
- Export: call `ISketch.GetSketchPoints2`, then transform each point by `ISketch.ModelToSketchTransform.Inverse()` with `IMathPoint.MultiplyTransform` to get model coordinates.
- Built-in overlap: "Curve Through XYZ Points" makes a curve, not points, so no conflict.
- Reference: codestack `document/sketch/csv-import-points` and `export-point-coordinates`.

### Batch Exporter (Phase 1, medium)
- Call `IModelDocExtension.SaveAs3` for each format. The file extension selects the format; `SaveAs` is obsolete.
- STEP, IGES, and STL require the document to be active, so call `ISldWorks.ActivateDoc3` first. Call `ClearSelection2` first as well: when anything is selected, only the selection gets exported.
- PDF: use `ISldWorks.GetExportFileData` to get an `IExportPdfData`, and choose sheets with `SetSheets`.
- Format options use `SetUserPreferenceIntegerValue` with `swUserPreferenceIntegerValue_e`, for example `swStepAP` = 214.
- Save the user's preferences first and restore them afterwards.
- STEP AP242 is only documented through `PublishSTEP242File2`, which needs the MBD add-in and SW 2025 SP03 or later. Default to AP214.
- Scope: export the active doc, an assembly plus all unique components, or the sheet-metal flat patterns of an assembly. Name output files from part number properties. For unattended folder jobs, point users to the Task Scheduler "Export Files" task.

### Parametric Part Numbering (Phase 2, medium, highest team value)
- Traverse the tree by recursing on `IComponent2.GetChildren`, de-duplicating by file path plus referenced configuration.
- Read and write properties through `IModelDocExtension.CustomPropertyManager(config)`: `Add3(name, type, value, overwrite)`, `Set2`, `Get6`.
  - `IComponent2.CustomPropertyManager` gives per-component access but needs SW 2024 or later.
- `GetModelDoc2` returns null for suppressed or lightweight components. Resolve them first with `ResolveAllLightWeightComponents`, and report suppressed ones instead of skipping them silently.
- The numbering scheme needs a team decision first (open question 3). Store it in a config file so it can change without a rebuild.

### Mounting Tab Generator (Phase 2, medium)
- Inputs come from a PropertyManager page (`IPropertyManagerPage2`, created with `swPropertyManagerOptions_LockedPage`):
  - tab type preset (lap belt, anti-sub, seat, fire extinguisher, fuel tank, custom);
  - single or double shear;
  - tube OD, which sets the parallel spacing between the two tabs in double shear;
  - bolt size and tab thickness.
- Presets come from `docs/rules-reference.md` (Figures B-54 and B-55):
  - base width = 3 * D1;
  - end diameter = 2.2 * D1;
  - length at most the L value;
  - thickness at least the t value.
- Profile: a base line of width B, two straight sides tangent to a D2 arc that is concentric with the hole, and the hole D1 at height L above the base midpoint.
- Optionally contour the base line to the tube OD so the tab sits flush.
- Validate inputs against the table and warn when a value breaks a rule.
- Hole: B.12.2.1 requires the hole to match the bolt's nominal diameter, so default to a close fit, not a loose clearance hole.
- Geometry:
  - `SelectByID2` on a plane, then `InsertSketch`, then sketch the profile, then `IFeatureManager.FeatureExtrusion3`.
  - For the hole, either sketch a circle in the profile (simplest) or place `HoleWizard5` at a point selected with `SelectByRay`.
- Open question: should tabs be created as a new part placed in the assembly, or as features inside the frame weldment part?

### Auto-Saver (Phase 2, small)
- On a timer, call `ISldWorks.GetDocuments`, and for each document where `GetSaveFlag` is true, call `IModelDoc2.Save3(swSaveAsOptions_Silent, ...)`.
- Skip documents that have never been saved, and skip any save while a PropertyManager page or command is active.
- Warn in the settings UI that each save also triggers SolidWorks Backup copies when backups are enabled. The built-in Auto-recover only writes recovery files.

### Add-in Trigger Config (Phase 2, small)
- A JSON file lists add-ins to load or unload at startup, by DLL path or GUID.
- Load and unload with `ISldWorks.LoadAddIn(path)` / `UnloadAddIn(path)`, and check the result with `GetAddInObject`.
- Startup defaults are stored in `HKCU\Software\SolidWorks\AddInsStartup\{GUID}`. This key is documented on codestack, not in the official help.

### Surface Copier (Phase 2, only if still requested)
- Offset Surface at distance 0 already covers it. Ask the requester what is missing before building it.

### Ordered Tube Notcher (Phase 3, large)
- Use `IFeatureManager.InsertWeldmentTrimFeature2` with `swWeldmentTrimExtendOption_CopedCut` (4) for saddle notches.
  - Prefer this over boolean cuts (`InsertCombineFeature`), which leave tool-body cleanup to us.
- Priority order:
  - A trim/extend feature runs in tree order and has no priority property.
  - The notcher builds the trim features itself from a user-defined priority list (for example: floor rails > RRH > LFS > secondary). Each lower-priority member is trimmed against the higher-priority members it touches.
  - Per-node overrides come from the Task Pane.
- Structure System (SW 2022+): `IModelDocExtension.CreateStructureSystem` and `ICornerMember.TrimOrder` expose trim order directly. Investigate whether the team's frame is (or should be) a Structure System, because that could make this much simpler.
- Setting structural member group "Trim order" through the API is unconfirmed.

### Suspension Geometry Monitor (Phase 3, large)
- Inputs: a naming convention for reference points in the suspension subassemblies, for example `HP_UBJ_FL`, `HP_LBJ_FL`, `HP_TIE_FL`, `HP_WC_FL`.
- Read each point with `IComponent2.FeatureByName` and `IRefPoint.GetRefPoint`, then transform it into assembly space with `IComponent2.Transform2` and `IMathPoint.MultiplyTransform`.
- Compute camber, caster, toe, KPI, scrub, and steering radius in our own code, so the math can be unit tested without SolidWorks.
- Update triggers:
  - `ComponentMoveNotify2` fires only on mouse release.
  - For live updates during a drag, use a throttled `RepaintPostNotify` or a timer, and recompute only when `GetUpdateStamp` changes.
  - Show the values in a Task Pane created with `CreateTaskpaneView3`.
- Later: sweep through travel using a driving dimension and export a CSV of the curves.

### Auto Drawing Generator (Phase 3, experimental)
- Start with flat-pattern DXF export for sheet metal tabs only (part of the Batch Exporter). Full drawings come later, if ever.

## 3. Testing

- Pure logic (unit conversion, tab dimension rules, part number formatting, suspension math) lives in a separate project with no SolidWorks references, so it can be unit tested with `dotnet test` on any OS.
- SolidWorks-dependent code is tested by hand on Windows against the files in `tests/`. Each feature PR lists its manual test steps.

## 4. Open questions for the team

1. Interop DLL source for off-Windows builds. Dassault publishes no official NuGet package, and redistribution terms are unconfirmed. Options:
   - a third-party NuGet package (for example Visiativ.SOLIDWORKS.Interop), pinned to a version;
   - each developer copies the DLLs from `<SW install>\api\redist` into an untracked folder (does not work on Linux without a Windows install to copy from).
2. Minimum SolidWorks version the team runs. This decides whether SW 2024+ APIs like `IComponent2.CustomPropertyManager` are available.
3. Part numbering scheme: format, which fields, per configuration or per file, and who owns the counter.
4. Mounting tabs: generate them as separate parts or as features in the frame part?
5. Is the frame built with structural members (weldment profiles) or with Structure System? This decides the tube notcher approach.
6. Does the team use SOLIDWORKS PDM or a shared drive? This affects the auto-saver and batch export paths.
