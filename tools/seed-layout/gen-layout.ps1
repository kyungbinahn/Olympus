# Generate the starter base layout. ASCII only on purpose:
# Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI (CP949 on Korean Windows),
# which mangles Hangul and breaks parsing. The JSON output is pure ASCII anyway.

$BaseSize = 48
$Min = -[math]::Floor($BaseSize / 2)
$MaxEx = $Min + $BaseSize

$roster = @(
  @{ id='main_hall';      name='building.main_hall';      cells=6; count=1; ms=0;      costs=@() },
  @{ id='arena';          name='building.arena';          cells=6; count=1; ms=600000; costs=@(@{k='Stone';a=800},@{k='Wood';a=300}) },
  @{ id='grand_barracks'; name='building.grand_barracks'; cells=6; count=1; ms=480000; costs=@(@{k='Stone';a=600},@{k='Wood';a=400}) },
  @{ id='barracks';       name='building.barracks';       cells=5; count=2; ms=180000; costs=@(@{k='Wood';a=200},@{k='Stone';a=100}) },
  @{ id='infirmary';      name='building.infirmary';      cells=5; count=1; ms=150000; costs=@(@{k='Wood';a=180},@{k='Stone';a=80}) },
  @{ id='granary';        name='building.granary';        cells=5; count=1; ms=120000; costs=@(@{k='Wood';a=150}) },
  @{ id='warehouse';      name='building.warehouse';      cells=5; count=1; ms=120000; costs=@(@{k='Wood';a=150},@{k='Stone';a=60}) },
  @{ id='workshop';       name='building.workshop';       cells=5; count=1; ms=150000; costs=@(@{k='Wood';a=200},@{k='Stone';a=120}) },
  @{ id='academy';        name='building.academy';        cells=5; count=1; ms=240000; costs=@(@{k='Stone';a=300},@{k='Wood';a=150}) },
  @{ id='shrine';         name='building.shrine';         cells=5; count=1; ms=300000; costs=@(@{k='Stone';a=400}) },
  @{ id='quarry';         name='building.quarry';         cells=3; count=3; ms=60000;  costs=@(@{k='Wood';a=60}) },
  @{ id='lumber_camp';    name='building.lumber_camp';    cells=3; count=3; ms=60000;  costs=@(@{k='Stone';a=40}) },
  @{ id='farm';           name='building.farm';           cells=3; count=3; ms=45000;  costs=@(@{k='Wood';a=50}) },
  @{ id='olive_press';    name='building.olive_press';    cells=3; count=1; ms=90000;  costs=@(@{k='Wood';a=80},@{k='Stone';a=40}) },
  @{ id='well';           name='building.well';           cells=3; count=1; ms=30000;  costs=@(@{k='Stone';a=50}) },
  @{ id='forge';          name='building.forge';          cells=3; count=1; ms=120000; costs=@(@{k='Stone';a=150},@{k='Wood';a=60}) },
  @{ id='market';         name='building.market';         cells=3; count=1; ms=90000;  costs=@(@{k='Wood';a=120}) },
  @{ id='stable';         name='building.stable';         cells=3; count=1; ms=90000;  costs=@(@{k='Wood';a=100}) },
  @{ id='pottery';        name='building.pottery';        cells=3; count=1; ms=60000;  costs=@(@{k='Wood';a=70}) },
  @{ id='watchtower';     name='building.watchtower';     cells=3; count=2; ms=75000;  costs=@(@{k='Stone';a=120}) },
  @{ id='guard_post';     name='building.guard_post';     cells=3; count=2; ms=45000;  costs=@(@{k='Wood';a=60},@{k='Stone';a=40}) },
  @{ id='wall_segment';   name='building.wall_segment';   cells=3; count=2; ms=60000;  costs=@(@{k='Stone';a=200}) }
)

$toPlace = @()
foreach ($r in $roster) { for ($i = 0; $i -lt $r.count; $i++) { $toPlace += $r } }

# main_hall goes first so it lands dead centre - it is the core building the base grows around.
# Then largest first (big buildings need contiguous room), then id for determinism.
$toPlace = $toPlace | Sort-Object `
  -Property @{Expression={ if ($_.id -eq 'main_hall') { 0 } else { 1 } }},
            @{Expression={$_.cells}; Descending=$true},
            @{Expression={$_.id}}

$occ = @{}

function Test-CanPlace([int]$x, [int]$y, [int]$cells, [int]$margin) {
  if ($x -lt $Min -or $y -lt $Min) { return $false }
  if (($x + $cells) -gt $MaxEx -or ($y + $cells) -gt $MaxEx) { return $false }
  $lo = -$margin
  $hi = $cells - 1 + $margin
  for ($dy = $lo; $dy -le $hi; $dy++) {
    for ($dx = $lo; $dx -le $hi; $dx++) {
      if ($occ.ContainsKey("$($x+$dx),$($y+$dy)")) { return $false }
    }
  }
  return $true
}

function Set-Occupied([int]$x, [int]$y, [int]$cells, [string]$id) {
  for ($dy = 0; $dy -lt $cells; $dy++) {
    for ($dx = 0; $dx -lt $cells; $dx++) { $occ["$($x+$dx),$($y+$dy)"] = $id }
  }
}

# Candidate anchors ordered by distance from the centre. Deterministic.
$candidates = New-Object System.Collections.ArrayList
for ($y = $Min; $y -lt $MaxEx; $y++) {
  for ($x = $Min; $x -lt $MaxEx; $x++) {
    [void]$candidates.Add([pscustomobject]@{ x=$x; y=$y; d=($x*$x + $y*$y) })
  }
}
$candidates = $candidates | Sort-Object d, y, x

$slots = New-Object System.Collections.ArrayList
$slotId = 1
foreach ($b in $toPlace) {
  $placed = $false
  foreach ($c in $candidates) {
    $ax = $c.x - [math]::Floor($b.cells / 2)
    $ay = $c.y - [math]::Floor($b.cells / 2)
    if (Test-CanPlace $ax $ay $b.cells 1) {
      Set-Occupied $ax $ay $b.cells $b.id
      [void]$slots.Add([pscustomobject]@{ slotId=$slotId; defId=$b.id; x=$ax; y=$ay; zoneId=0 })
      $slotId++
      $placed = $true
      break
    }
  }
  if (-not $placed) { Write-Output "FAILED to place: $($b.id) ($($b.cells)x$($b.cells))" }
}

Write-Output "=== placement ==="
"slots        : $($slots.Count) / $($toPlace.Count)"
"cells used   : $($occ.Count) / $($BaseSize*$BaseSize)  (density $([math]::Round(100*$occ.Count/($BaseSize*$BaseSize),1))%)"
$xs = $slots | ForEach-Object { $_.x }
$ys = $slots | ForEach-Object { $_.y }
"anchor range : x $(($xs|Measure-Object -Min).Minimum)..$(($xs|Measure-Object -Max).Maximum)  y $(($ys|Measure-Object -Min).Minimum)..$(($ys|Measure-Object -Max).Maximum)   (base $Min..$($MaxEx-1))"

# Independent verification: rebuild occupancy from the emitted slots.
$check = @{}
$bad = 0
foreach ($s in $slots) {
  $cells = ($roster | Where-Object { $_.id -eq $s.defId })[0].cells
  for ($dy=0; $dy -lt $cells; $dy++) {
    for ($dx=0; $dx -lt $cells; $dx++) {
      $k = "$($s.x+$dx),$($s.y+$dy)"
      if ($check.ContainsKey($k)) { Write-Output "OVERLAP: Slot#$($s.slotId) vs Slot#$($check[$k]) at $k"; $bad++ }
      $check[$k] = $s.slotId
      if (($s.x+$dx) -lt $Min -or ($s.x+$dx) -ge $MaxEx -or ($s.y+$dy) -lt $Min -or ($s.y+$dy) -ge $MaxEx) {
        Write-Output "OUT OF BOUNDS: Slot#$($s.slotId) at $k"; $bad++
      }
    }
  }
}
if ($bad -eq 0) { Write-Output "verify OK - 0 overlaps, 0 out of bounds" } else { Write-Output "verify FAILED: $bad problems" }

$defs = foreach ($r in $roster) {
  [ordered]@{
    defId = $r.id
    displayNameKey = $r.name
    width = $r.cells
    height = $r.cells
    buildDurationMs = $r.ms
    costs = @(foreach ($c in $r.costs) { [ordered]@{ kind = $c.k; amount = $c.a } })
  }
}

$defFile = [ordered]@{ defs = @($defs) }
$layoutFile = [ordered]@{
  baseSize = $BaseSize
  slots = @($slots | ForEach-Object { [ordered]@{ slotId=$_.slotId; defId=$_.defId; x=$_.x; y=$_.y; zoneId=$_.zoneId } })
}

New-Item -ItemType Directory -Force -Path 'D:\Olympus\Assets\Data' | Out-Null
$enc = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText('D:\Olympus\Assets\Data\building-defs.json', ($defFile | ConvertTo-Json -Depth 6), $enc)
[IO.File]::WriteAllText('D:\Olympus\Assets\Data\base-layout.json', ($layoutFile | ConvertTo-Json -Depth 6), $enc)

Write-Output ""
Write-Output "wrote Assets/Data/building-defs.json ($($defs.Count) defs) and base-layout.json ($($slots.Count) slots)"
