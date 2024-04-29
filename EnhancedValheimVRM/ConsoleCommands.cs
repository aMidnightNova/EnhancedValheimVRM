

namespace EnhancedValheimVRM
{
    public class ConsoleCommands
    {
        public static readonly Terminal.ConsoleCommand ReloadSettings;
        public static readonly Terminal.ConsoleCommand ReloadGlobalSettings;

        static ConsoleCommands()
        {
            ReloadSettings = new Terminal.ConsoleCommand(
                "reload_settings",
                "reload VRM settings for your character",
                args =>
                {
                    VrmController.GetVrmInstance(Player.m_localPlayer).ReloadSettings();

                    args.Context.AddString("Settings for " + Player.m_localPlayer.name + " were reloaded");
                }
            );

            // probably not relevant anymore. using the bepinex config for global settings.

            //  ReloadGlobalSettings = new Console.ConsoleCommand(
            //     "reload_global_settings",
            //     "reload global VRM settings",
            //     args =>
            //     {
            //         Settings.ReloadGlobalSettings();
            //     
            //         args.Context.AddString("Global settings were reloaded");
            //     }
            // );
        }
    }
}