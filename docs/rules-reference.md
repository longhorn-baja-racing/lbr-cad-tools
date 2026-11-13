# Baja SAE Rules Reference

Rule values the tools depend on. Source: Baja SAE Rules 2027 Rev A
(2026-09-11), checked against the PDF text. Figure numbers below are from
2027 Rev A; 2026 Rev B has the same B.12 text with different figure numbers.
Always confirm against the current rule book at
https://www.bajasae.net/cdsweb/gen/DocumentResources.aspx before competition.

## Section map

- B.4 Driver Restraint: harness, arm restraints, head restraint, seats. Points to B.12.2 for tabs.
- B.12 Fasteners and Attachments. B.12.2 governs welded attachment tabs.

## B.12.2 Required Attachment Tabs (Figures B-54 and B-55)

Applies to tabs for B.4.2 Driver Harness, B.8.8 Fire Extinguisher, and B.4.5 Seats.

D1 = min bolt size (and hole diameter), D2 = min edge diameter, B = min base
width, L = max length (base line midpoint to hole center), t = min thickness.

Tab geometry (Figure B-54, page 89):

```text
        ____
      /  D2  \       D2 = diameter of the rounded end, concentric with the hole
     |  (D1)  |      D1 = hole diameter (= nominal bolt diameter, B.12.2.1)
     |   +    | ---
     /        \   |
    /          \  L  L = base line to hole center
   /            \ |
  /______________\---
  |<---- B ----->|   B = base width, along the weld line
```

The side edges run straight from the ends of the base line up to the D2 arc,
tangent to it. The base line may be contoured to fit the tube (B.12.2.2).

Table (Figure B-55):

| Use               | D1             | D2      | B     | L (max) | t (min)  | Shear  | Weld       |
|-------------------|----------------|---------|-------|---------|----------|--------|------------|
| Lap belt          | 7/16 in        | 2.2*D1  | 3*D1  | 2.0 in  | 0.090 in | Double | One side   |
| Anti-sub belt     | 7/16 in        | 2.2*D1  | 3*D1  | 2.0 in  | 0.090 in | Double | One side   |
| Fire extinguisher | check bracket  | 2.2*D1  | 3*D1  | 3.0 in  | 0.125 in | Single | Both sides |
| Seat              | 1/4 in         | 2.2*D1  | 3*D1  | 2.0 in  | 0.125 in | Single | Both sides |
| Fuel tank         | check bracket  | 2.2*D1  | 3*D1  | 2.0 in  | 0.125 in | Single | Both sides |

Rule text that affects tab geometry:
- B.12.2.1: the fastener must match the nominal hole diameter in the tab
  (0.5 in hole means 0.5 in bolt). The tab generator must use a close-fit
  hole, not a loose clearance hole.
- B.12.2.2: welded completely along the base line, on one or both sides per
  the table; base line may be contoured to the mating surface; no cutouts or
  other weight-saving features; must not visibly deform when loaded.
- No tab material grade is specified. B.4.2.5.2 only says "metal tabs".

## B.12.1 Fasteners

- B.12.1.1: captive fasteners only (nylon locknuts, cotter pins, safety wire
  for blind holes). Welded nylon locknuts, lock washers, and thread sealants
  do not count.
- B.12.1.2: at least 2 threads past the nut.
- B.12.1.3: SAE Grade 5, Metric 8.8, or AN/MS.

## Related attachment rules

- B.4.2.5.2 Lap belts: tabs per B.12.2 on primary or secondary tubes in the
  plane of the LFS or RRH; must pivot freely and not be loaded in bending;
  bolts at least 7/16 in. (The 2026 change log wording says "attached to the
  LFS or RRH"; the rule text says "in the plane of".)
- B.4.2.6.3 Anti-sub belt: metal tabs in double shear, bolts at least 7/16 in.
- B.4.5.3 Seats: at least 6 mounting points (4 bottom, 2 back), at least
  6.0 in (153 mm) apart; tabs per B.12.2; spacers up to 0.5 in (13 mm).
- B.6.5.1 Fuel tank: mount to secondary-spec tubes supported at both ends
  (preferred: two square/rectangular tubes with sleeved holes). Cantilevered
  "C" brackets are acceptable if length is at most 4 in (101.6 mm) from weld
  line to hole center and edge distance is at least 14.3 mm (0.5626 in).
  Hardware: 3/8 in bolts, metal washers with 0.812 to 0.875 in OD, and a
  rubber isolating washer (McMaster 94733A723) on both sides of the flange.
- B.8.8.4 Fire extinguisher: mounted in the RRH plane using B.12.2 or B.6.5.1 tabs.
- B.10.3.3.2 External kill switch: on a tab connected directly to the RRH.
- B.11.3.2 Tow tabs: at least 0.125 in thick, grade-rated pin at least 0.25 in.

## Tube rules relevant to tabs and notching

- B.3.2.3 Secondary members: at least 1.0 in (25.4 mm) OD x 0.035 in (0.89 mm) wall.
- B.3.2.16 Primary members: wall at least 0.062 in (1.57 mm), yield at least
  365 MPa, carbon at least 0.18 percent (Docol R8 exempt), bending stiffness
  Kb at least 2620 Nm^2, bending strength Sb at least 374 Nm.
- B.3.6.1 Drilled tubes: a hole through both walls needs a weld-in sleeve on
  the tube centerline, OD at most 0.5x the tube OD, wall at least the tube
  wall, extending past the tube on both sides and fully welded.
- B.3.6.2 A single hole of 5 mm (0.197 in) or less needs no sleeve.
