# Sample models

Small models to try every tool on, and to test with. They follow the team naming
convention (`[a|d][TEAM]_####_[PartName]_REV##_V##`).

| Folder | Model | Try |
|---|---|---|
| `Frame` | `CHA_0001_DemoFrame_REV01_V01.SLDPRT`: weldment frame of 1.5 in pipe, an LFS floor, an RRH hoop on its corners, and an SM diagonal; no notches yet | **Tube Notcher**: LFS > RRH > SM, 5 cuts in 3 trim features |
| `Suspension` | `DYN_0001_UprightSweep_REV01_V01.SLDPRT`: front left upright swung about the lower ball joint by an angle dimension (100 deg), with HP_LBJ, HP_UBJ, HP_SA, and HP_WC_FL reference points | **Suspension Check**; **Suspension Sweep** (double-click the sketch, click the angle, sweep 90 to 110 deg: camber changes by minus the turn); **Suspension Monitor** (change the angle and rebuild) |
| `Tabs` | `aERG_0001_PedalBox_REV01_V01.SLDASM` with a 1 in tube | **Mounting Tab**: click the tube's outer face first; the tab is inserted and named ERG_0003_..._REV01_V01 |
| `Naming` | `aCHA_0010_SeatFrame_REV02_V01.SLDASM` with planted mistakes: a REV01 part in a REV02 assembly, a part called "Seat Bracket", and 0011 used for two parts | **Name Check**: 4 files to fix; next free CHA number 0013 |
| `Drawings` | `CHA_0020_SeatBracket_REV01_V01.SLDPRT` | **Make Drawings**: dCHA_0020_SeatBracket_REV01_V01.SLDDRW at 1:1 |

Copy a folder somewhere else before trying a tool on it, so the samples stay as they are.

## Making them

The models are built by the SolidWorks test tool, so they open in the SolidWorks
version that made them. SolidWorks cannot open files from a newer version, so make
them on the oldest version the team runs (SolidWorks 2025):

```powershell
dotnet build
tools\SwSmokeTest\bin\Debug\net48\SwSmokeTest.exe --samples=samples
```

Close SolidWorks first. It takes about 30 seconds.
