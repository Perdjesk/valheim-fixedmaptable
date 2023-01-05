using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using static Minimap;

namespace FixedMapTable.GameClasses
{
    /**
     * 
     * Conflicts within a conflictRadius radius of pin's location, regadless of pin's owner, between MapTable and Minimap pins are resolved 
     * as such:
     * - When MapTable is read: 
     *      - MapTables pins (others-grey) take precedence over MiniMap (own-white) pins
     * - When MapTable is written: 
     *      - Minimap pins (own-white) take precedence over MapTable pins (others-grey)
     * This behaviour allows for collaboration between players to edit each others pins.
     * Concurrent changes over the same pin (i.e within conflictRadius) by more than one player will be resolved 
     * depending of players order of read and write to the MapTable.
     *  
     *  
     *  Limitations:
     *      - Edgecase for game create pins with m_save=true.
     *        The conflictRadius radius rule is only apply during write/read of MapTable and RightDoubleClick on Minimap
     *        and thus it is possible for a player Minimap to contain several own-white pins (Boss pin for example, any others?)
     *        whitin a conflictRadius radius. 
     *        However when wrote to the table only one will persist.
     *        Possible resolution would be to seek to patch others kind of game's ping addition to
     *        Minimap and enforce the same conflictRadius radius rule.
     *        TODO: search for game created pins and apply the conflict radius.
     *      - Edgecase of pins with same m_pos wrote by different players wrote to different MapTables and read by one player those
     *        MapTables.
     *        TODO: rethink an alternative of othersPinDB and orphaned pins detection and deletion.
     */
    [HarmonyPatch(typeof(Minimap))]
    class MinimapPatch
    {
        private static ManualLogSource logger = FixedMapTablePlugin.logger;

        /**
         * Define the radius distance from a pin's position for which pin will be considered unique
         */
        private const float conflictRadius = 10f;

        /**
         * Player's pins are only added if none exists within conflict radius, when creating from Minimap UI.
         */
        [HarmonyPrefix]
        [HarmonyPatch("OnMapDblClick")]
        static bool OnMapDblClick_PrefixPatch()
        {
            if (PinsInRange(Minimap.instance.ScreenToWorldPoint(Input.mousePosition), ref Minimap.instance.m_pins).Count > 0)
            {
                return false;
            }
            return true;
        }

        /**
         * This method is not a patch because the caller method in MapTable is patched to extend it with the MapTable identifier.
         * Used when reading from MapTable.
         * 
         * Read from MapTable:
         *  - Basis is taken from the current state of Minimap
         *  - The MapTable others-grey pins are added applying the radius conflict rule (removing own-white pins as well)
         *  - Deletion of orphaned others-grey pins
         *  - The state is written to the Minimap
         */
        public static bool AddSharedMapData(byte[] dataArray, string mapTableId)
        {
            logger.LogDebug("AddSharedMapData prefix patch starts.");

            // Instrumentation
            Stopwatch watch = new Stopwatch();
            watch.Start();

            logger.LogDebug("Reading data from MapTable: " + mapTableId);

            ZPackage zPackage = new ZPackage(dataArray);
            int mapTableMapVersion = zPackage.ReadInt();
            List<bool> mapTableExplored = Minimap.instance.ReadExploredArray(zPackage, mapTableMapVersion);
            if (mapTableExplored == null)
            {
                return false;
            }
            bool changeDetected = false;
            for (int i = 0; i < Minimap.instance.m_textureSize; i++)
            {
                for (int j = 0; j < Minimap.instance.m_textureSize; j++)
                {
                    int num2 = i * Minimap.instance.m_textureSize + j;
                    bool flag2 = mapTableExplored[num2];
                    bool flag3 = Minimap.instance.m_exploredOthers[num2] || Minimap.instance.m_explored[num2];
                    if (flag2 != flag3 && flag2 && Minimap.instance.ExploreOthers(j, i))
                    {
                        changeDetected = true;
                    }
                }
            }
            if (changeDetected)
            {
                Minimap.instance.m_fogTexture.Apply();
            }
  
            if (mapTableMapVersion >= 2)
            {
                long playerID = Player.m_localPlayer.GetPlayerID();
                int mapTableNbPins = zPackage.ReadInt();

                //Accumulate all the pins of others read from MapTable for detecting pin deletion (orphans)
                List<PinData> othersSeenPins = new List<PinData>();
                for (int k = 0; k < mapTableNbPins; k++)
                {
                    long pinOwner = zPackage.ReadLong();
                    string text = zPackage.ReadString();
                    Vector3 pos = zPackage.ReadVector3();
                    PinType type = (PinType)zPackage.ReadInt();
                    bool isChecked = zPackage.ReadBool();
                    if (pinOwner == playerID)
                    {
                        // We ignore from the remote MapTable player's own pins
                        // The Minimap take precedence for those
                        continue;
                    }

                    // Search pins in conflict radius of the addedPin
                    List<PinData> pinsInRange = MinimapPatch.PinsInRange(pos, ref Minimap.instance.m_pins);
                    // Remove all pins within the surrounding conflict radius of addedPin
                    // Has to be done before additions for correct tracking in ohtersPinDB.
                    foreach (PinData pin in pinsInRange)
                    {
                        // Remove from Minimap
                        Minimap.instance.RemovePin(pin);
                        // Remove if present in the othersPinsDB tracking
                        MapTablePatch.othersPinsDB.Remove(pin.m_pos.ToString());
                    }

                    // Add the addedPin from MapTable to Minimap
                    PinData addedPin = Minimap.instance.AddPin(pos, type, text, true, isChecked, pinOwner);

                    // Add the addedPin to the tracking for deletion of orphaned pins
                    othersSeenPins.Add(addedPin);
                    MapTablePatch.othersPinsDB[addedPin.m_pos.ToString()] = mapTableId;

                    // Irrelevant from this point as it will be true as soon as one pin is processed
                    // Original implementation check for a radius of 1 before adding pin, any pin addition is detected as a change
                    // Only used to change the message feedback in UI
                    //TODO: see if pins changes can be correctly tracked since othersPins are now always added from MapTable
                    changeDetected = true;                    
                }

                // Clean others orphaned pins in Minimap
                // TODO: edge case of deleting pins with exacts same m_pos, from different players and each wrote to a different
                // MapTable. naaaaah not gonna happen :fingers_crossed:
                for (int pinIndex = Minimap.instance.m_pins.Count - 1; pinIndex >= 0; pinIndex--)
                {
                    PinData pinData = Minimap.instance.m_pins[pinIndex];
                    // Others orphaned pins is defined as:
                    // - it is from others,
                    // - it was not in the currently read MapTable,
                    // - the last time it was read its source MapTable is the currently read MapTable (for when player is reading 
                    //   others pins from more than one MapTable).
                    if (pinData.m_ownerID != 0L 
                        && !othersSeenPins.Contains(pinData)
                        && MapTablePatch.othersPinsDB.ContainsKey(pinData.m_pos.ToString())
                        && MapTablePatch.othersPinsDB[pinData.m_pos.ToString()] == mapTableId)
                    {
                        if ((bool)pinData.m_uiElement)
                        {
                            UnityEngine.Object.Destroy(pinData.m_uiElement.gameObject);
                        }
                        Minimap.instance.m_pins.RemoveAt(pinIndex);
                        MapTablePatch.othersPinsDB.Remove(pinData.m_pos.ToString());
                        changeDetected = true;
                    }
                }
            }

            // Anytime a MapTable is read cache to disk
            MapTablePatch.writeToFileCacheOthersPinsDB();

            // Instrumentation
            watch.Stop();
            logger.LogDebug("AddSharedMapData elapsed time: " + watch.ElapsedMilliseconds.ToString());
            return changeDetected;
        }

        /**
         *  Used when MapTable is written.
         *  Returned array will be the new state persisted in MapTable
         * 
         * Write to MapTable:
         *  - Basis is taken from current state of MapTable with own-white pins ignored (MapTable pins (minus) player's own pins)
         *  - The Minimap own-white pins are added applying the conflictRadius conflicts rule (removing any others-grey pins
         *    or conflicting other own-white pins, see limitations). Among conflicting own-white pins the last in the for loop wins.
         *  - The final state is written to the MapTable
         */
        [HarmonyPrefix]
        [HarmonyPatch("GetSharedMapData")]
        static bool GetSharedMapData_PrefixPatch(ref byte[] __result, byte[] oldMapData)
        {
            // Instrumentation
            logger.LogDebug("GetSharedMapData prefix patch starts.");
            Stopwatch watch = new Stopwatch();
            watch.Start();

            List<bool> mapTableExplored = null;
            ZPackage currentMapTable = null;
            if (oldMapData != null)
            {
                currentMapTable = new ZPackage(oldMapData);
                int version = currentMapTable.ReadInt();
                mapTableExplored = Minimap.instance.ReadExploredArray(currentMapTable, version);
            }
            ZPackage zPackage2 = new ZPackage();
            zPackage2.Write(2);
            zPackage2.Write(Minimap.instance.m_explored.Length);
            for (int i = 0; i < Minimap.instance.m_explored.Length; i++)
            {
                bool flag = Minimap.instance.m_explored[i];
                if (mapTableExplored != null)
                {
                    flag |= mapTableExplored[i];
                }
                zPackage2.Write(flag);
            }

            // Traverse MapTable Pins from remote state to identify others newPins
            // Only others player pins are considered
            long playerID = Player.m_localPlayer.GetPlayerID();
            List<PinData> newPins = new List<PinData>();
            if (currentMapTable != null)
            {
                int nbPins = currentMapTable.ReadInt();
                for (int k = 0; k < nbPins; k++)
                {
                    long num4 = currentMapTable.ReadLong();
                    string text = currentMapTable.ReadString();
                    Vector3 pos = currentMapTable.ReadVector3();
                    PinType type = (PinType)currentMapTable.ReadInt();
                    bool isChecked = currentMapTable.ReadBool();
                    if (num4 == playerID)
                    {
                        // Ignore pin as it is player's own from MapTable, MiniMap will take precedence for those
                        continue;
                    }
                    else
                    {
                        // Others pin
                        PinData pinData = new PinData();
                        pinData.m_type = type;
                        pinData.m_name = text;
                        pinData.m_pos = pos;
                        pinData.m_checked = isChecked;
                        pinData.m_ownerID = num4;
                        newPins.Add(pinData);
                    }
                }
            }

            // Traverse Minimap pins to identify newPins
            // Only player's own pins are considered
            foreach (PinData pin2 in Minimap.instance.m_pins)
            {
                if (pin2.m_save && pin2.m_type != PinType.Death
                    && pin2.m_ownerID == 0L)
                {
                    List<PinData> pinsInRange = PinsInRange(pin2.m_pos, ref newPins);
                    if (pinsInRange.Count > 0)
                    {
                        foreach(PinData pin in pinsInRange)
                        {
                            newPins.Remove(pin);
                            // TODO: see limitation for game created pins.
                        }
                    }
                    newPins.Add(pin2);
                }
            }

            // Compute size of final newPins and write to Zpackage
            int num = newPins.Count();
            zPackage2.Write(num);

            // Traverse newpins and add them to Zpackage
            playerID = Player.m_localPlayer.GetPlayerID();
            foreach (PinData pin2 in newPins)
            {
                long data = ((pin2.m_ownerID != 0L) ? pin2.m_ownerID : playerID);
                zPackage2.Write(data);
                zPackage2.Write(pin2.m_name);
                zPackage2.Write(pin2.m_pos);
                zPackage2.Write((int)pin2.m_type);
                zPackage2.Write(pin2.m_checked);
            }

            // Set the patched return arrray
            __result = zPackage2.GetArray();

            // Instrumentation
            watch.Stop();
            logger.LogDebug("GetSharedMapData elapsed time: " + watch.ElapsedMilliseconds.ToString());

            // Skip the original method
            return false;
        }

        public static List<PinData> PinsInRange(Vector3 pos, ref List<PinData> pinsCollection)
        {
            List<PinData> pins = new List<PinData>();
            float radius = conflictRadius;
            foreach (PinData pin in pinsCollection)
            {
                float distance = Utils.DistanceXZ(pos, pin.m_pos);
                if (Utils.DistanceXZ(pos, pin.m_pos) < radius)
                {
                    pins.Add(pin);
                }
            }
            return pins;
        }
    }
}

   
