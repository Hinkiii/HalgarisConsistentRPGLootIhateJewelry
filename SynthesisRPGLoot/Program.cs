﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using SynthesisRPGLoot.Analyzers;


namespace SynthesisRPGLoot
{
    class Program
    {
        private static Lazy<Settings.Settings> _lazySettings = null!;
        public static Settings.Settings Settings => _lazySettings.Value;

        private static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "Settings.json",
                    out _lazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "SynthesisRpgLoot.esp")
                .Run(args);
        }

        private static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {

            // Workaround for VR Support until Mutagen adds support for the 1.71 header in VR
            if (state.PatchMod.ModHeader.Stats.NextFormID <= 0x800)
            {
                state.PatchMod.ModHeader.Stats.NextFormID = 0x800;
            }
            
            Settings.RarityAndVariationDistributionSettings.ArmorSettings.RarityClasses.Sort();
            Settings.RarityAndVariationDistributionSettings.WeaponSettings.RarityClasses.Sort();
            
            ObjectEffectsAnalyzer objectEffectsAnalyzer = new(state);
            
            var armor = new ArmorAnalyzer(state, objectEffectsAnalyzer);
            var weapon = new WeaponAnalyzer(state, objectEffectsAnalyzer);
            
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            
            Console.WriteLine("Analyzing mod list");
            var th1 = new Thread(() => armor.Analyze());
            var th2 = new Thread(() => weapon.Analyze());
            
            th1.Start();
            th2.Start();
            th1.Join();
            th2.Join();

            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine("Calculating Output Statistics For Comparison:");
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine("Armor Statistics:");
            armor.CalculateStats();
            
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine("Weapon Statistics:");
            weapon.CalculateStats();
            
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            
            Console.WriteLine("Please consider the Calculations above when you think the next steps take too long.");
            Console.WriteLine("This patcher will take a long time on bigger setups.");
            Console.WriteLine("If your runtime reaches more than 40min or more than an hour, then you should dial down\n" +
                              "The amount of Variations per Rarity and maybe even the amount of Rarities you are using.\n" +
                              "Because long generation times will also mean longer loading times for the game\n" +
                              "and possible instabilities since the Engine wasn't made for such insanities.");
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            
            Console.WriteLine("Generating armor enchantments");
            armor.Generate();
            Console.WriteLine("Done Generating armor enchantments");
            
            Console.WriteLine("Generating weapon enchantments");
            weapon.Generate();
            Console.WriteLine("Done Generating weapon enchantments");
            
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");

            Console.WriteLine("\n" +
                              " _                 _     _____                _           _   \n" +
                              "| |               | |   /  __ \\              | |         | |  \n" +
                              "| |     ___   ___ | |_  | /  \\/_ __ ___  __ _| |_ ___  __| |  \n" +
                              "| |    / _ \\ / _ \\| __| | |   | '__/ _ \\/ _` | __/ _ \\/ _` |  \n" +
                              "| |___| (_) | (_) | |_  | \\__/\\ | |  __/ (_| | ||  __/ (_| |_ \n" +
                              "\\_____/\\___/ \\___/ \\__|  \\____/_|  \\___|\\__,_|\\__\\___|\\__,_(_)\n" +
                              "                                                              \n");
        }
    }
}