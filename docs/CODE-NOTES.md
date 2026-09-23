# Rule sources and verification notes

Checked 2026-09-23. "Confirmed" means the number was read from the code text or
an official City table; "secondary" means a reputable summary was the only
readable source. Official code hosts (amlegal, ecode360, ICC) block automated
fetches, so open them in a browser before a real submission.

## Los Angeles (LAMC Chapter 1)

### `lamc-r1-hd1` R1 One-Family, Height District 1

| Rule | Value used | Status | Source |
| --- | --- | --- | --- |
| Front yard | 20% of lot depth, need not exceed 20 ft | secondary (2 guides agree) | LAMC 12.08 C.1 |
| Side yard | 5 ft; lots < 50 ft wide: 10% of width, min 3 ft | secondary | LAMC 12.08 C.2 |
| Rear yard | 15 ft | secondary | LAMC 12.08 C.3 |
| Height | 33 ft; 28 ft when uppermost roof slope < 25% | confirmed from 12.21.1 text (Ord. 181,624) | LAMC 12.21.1 A.1 |
| Encroachment plane | starts 20 ft above grade at front and side setback lines, 45 deg inward | secondary (BMO summary + zoning guide) | LAMC 12.08 C.5 (Ord. 184,802) |
| RFA | 0.45 x lot area | secondary (BMO 2017) | LAMC 12.03, 12.08 C.4 |

Not modeled: prevailing front setback, hillside and coastal height rules (45 ft
in Coastal Zone per 12.21.1), side wall plane break, RFA garage exemption and
20% bonus, R1 variation zones (R1V/R1F/R1R, 12.21.6).

### `lamc-r3-hd1` R3 Multiple Dwelling, Height District 1

| Rule | Value used | Status | Source |
| --- | --- | --- | --- |
| Front yard | 15 ft | secondary | LAMC 12.10 C.1 |
| Side yard | 5 ft, +1 ft per story above the 2nd, max 16 ft | confirmed (12.10 text) | LAMC 12.10 C.2 |
| Rear yard | 15 ft | confirmed | LAMC 12.10 C.3 |
| Height | 45 ft | secondary (CP-7150 summary) | LAMC 12.21.1 A.1 |
| FAR | 3:1 | confirmed (HD1 definition) | LAMC 12.21.1 A.1 |
| Density | 800 sf lot area per unit | confirmed | LAMC 12.10 C.4 |

### `lamc-c2-1vl` C2 Commercial, Height District 1VL

| Rule | Value used | Status | Source |
| --- | --- | --- | --- |
| Yards | none (commercial use) | secondary; residential use follows R4 yards | LAMC 12.14 C |
| Height / stories | 45 ft / 3 stories | secondary (CP-7150) | LAMC 12.21.1 A.1 |
| FAR | 1.5:1 | secondary | LAMC 12.21.1 A.1 |
| Transitional height | within 0-49 ft of an RW1-or-more-restrictive lot: 25 ft; 50-99 ft: 33 ft; 100-199 ft: 61 ft | confirmed (two sources quote the table) | LAMC 12.21.1 A.10 |

Modeled as bands measured perpendicular from the flagged parcel edge:
[0,50) 25 ft, [50,100) 33 ft, [100,200) 61 ft. The code measures from the
adjacent lot, which is the shared lot line when the lots abut.

## Santa Monica (SMMC Article 9, 2015 Zoning Ordinance as amended)

### `smmc-r1` R1 Single-Unit Residential

| Rule | Value used | Status | Source |
| --- | --- | --- | --- |
| Front | 20 ft default ("varies by street") | official table says varies | Table 9.07.030; Official Districting Map |
| Sides | 10% of parcel width each side | official table ("10% or 30% aggregate") | Table 9.07.030 |
| Rear | 15 ft | official table | Table 9.07.030 |
| Height | 2 stories; 28 ft flat / 32 ft pitched | official table ("28'-32' depending on parcel size"), press release says 28 ft | Table 9.07.030 |
| Parcel coverage | 45% new construction (55% additions) | official press release 2019-10-23 | Table 9.07.030 |

Not modeled: 30% aggregate side option, 23 ft flat-roof wall height limit,
upper-story stepback areas (1% of parcel each), outdoor living space limits,
small-parcel coverage allowance, ADU exemptions.

Sources: City of Santa Monica Housing Element 2021-2029 Appendix E Figure E-2;
City press release "City Council Approves Changes to Development Standards for
Single-Unit Dwellings" (2019-10-23).

### `smmc-r2` R2 Low Density Residential

| Rule | Value used | Status | Source |
| --- | --- | --- | --- |
| Front | 20 ft | official table | Table 9.08.030 |
| Sides | parcel >= 50 ft: 8 ft; < 50 ft: 16% of width, min 4 ft | official table ("8'" / "4' or 16%") | Table 9.08.030 |
| Rear | 15 ft | official table | Table 9.08.030 |
| Height | 2 stories / 30 ft | official table | Table 9.08.030 |
| Coverage | ground floor 45% | official table | Table 9.08.030 |
| Daylight plane | above 23 ft nothing crosses a plane from 23 ft at the front setback line rising 45 deg toward the rear | code excerpt (search result quoting 9.08/9.21) | SMMC 9.08.030 / 9.21 |
| Side stepback | additional 2 ft average at each story above ground | code excerpt; modeled as flat 2 ft on stories 2+ | SMMC 9.08.030 |
| R1 boundary | 10 ft interior side, 20 ft rear when abutting R1 | code excerpt | SMMC 9.21 |

Not modeled: upper-story coverage (90% of allowable ground floor), 100%
affordable height bonus, density (2,000 sf per unit or 4 units), outdoor living
area.

## California Building Code 2022, Table 705.8

Rows confirmed from the IBC 2021 table text (Washington SBCC reproduction) and a
secondary summary.

| Fire separation distance | UP, NS | UP, S | P |
| --- | --- | --- | --- |
| 0 to < 3 ft | Not permitted | Not permitted | Not permitted |
| 3 to < 5 | Not permitted | 15% | 15% |
| 5 to < 10 | 10% | 25% | 25% |
| 10 to < 15 | 15% | 45% | 45% |
| 15 to < 20 | 25% | 75% | 75% |
| 20 to < 25 | 45% | No limit | No limit |
| 25 to < 30 | 70% | No limit | No limit |
| 30 or more | No limit | No limit | No limit |

UP = unprotected openings, NS / S = building not / fully sprinklered
(903.3.1.1), P = protected openings. Fire separation distance is measured
perpendicular from the exterior wall to the closest lot line, the centerline of
a street or alley, or an imaginary line between buildings on the same lot. The
tool uses the parcel edge each facade faces and adds half the street width on
edges flagged as street. Footnote exceptions (Group R-3, H, party walls, open
parking) are not applied.
