using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FixedMapTable
{
    [BepInPlugin("org.bepinex.plugins" + pluginName, pluginName, version)]
    public class FixedMapTablePlugin : BaseUnityPlugin
    {
        public const string version = "0.0.1";
        public const string pluginName = "fixedmaptable";
        public static Harmony harmony = new Harmony("mod" + pluginName);
        public static ManualLogSource logger;
        void Awake()
        {
            logger = Logger;
            logger.LogInfo("Applying harmony patches");
            harmony.PatchAll();
        }
    }
}
