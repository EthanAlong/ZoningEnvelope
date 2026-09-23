# Zoning Envelope for Rhino 8

Turns a zoning rule set (setbacks, height, encroachment / daylight planes,
transitional height, FAR, coverage) into a live buildable envelope on a parcel,
and checks a massing against it. Rules live in small JSON files, so adding a
new zone is a new file, not new code. Exterior-wall opening limits (CBC / IBC
Table 705.8) are reported per facade from the fire separation distance.

Status: prototype for design-feasibility conversations. Every rule set carries
its source and a `verified` note. Confirm numbers against the current code text
before relying on them. See `docs/CODE-NOTES.md`.

## What it does

1. Pick a closed polyline as the parcel and click the front (street) edge.
   Rear and side edges are assigned automatically; change them with `ZoneEdgeFlags`.
2. Choose a rule set in the panel. The envelope appears in the viewport as a
   translucent blue solid; setback lines and edge labels are drawn on the ground.
3. Pick a massing (polysurface, extrusion, mesh or SubD). The panel shows height,
   floor area vs FAR, footprint vs coverage, volume outside the envelope (drawn
   in red), and the allowed opening percentage for each facade.
4. Move or edit the parcel, the massing, or the rule JSON; everything recomputes
   on the next idle tick.

The panel has three regions: the rule table (what the code says, what it means
for this lot), the compliance table, and a 2D plan diagram of the parcel with
setbacks and story footprints. The 3D envelope is drawn in Rhino's own viewport
rather than a second 3D pane.

## Commands

| Command | Purpose |
| --- | --- |
| `ZoneEnvelope` | Open the panel |
| `ZoneSetParcel` | Select the parcel curve, then click near the front edge |
| `ZoneSetMassing` | Link a massing object to check |
| `ZoneEdgeFlags` | Per edge: role (front / side / rear), street + street width, adjacent low-density lot |
| `ZoneBake` | Add the envelope solid(s) to the document on layer "Zoning Envelope" |
| `ZoneReloadCodes` | Re-read the rule folder |

Edge flags matter for two rules: a street edge measures fire separation
distance to the street centerline; an "adjacent low density" edge triggers
transitional height (LA) or the R1-boundary setbacks (Santa Monica).

## Rule sets included

| id | Jurisdiction | Highlights |
| --- | --- | --- |
| `lamc-r1-hd1` | Los Angeles R1 | 20% depth front, narrow-lot side rule, 28/33 ft, 45 deg encroachment plane at 20 ft, RFA 0.45 |
| `lamc-r3-hd1` | Los Angeles R3 | side yard grows 1 ft per story above the 2nd, 45 ft, FAR 3, 800 sf per unit |
| `lamc-c2-1vl` | Los Angeles C2 1VL | no yards, 3 stories / 45 ft, FAR 1.5, transitional height 25 / 33 / 61 ft |
| `smmc-r1` | Santa Monica R1 | 10% width side, 28/32 ft, 45% coverage |
| `smmc-r2` | Santa Monica R2 | 8 ft or 16% side, 30 ft, 23 ft / 45 deg daylight plane, 2 ft upper-story side stepback, R1-boundary setbacks |
| `cbc-2022-705-8` | CBC 2022 | max exterior wall openings by fire separation distance |

## Adding or editing a rule set

Rule files are JSON. Built-in ones are embedded in the plugin; your own go in
`%APPDATA%\ZoningEnvelope\codes\`. A user file with the same `id` overrides the
built-in one. The folder is watched, so saving a file updates the envelope
immediately. "Edit JSON" in the panel copies the current built-in file to that
folder and opens it.

Schema (all lengths in feet):

```jsonc
{
  "id": "my-zone", "kind": "zone",
  "jurisdiction": "City", "name": "Zone name", "source": "code section", "verified": "date + how",
  "notes": ["anything not modeled"],
  "setbacks": {
    "front": { "feet": 15, "percentOfLotDepth": 20, "minFeet": 0, "maxFeet": 20 },
    "side":  { "feet": 5, "narrowLot": { "widthLessThanFeet": 50, "percentOfLotWidth": 10, "minFeet": 3 },
               "perStoryAboveStory": 2, "perStoryFeet": 1, "perStoryMaxFeet": 16 },
    "rear":  { "feet": 15 }
  },
  "height": { "maxFeet": 45, "pitchedRoofMaxFeet": 0, "maxStories": 0, "storyHeightFeet": 10 },
  "floorArea": { "ratio": 3.0, "label": "FAR" },
  "coverage": { "maxPercent": 45 },
  "density": { "lotAreaPerUnitSqFt": 800 },
  "stepbacks": [ { "sides": ["side"], "aboveStory": 1, "additionalFeet": 2, "label": "..." } ],
  "planes": [ { "sides": ["front","side"], "startHeightFeet": 20, "angleDegrees": 45, "from": "setbackLine", "label": "..." } ],
  "transitionalHeight": { "trigger": "adjacentLowDensity",
                          "steps": [ { "upToFeet": 50, "maxHeightFeet": 25 } ], "label": "..." },
  "adjacentLowDensitySetbacks": { "sideFeet": 10, "rearFeet": 20 }
}
```

Setback resolution: base `feet`, then the larger of any percent rules, then the
narrow-lot override, then min/max, then per-story growth. Stepbacks add on top
for stories above `aboveStory`.

## How the envelope is built

1. Parcel polyline oriented counter-clockwise; each edge gets a role.
2. For each story (or once, if no rule depends on the story) the parcel polygon
   is clipped by each edge's setback half-plane and extruded to that story's top.
3. Slabs are unioned, then cut by each plane rule (half-space above a plane that
   starts at the setback line at the given height and rises inward) and by each
   transitional-height band (everything above the step height within the band).
4. Compliance: massing height from its bounding box, floor plates by horizontal
   sections every `storyHeightFeet`, volume outside the envelope by boolean
   difference, fire separation distance per vertical face to the parcel edge it
   faces (plus half the street width on street edges).

Limits: parcels are treated as convex for setbacks (a concave corner is
over-cut), grade is the parcel's Z, and rules that depend on things the tool
cannot see (prevailing setback, hillside, overlays, bonuses) are listed in the
notes column, not modeled.

## Build and install

Requires Rhino 8 (runs on .NET 8) and the .NET 8+ SDK.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-local.ps1
```

This builds `src\ZoningEnvelope\bin\Release\net8.0-windows\ZoningEnvelope.rhp`
and copies it to `%APPDATA%\McNeel\Rhinoceros\packages\8.0\ZoningEnvelope\<version>\`,
where Rhino registers it at startup. If Rhino does not pick it up, drag the
`.rhp` onto the Rhino window once.

Engine smoke test (no Rhino needed):

```powershell
dotnet run --project tests\EngineSmoke
```

## Layout

```
src/ZoningEnvelope/
  ZoningEnvelopePlugIn.cs   plugin entry, panel registration
  Model/                    rule schema (ZoneCode), library loader, parcel setup persisted on the curve
  Engine/                   parcel analysis, half-plane clipping, envelope builder, compliance + openings
  Session/                  live state, document events, idle recompute, bake
  Display/                  viewport conduit (envelope, setback lines, excess volume)
  UI/                       Eto panel and 2D plan diagram
  Commands/                 Zone* commands
  codes/                    built-in rule sets
docs/CODE-NOTES.md          per-rule sources, what is and is not modeled
scripts/install-local.ps1   build + install
tests/EngineSmoke           console checks for rules and clipping
```
