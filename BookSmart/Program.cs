using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;

namespace BookSmart
{
    public class Program
    {
        // Settings
        static Lazy<Settings> LazySettings = new Lazy<Settings>();
        static Settings settings => LazySettings.Value;
        
        // Initial setup
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "settings.json",
                    out LazySettings
                )
                .SetTypicalOpen(GameRelease.SkyrimSE, "WeightlessThings.esp")
                .Run(args);
        }

        // Let's get to work!
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // If quest labels are enabled, create the Quest Book cache first
            List<String> questBookCache = new();
            if (settings.addQuestLabels) { questBookCache = CreateQuestBookCache(state); }
            
            // Iterate all winning books from the load order
            foreach (var book in state.LoadOrder.PriorityOrder.OnlyEnabled().Book().WinningOverrides())
            {
                // If the book has no name, skip it
                if (book.Name == null) { continue; }

                // Store our new tags
                List<String> newTags = new();

                // Add Skill labels
                if (settings.addSkillLabels && book.Teaches is IBookSkillGetter skillTeach)
                {
                    var skillLabel = GetSkillLabelName(book);
                    if (skillLabel is not null) { newTags.Add(skillLabel); }
                }

                //Add Map Marker labels
                if (settings.addMapMarkerLabels && (book.VirtualMachineAdapter is not null && book.VirtualMachineAdapter.Scripts.Count > 0)                    )
                {
                    var mapMarkerLabel = GetMapLabelName(book);
                    if (mapMarkerLabel is not null) { newTags.Add(mapMarkerLabel); }
                }

                // Add Quest labels
                if (settings.addQuestLabels)
                {
                    var questLabel = GetQuestLabelName(book, questBookCache);
                    if (questLabel is not null) { newTags.Add(questLabel); }
                }

                // If we don't have any new tags, no need for an override record
                if (newTags.Count == 0) { continue; }

                // Actually create the override record
                var bookOverride = state.PatchMod.Books.GetOrAddAsOverride(book);
                
                // Special handling for a labelFormat of Star
                if (settings.labelFormat == Settings.LabelFormat.Étoile)
                {
                    switch (settings.labelPosition) {
                        case Settings.LabelPosition.Avant: { bookOverride.Name = $"*{book.Name.ToString()}"; break; }
                        case Settings.LabelPosition.Après: { bookOverride.Name = $"{book.Name.ToString()}*"; break; }
                        default: throw new NotImplementedException("Vous avez défini une position de label qui n'est pas supportée.");
                    }
                }
                // All other labelFormats
                else
                {
                    bookOverride.Name = GetLabel(book.Name.ToString()!, String.Join("/", newTags));
                }

                // Console output
                Console.WriteLine($"{book.FormKey}: '{book.Name}' -> '{bookOverride.Name}'");
            };
        }

        
        public static string GetLabel(string existingName, string newLabel)
        {
            // set the open and close characters that go around the skill name
            string open = "";
            string close = "";
            switch (settings.encapsulatingCharacters)
            {
                case Settings.EncapsulatingCharacters.Chevrons: { open = "<"; close = ">"; break; }
                case Settings.EncapsulatingCharacters.Accolades: { open = "{"; close = "}"; break; }
                case Settings.EncapsulatingCharacters.Parenthèses: { open = "("; close = ")"; break; }
                case Settings.EncapsulatingCharacters.Crochets: { open = "["; close = "]"; break; }
                case Settings.EncapsulatingCharacters.Étoiles: { open = "*"; close = "*"; break; }
                default: throw new NotImplementedException("Vous avez défini des caractères d'encapsulation qui ne sont pas pris en charge.");
            }

            return settings.labelPosition switch
            {
                Settings.LabelPosition.Avant => $"{open}{newLabel}{close} {existingName}",
                Settings.LabelPosition.Après => $"{existingName} {open}{newLabel}{close}",
                _ => throw new NotImplementedException("Vous avez défini une position du tag qui n'est pas supportée.")
            };
        }
        
        public static List<string> CreateQuestBookCache(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Console.WriteLine("--------------------------------------------------------------------");
            Console.WriteLine("Je feuillette la bibliothèque des quêtes à la recherche de livres, veuillez patienter...");
            Console.WriteLine("--------------------------------------------------------------------");

            List<string> questBookCache = new List<String>();

            // Search all quests
            foreach (var quest in state.LoadOrder.PriorityOrder.OnlyEnabled().Quest().WinningOverrides())
            {
                if ((quest.Aliases is not null) && (quest.Aliases.Count > 0))
                {
                    // Examine each alias
                    foreach (var alias in quest.Aliases)
                    {
                        // CreateReferenceToObject alias
                        if (alias.CreateReferenceToObject is not null)
                        {
                            // try to resolve the quest object to the actual records
                            if (alias.CreateReferenceToObject.Object.TryResolve<IBookGetter>(state.LinkCache, out var questObject))
                            {
                                //Console.WriteLine($"{quest.FormKey}: '{questObject.FormKey}' is used in quest '{quest.Name}'");
                                questBookCache.Add(questObject.FormKey.ToString());
                            }
                        }

                        // Items alias
                        if (alias.Items is not null)
                        {
                            // try to resolve the quest object ot the actual records
                            foreach (var item in alias.Items)
                            {
                                // try to resolve the quest object to the actual records
                                // item.item.item.item
                                if (item.Item.Item.TryResolve<IBookGetter>(state.LinkCache, out var questObject))
                                {
                                    //Console.WriteLine($"{quest.FormKey}: '{questObject.FormKey}' is used in quest '{quest.Name}'");
                                    questBookCache.Add(questObject.FormKey.ToString());
                                }
                            }
                        }
                    }
                }
            }

            return questBookCache;
        }


        public static string? GetSkillLabelName(IBookGetter book)
        {
            if (book.Teaches is not IBookSkillGetter skillTeach) return null;
            if (skillTeach.Skill == null) return null;
            if ((int)skillTeach.Skill == -1) return null;

            // Label Format: Long
            if (settings.labelFormat == Settings.LabelFormat.Long)
            {
                return skillTeach.Skill switch
                {
                    Skill.Alchemy => "Alchimie",
                    Skill.Alteration => "Altération",
                    Skill.Archery => "Archerie",
                    Skill.Block => "Parade",
                    Skill.Conjuration => "Conjuration",
                    Skill.Destruction => "Destreuction",
                    Skill.Enchanting => "Enchantement",
                    Skill.Illusion => "Illusion",
                    Skill.Lockpicking => "Crochetage",
                    Skill.Pickpocket => "Vol à la tir",
                    Skill.Restoration => "Guérison",
                    Skill.Smithing => "Forgeage",
                    Skill.Sneak => "Furtivité",
                    Skill.Speech => "Éloquence",
                    Skill.HeavyArmor => "Armure lourde",
                    Skill.LightArmor => "Armure légère",
                    Skill.OneHanded => "Une main",
                    Skill.TwoHanded => "Deux mains",
                    _ => skillTeach.Skill.ToString()
                };
            }
            // Label Format: Short
            else if (settings.labelFormat == Settings.LabelFormat.Court)
            {
                return skillTeach.Skill switch
                {
                    Skill.Alchemy => "Alch",
                    Skill.Alteration => "Altr",
                    Skill.Archery => "Arch",
                    Skill.Block => "Pard",
                    Skill.Conjuration => "Conj",
                    Skill.Destruction => "Dest",
                    Skill.Enchanting => "Ench",
                    Skill.HeavyArmor => "Arm.L",
                    Skill.Illusion => "Illu",
                    Skill.LightArmor => "Arm.l",
                    Skill.Lockpicking => "Croch",
                    Skill.OneHanded => "1M",
                    Skill.Pickpocket => "Vol",
                    Skill.Restoration => "Guéri",
                    Skill.Smithing => "Forge",
                    Skill.Sneak => "Furti",
                    Skill.Speech => "Éloq",
                    Skill.TwoHanded => "2M",
                    _ => skillTeach.Skill.ToString()
                };
            }
            // Label Format: Star
            else if (settings.labelFormat == Settings.LabelFormat.Étoile)
            {
                return "*";
            }
            else
            {
                throw new NotImplementedException("Vous avez défini un format de tag qui n'est pas supporté.");
            }
        }


        public static string? GetMapLabelName(IBookGetter book)
        {
            // variables for use in this section
            if (book == null) { return null; }
            if (book.VirtualMachineAdapter == null) { return null; }

            foreach (var script in book.VirtualMachineAdapter.Scripts)
            {
                // Any script with MapMarker in the script name will do
                if (script.Name.Contains("MapMarker", StringComparison.OrdinalIgnoreCase))
                {
                    return settings.labelFormat switch
                    {
                        Settings.LabelFormat.Long => "Marqueur carte",
                        Settings.LabelFormat.Court => "Marqueur",
                        Settings.LabelFormat.Étoile => "*",
                        _ => throw new NotImplementedException("Vous avez défini un format de tag qui n'est pas supporté.")
                    };
                }
            }

            return null;
        }


        public static string? GetQuestLabelName(IBookGetter book, List<string> questBookCache)
        {
            bool isBookQuestRealted = false;

            // Check the Quest Book Cache
            
            if (questBookCache.Contains(book.FormKey.ToString()))
            {
                isBookQuestRealted = true;
            }
            // Check for quest-related Book scripts
            else if ((book.VirtualMachineAdapter is not null) &&
                    (book.VirtualMachineAdapter.Scripts is not null) &&
                    (book.VirtualMachineAdapter.Scripts.Count > 0))
            {
                foreach (var script in book.VirtualMachineAdapter.Scripts)
                {
                    if (script.Name.Contains("Quest", StringComparison.OrdinalIgnoreCase) || settings.assumeBookScriptsAreQuests)
                    {
                        Console.WriteLine($"{book.FormKey}: '{book.Name}' a un script de quête appelé '{script.Name}'.");
                        isBookQuestRealted = true;
                    }
                }
            }

            if (isBookQuestRealted)
            {
                return settings.labelFormat switch
                {
                    Settings.LabelFormat.Long => "Quête",
                    Settings.LabelFormat.Court => "Q",
                    Settings.LabelFormat.Étoile => "*",
                    _ => throw new NotImplementedException("Vous avez défini un format de tag qui n'est pas supporté.")
                };
            } else
            {
                return null;
            }
            
        }


        
    }
}
