using System;
using System.Linq;

namespace EnhancedValheimVRM
{
    public class ConsoleCommands
    {
        public static readonly Terminal.ConsoleCommand Vrm;

        private const string Usage =
            "/vrm outfit list | next | reload | generate | set <name>; /vrm toggle <mesh>; /vrm blend <shape> <1-100>; /vrm settings reload | auto on | auto off";

        private const string DevUsage = "Usage: /vrm dev on | off";

        // Test-spawning items and writing the weapon catalogue are developer actions: they
        // require the game's own cheat mode, like vanilla spawn commands.
        private static bool CheatsEnabled => Console.instance != null && Console.instance.IsCheatsEnabled();

        static ConsoleCommands()
        {
            // Chat strips its leading slash before looking up Terminal commands.
            Vrm = new Terminal.ConsoleCommand("vrm",
                Usage,
                args =>
                {
                    if (args.Args.Length < 2)
                    {
                        args.Context.AddString(Usage);
                        return;
                    }

                    // Developer-only research commands, disabled for release. Re-enable by uncommenting.
                    // if (args.Args.Length == 3 && args.Args[1].Equals("audit", StringComparison.OrdinalIgnoreCase) &&
                    //     args.Args[2].Equals("weapons", StringComparison.OrdinalIgnoreCase))
                    // {
                    //     if (!CheatsEnabled) { args.Context.AddString("Enable devcommands first."); return; }
                    //     GameItem.AuditWeapons();
                    //     args.Context.AddString("Writing weapon audit in the background.");
                    //     return;
                    // }

                    if (args.Args.Length >= 3 && args.Args[1].Equals("dev", StringComparison.OrdinalIgnoreCase))
                    {
                        bool twoHanded = args.Args.Length == 4 &&
                            args.Args[2].Equals("weapons", StringComparison.OrdinalIgnoreCase) &&
                            args.Args[3].Equals("twohanded", StringComparison.OrdinalIgnoreCase);
                        if (args.Args.Length != 3 && !twoHanded)
                        {
                            args.Context.AddString(DevUsage);
                            return;
                        }

                        if (args.Args[2].Equals("off", StringComparison.OrdinalIgnoreCase))
                            args.Context.AddString(VrmController.SetLocalEnabled(false));
                        else if (args.Args[2].Equals("on", StringComparison.OrdinalIgnoreCase))
                            args.Context.AddString(VrmController.SetLocalEnabled(true));
                        // else if (args.Args[2].Equals("weapons", StringComparison.OrdinalIgnoreCase))
                        //     args.Context.AddString(CheatsEnabled ? GameItem.SpawnWeapons(twoHanded) : "Enable devcommands first.");
                        // else if (args.Args[2].Equals("clearweapons", StringComparison.OrdinalIgnoreCase))
                        //     args.Context.AddString(CheatsEnabled ? GameItem.ClearTestWeapons() : "Enable devcommands first.");
                        else
                            args.Context.AddString(DevUsage);
                        return;
                    }

                    string group = args.Args[1];
                    string action = args.Args.Length > 2 ? args.Args[2] : "list";
                    if (group.Equals("settings", StringComparison.OrdinalIgnoreCase))
                    {
                        bool auto = args.Args.Length == 4 && action.Equals("auto", StringComparison.OrdinalIgnoreCase);
                        if (!auto && (args.Args.Length != 3 ||
                                !action.Equals("reload", StringComparison.OrdinalIgnoreCase)))
                        {
                            args.Context.AddString("Usage: /vrm settings reload | auto on | auto off");
                            return;
                        }

                        var player = Player.m_localPlayer;
                        var instance = player != null ? player.GetVrmInstance() : null;
                        if (instance == null)
                        {
                            args.Context.AddString("Load a character with a VRM first.");
                            return;
                        }

                        if (auto)
                        {
                            bool on = args.Args[3].Equals("on", StringComparison.OrdinalIgnoreCase);
                            if (!on && !args.Args[3].Equals("off", StringComparison.OrdinalIgnoreCase))
                            {
                                args.Context.AddString("Usage: /vrm settings auto on | off");
                                return;
                            }

                            args.Context.AddString(VrmController.SetAutoReload(player, on));
                            return;
                        }

                        instance.ReloadSettings();
                        args.Context.AddString(
                            "VRM settings reload requested; the previous avatar stays if loading fails.");
                        return;
                    }

                    if (!group.Equals("outfit", StringComparison.OrdinalIgnoreCase) &&
                        !group.Equals("toggle", StringComparison.OrdinalIgnoreCase) &&
                        !group.Equals("blend", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Context.AddString(Usage);
                        return;
                    }

                    var outfits = Player.m_localPlayer?.GetVrmInstance()
                        ?.GetGameObject()
                        ?.GetComponent<OutfitController>();
                    if (outfits == null)
                    {
                        args.Context.AddString("Load a character with a VRM first.");
                        return;
                    }

                    if (group.Equals("toggle", StringComparison.OrdinalIgnoreCase))
                    {
                        string part = string.Join(" ", args.Args.Skip(2)).Trim().Trim('"');
                        args.Context.AddString(outfits.Toggle(part) ? "Toggled: " + part : "Unknown mesh: " + part);
                        return;
                    }

                    if (group.Equals("blend", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Args.Length < 4 || !float.TryParse(args.Args.Last(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var weight) || float.IsNaN(weight) || weight < 1 || weight > 100)
                        {
                            args.Context.AddString("Usage: /vrm blend <blendshape> <1-100>");
                            return;
                        }

                        string shape = string.Join(" ", args.Args.Skip(2).Take(args.Args.Length - 3)).Trim().Trim('"');
                        args.Context.AddString(outfits.Override(shape, true, weight, true)
                            ? "Blendshape: " + shape + " = " + weight
                            : "Unknown blendshape: " + shape);
                        return;
                    }

                    if (action.Equals("generate", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Context.AddString(outfits.Generate());
                        return;
                    }

                    if (action.Equals("list", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Context.AddString("Current: " + outfits.CurrentName + "; outfits: " +
                            string.Join(", ", outfits.Names));
                        return;
                    }

                    if (action.Equals("reload", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Context.AddString(outfits.Reload());
                        return;
                    }

                    string name;
                    if (action.Equals("next", StringComparison.OrdinalIgnoreCase))
                    {
                        var names = outfits.Names.ToArray();
                        if (names.Length == 0)
                        {
                            args.Context.AddString("No valid outfits loaded.");
                            return;
                        }

                        name = names[(Array.IndexOf(names, outfits.CurrentName) + 1) % names.Length];
                    }
                    else if (action.Equals("set", StringComparison.OrdinalIgnoreCase) && args.Args.Length > 3)
                    {
                        name = string.Join(" ", args.Args.Skip(3)).Trim().Trim('"');
                        if (name.Length == 0)
                        {
                            args.Context.AddString("Usage: /vrm outfit set <name>");
                            return;
                        }
                    }
                    else
                    {
                        args.Context.AddString("Usage: /vrm outfit list | next | reload | set <name>");
                        return;
                    }

                    args.Context.AddString(outfits.Apply(name)
                        ? "Outfit: " + outfits.CurrentName
                        : "Unknown outfit: " + name);
                });
        }
    }
}
