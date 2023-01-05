
# Introduction
Valheim version: 0.212.9
Mods: no mods (vanilla)

## Code references
* MapTable.OnRead
* MapTable.OnWrite
* Minimap.GetSharedMapData (used by MapTable.OnWrite, Get means: get a combination of Minimap data and current MapTable data passed in parameter)
* Minimap.AddSharedMapData (used by MapTable.OnRead, Add means: reads MapTable data and add it to Minimap data)

# Current implementation

## Description
The current implementation follows a principle of simplicity by being fully "additive" (i.e meaning there is never deletion carried when sharing map data). This lead to some limitations when players wish to collaborate on exploration with pins (i.e markers on the map), when many pins are moved by players or when multiple MapTable are set up for creating different sharing groups.

A few specific points of the existing implementation:

1. When writing to the MapTable only the player's Minimap pins (Minimap.m_pins) are taken into account to construct the new map data wrote to the MapTable.
2. When writing to the MapTable all the player's Minimap pins from others player will be wrote to the MapTable.
3. When writing to MapTable the player's own exploration (m_explored) an others exploration (m_exploredOthers) from Minimap is written to MapTable.

When more than one MapTable is being used by different player's groups the expectation is that each MapTable's content is fully independent from each other. However whenever at least one player read and write multiple MapTables the content of MapTables leaks between each others through player's writes to it.

# Identified bugs

## Player's own pins reappear after deletion

### Steps to reproduce
1. MapTable is created
2. PlayerA creates a pinX on Minimap
3. PlayerA writes MapTable
4. PlayerB reads MapTable
5. PlayerA deletes pinX on Minimap
6. PlayerA writes MapTable
7. PlayerB writes MapTable
8. PlayerA reads MapTable

### Expected results
PlayerA's Minimap doesn't have a pinX.

### Actual result
PlayerA has a pinX on Minimap.

### Discussion
When PlayerB wrote to the MapTable it wrote the pinX again to the MapTable from its own Minimap.

## Pins's additions removed from MapTable

### Steps to reproduce
1. MapTable is created
2. PlayerA creates a pinX on Minimap
3. PlayerA writes MapTable
4. PlayerB creates a pinY on Minimap
5. PlayerB writes MapTable
6. PlayerC reads MapTable

### Expected results
PlayerC has pinX and pinY on Minimap.

### Actual result
PlayerC only has pinY on Minimap.

### Discussion
When playerB wrote to the table only pinY was known from the player's Minimap and thus once written to Maptable pinX is removed from it.

## MapTable takes precedence over Minimap changes for player's own pins

### Steps to reproduce
1. PlayerA creates pin1
2. PlayerA writes MapTable
3. PlayerA deletes pin1
4. PlayerA reads MapTable

### Expected results
PlayerA minimap hasn't pin1

### Actual result
PlayerA minimap show pin1

## Exploration leaking between MapTables

### Steps to reproduce
Assuming that PlayerA and PlayerB do not wish to share each other's exploration and that PlayerC wish to share exploration with both PlayerA and PlayerB.
MapTable1 will be used to share exploration between PlayerA and PlayerC.
MapTable2 will be used to share exploration between PlayerB and PlayerC.

1. MapTable1 is created
2. MapTable2 is created
3. PlayerA explores N area
4. PlayerA writes to MapTable1
5. PlayerB explores M area
6. PlayerB writes to MapTable2
7. PlayerC explores O area
8. PlayerC reads MapTable1 and MapTable2
9. PlayerC writes MapTable1
10. PlayerA reads MapTable1

N,M and O areas are mutually exclusive areas of the map.

### Expected results
PlayerA do not see the M area
PlayerA see the O area (as others faded)
PlayerA see the N area (as own)

### Actual result
PlayerA see the M area (as others faded)
PlayerA see the O area (as others faded)
PlayerA see the N area (as own)

### Discussion
When PlayerC writes the MapTable all the exploration known by PlayerC (own and others) is written.