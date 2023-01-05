# FixedMapTable mod for Valheim

This mode address different behavior use cases when sharing map exploration and pins using MapTable (i.e cartography table).

## Goals
- Allow collaborative map exchanges between players by allowing player to modify each other pins.
- Enable setting up several collaboration groups among players by using several MapTables in which content is independent from each other.
- Resolve cases described in `behavior-cases.md` document. 

## Changes from original implementation
- A MapTable's state always comprises the full known status.
- Only own's content of the writing player is added to the Table when writing.
- A pin uniqueness is defined by a 10 distance radius.

## References
Used for inspiration and examples.
* https://github.com/weiler-git/LimitCartographyPins
* https://github.com/valheimPlus/ValheimPlus/ 

Docs:
* https://github.com/Valheim-Modding/Wiki/wiki
* https://github.com/valheimPlus/ValheimPlus/blob/development/CONTRIBUTING.md 