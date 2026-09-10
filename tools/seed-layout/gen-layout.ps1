# Seed the base layout from the ART roster.
#
# The roster below mirrors D:\OlympusArt\배경\gen-buildings.sh exactly (21 buildings,
# codes B01/B03/B05/B14/B16 + N1-N4 + C01-C12). Footprints and roles are taken from the
# design descriptions in that script, so the generated art maps onto base slots.
# If the art roster changes, change it here too - the codes are the link.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI (CP949 on
# Korean Windows), which mangles Hangul and breaks parsing.

$BaseSize = 48
$Min = -[math]::Floor($BaseSize / 2)
$MaxEx = $Min + $BaseSize

# cells   : footprint (w x h). Non-square allowed.
# water   : must sit on the southern coastal strip (art shows it at the water's edge).
# Sizes quote the art script where it is explicit about scale.
$roster = @(
  # --- confirmed scope: S1/S2/S3 + resource chain + trojan horse ---
  @{ code='B01'; id='main_hall';      cells=@(6,6); ms=0;       water=$false; costs=@() },                                              # "the largest asset in the set"
  @{ code='B03'; id='timber_store';   cells=@(5,5); ms=60000;   water=$false; costs=@(@{k='Stone';a=60}) },                             # wood resource
  @{ code='B05'; id='mason_yard';     cells=@(5,5); ms=90000;   water=$false; costs=@(@{k='Wood';a=90}) },                              # stone resource
  @{ code='B14'; id='gatehouse';      cells=@(6,3); ms=180000;  water=$false; costs=@(@{k='Stone';a=300}) },                            # wall + gate tower, wide
  @{ code='B16'; id='war_engine_yard';cells=@(6,6); ms=600000;  water=$false; costs=@(@{k='Wood';a=500},@{k='Stone';a=200}) },          # "enormous" trojan horse

  # --- round 2: resource + village roster reinterpreted ---
  @{ code='N1';  id='ironworks';      cells=@(3,3); ms=120000;  water=$false; costs=@(@{k='Stone';a=150},@{k='Wood';a=60}) },
  @{ code='N2';  id='carpentry';      cells=@(3,3); ms=90000;   water=$false; costs=@(@{k='Wood';a=120}) },
  @{ code='N3';  id='mine';           cells=@(3,3); ms=120000;  water=$false; costs=@(@{k='Wood';a=100},@{k='Stone';a=60}) },
  @{ code='N4';  id='quarry';         cells=@(5,5); ms=90000;   water=$false; costs=@(@{k='Wood';a=80}) },                              # open rock-cutting site
  @{ code='C01'; id='arena';          cells=@(6,6); ms=480000;  water=$false; costs=@(@{k='Stone';a=700},@{k='Wood';a=200}) },          # stone amphitheatre
  @{ code='C02'; id='barracks';       cells=@(5,5); ms=180000;  water=$false; costs=@(@{k='Wood';a=200},@{k='Stone';a=100}) },
  @{ code='C03'; id='farm';           cells=@(5,5); ms=45000;   water=$false; costs=@(@{k='Wood';a=60}) },                              # food
  @{ code='C04'; id='fishing_hut';    cells=@(3,3); ms=45000;   water=$true;  costs=@(@{k='Wood';a=70}) },                              # on stilts at the water
  @{ code='C05'; id='silver_mine';    cells=@(3,3); ms=240000;  water=$false; costs=@(@{k='Wood';a=150},@{k='Stone';a=150}) },
  @{ code='C06'; id='council_hall';   cells=@(5,5); ms=300000;  water=$false; costs=@(@{k='Stone';a=400},@{k='Wood';a=150}) },
  @{ code='C07'; id='healing_house';  cells=@(3,3); ms=150000;  water=$false; costs=@(@{k='Stone';a=200}) },
  @{ code='C08'; id='notice_stele';   cells=@(1,1); ms=15000;   water=$false; costs=@(@{k='Stone';a=30}) },                             # "prop-scale, much smaller"
  @{ code='C09'; id='training_ground';cells=@(6,6); ms=60000;   water=$false; costs=@(@{k='Wood';a=100}) },                             # "mostly open space rather than a building"
  @{ code='C10'; id='shipyard';       cells=@(6,6); ms=420000;  water=$true;  costs=@(@{k='Wood';a=450},@{k='Stone';a=100}) },          # slipway down to the water
  @{ code='C11'; id='hidden_cove';    cells=@(3,3); ms=210000;  water=$true;  costs=@(@{k='Wood';a=120},@{k='Gold';a=50}) },            # sea cave at the waterline
  @{ code='C12'; id='warehouse';      cells=@(5,5); ms=120000;  water=$false; costs=@(@{k='Wood';a=150},@{k='Stone';a=60}) }
)

$occ = @{}

function Test-CanPlace([int]$x, [int]$y, [int]$w, [int]$h, [int]$margin) {
  if ($x -lt $Min -or $y -lt $Min) { return $false }
  if (($x + $w) -gt $MaxEx -or ($y + $h) -gt $MaxEx) { return $false }
  for ($dy = -$margin; $dy -lt ($h + $margin); $dy++) {
    for ($dx = -$margin; $dx -lt ($w + $margin); $dx++) {
      if ($occ.ContainsKey("$($x+$dx),$($y+$dy)")) { return $false }
    }
  }
  return $true
}

function Set-Occupied([int]$x, [int]$y, [int]$w, [int]$h, [string]$id) {
  for ($dy = 0; $dy -lt $h; $dy++) {
    for ($dx = 0; $dx -lt $w; $dx++) { $occ["$($x+$dx),$($y+$dy)"] = $id }
  }
}

# Inland anchors: nearest the centre first. Deterministic.
$inland = New-Object System.Collections.ArrayList
# Coastal anchors: southern strip first (lowest y), then nearest the centre line.
$coastal = New-Object System.Collections.ArrayList
for ($y = $Min; $y -lt $MaxEx; $y++) {
  for ($x = $Min; $x -lt $MaxEx; $x++) {
    [void]$inland.Add([pscustomobject]@{ x=$x; y=$y; d=($x*$x + $y*$y) })
    [void]$coastal.Add([pscustomobject]@{ x=$x; y=$y; d=($x*$x) })
  }
}
$inland  = $inland  | Sort-Object d, y, x
$coastal = $coastal | Sort-Object y, d, x

# main_hall first so it lands dead centre, then largest first, then code for determinism.
$order = $roster | Sort-Object `
  -Property @{Expression={ if ($_.id -eq 'main_hall') { 0 } else { 1 } }},
            @{Expression={ $_.cells[0] * $_.cells[1] }; Descending=$true},
            @{Expression={$_.code}}

$slots = New-Object System.Collections.ArrayList
$slotId = 1
foreach ($b in $order) {
  $w = $b.cells[0]; $h = $b.cells[1]
  $candidates = if ($b.water) { $coastal } else { $inland }
  $placed = $false

  foreach ($c in $candidates) {
    $ax = $c.x - [math]::Floor($w / 2)
    $ay = if ($b.water) { $c.y } else { $c.y - [math]::Floor($h / 2) }
    if (Test-CanPlace $ax $ay $w $h 1) {
      Set-Occupied $ax $ay $w $h $b.id
      [void]$slots.Add([pscustomobject]@{ slotId=$slotId; code=$b.code; defId=$b.id; x=$ax; y=$ay; zoneId=0 })
      $slotId++
      $placed = $true
      break
    }
  }
  if (-not $placed) { Write-Output "FAILED to place: $($b.id) ($w x $h)" }
}

Write-Output "=== placement ==="
"slots        : $($slots.Count) / $($roster.Count)"
$area = ($roster | ForEach-Object { $_.cells[0] * $_.cells[1] } | Measure-Object -Sum).Sum
"footprint    : $area cells / $($BaseSize*$BaseSize)  (density $([math]::Round(100*$area/($BaseSize*$BaseSize),1))%)"
$xs = $slots | ForEach-Object { $_.x }; $ys = $slots | ForEach-Object { $_.y }
"anchor range : x $(($xs|Measure-Object -Min).Minimum)..$(($xs|Measure-Object -Max).Maximum)  y $(($ys|Measure-Object -Min).Minimum)..$(($ys|Measure-Object -Max).Maximum)   (base $Min..$($MaxEx-1))"
Write-Output ""
Write-Output "coastal (water=true) placements:"
$slots | Where-Object { ($roster | Where-Object { $_.id -eq $_.defId }) } | Out-Null
foreach ($s in $slots) {
  $r = $roster | Where-Object { $_.id -eq $s.defId }
  if ($r.water) { "  {0,-5} {1,-16} @({2},{3})" -f $s.code, $s.defId, $s.x, $s.y }
}

# Independent verification: rebuild occupancy from the emitted slots.
$check = @{}
$bad = 0
foreach ($s in $slots) {
  $r = $roster | Where-Object { $_.id -eq $s.defId }
  for ($dy=0; $dy -lt $r.cells[1]; $dy++) {
    for ($dx=0; $dx -lt $r.cells[0]; $dx++) {
      $k = "$($s.x+$dx),$($s.y+$dy)"
      if ($check.ContainsKey($k)) { Write-Output "OVERLAP: Slot#$($s.slotId) vs Slot#$($check[$k]) at $k"; $bad++ }
      $check[$k] = $s.slotId
      if (($s.x+$dx) -lt $Min -or ($s.x+$dx) -ge $MaxEx -or ($s.y+$dy) -lt $Min -or ($s.y+$dy) -ge $MaxEx) {
        Write-Output "OUT OF BOUNDS: Slot#$($s.slotId) at $k"; $bad++
      }
    }
  }
}
if ($bad -eq 0) { Write-Output "`nverify OK - 0 overlaps, 0 out of bounds" } else { Write-Output "`nverify FAILED: $bad problems" }

$defs = foreach ($r in $roster) {
  [ordered]@{
    defId = $r.id
    artCode = $r.code
    displayNameKey = "building.$($r.id)"
    width = $r.cells[0]
    height = $r.cells[1]
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
