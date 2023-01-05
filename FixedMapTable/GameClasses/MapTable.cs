
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;


namespace FixedMapTable.GameClasses
{

    [HarmonyPatch(typeof(MapTable))]
    class MapTablePatch
    {
        private static ManualLogSource logger = FixedMapTablePlugin.logger;
        public static Dictionary<string, string> othersPinsDB;
        public static string currentWorld;

        /**
         * When MapTable is loaded try to populate the in-memory pins DB from cache file.
         */
        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        static void Start_PostfixPatch(ref ZNetView ___m_nview)
        {
            // Setup a per-world DB file
            if (currentWorld == null || currentWorld != ZNet.instance.GetWorldName())
            {
                currentWorld = ZNet.instance.GetWorldName();
                othersPinsDB = new Dictionary<string, string>();
                readFromFileCacheOthersPinsDB();
            }            
        }

        private static void readFromFileCacheOthersPinsDB()
        {
            string path = Path.Combine(Paths.CachePath, string.Format("FixedMapTablePinsDB_{0}.dat", currentWorld));

            if (File.Exists(path))
            {
                using (FileStream fs = new FileStream(path, FileMode.Open))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    othersPinsDB = (Dictionary<string, string>)formatter.Deserialize(fs);
                }
            }
        }

        public static void writeToFileCacheOthersPinsDB()
        {
            string path = Path.Combine(Paths.CachePath, string.Format("FixedMapTablePinsDB_{0}.dat", currentWorld));
            using (FileStream fs = new FileStream(path, FileMode.Create))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(fs, MapTablePatch.othersPinsDB);
            }
        }

        /**
         * Always return false to skip original.
         */
        [HarmonyPrefix]
        [HarmonyPatch("OnRead")]
        static bool OnRead_PrefixPatch(ref ZNetView ___m_nview, bool __result,
            Switch caller, Humanoid user, ItemDrop.ItemData item)
        {
            string mapTableUid = ___m_nview.GetZDO().m_uid.ToString();

            if (item != null)
            {
                __result = false;
                return false;
            }
            
            byte[] byteArray = ___m_nview.GetZDO().GetByteArray("data");
            if (byteArray != null)
            {
                byte[] dataArray = Utils.Decompress(byteArray);
                if (MinimapPatch.AddSharedMapData(dataArray, mapTableUid))
                {
                    user.Message(MessageHud.MessageType.Center, "$msg_mapsynced");
                }
                else
                {
                    user.Message(MessageHud.MessageType.Center, "$msg_alreadysynced");
                }
            }
            else
            {
                user.Message(MessageHud.MessageType.Center, "$msg_mapnodata");
            }
            __result = false;
            return false;
        }

    }

}

   
