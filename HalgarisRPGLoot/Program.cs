﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;


namespace HalgarisRPGLoot
{
    class Program
    {
        static Lazy<Settings> _LazySettings = null!;
        public static Settings Settings => _LazySettings.Value;

        static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "Settings.json",
                    out _LazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HalgariRpgLoot.esp")
                .Run(args);
        }

        private static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            //var armor  = new ArmorAnalyzer(state);
            var weapon = new WeaponAnalyzer(state);
            
            Console.WriteLine("Analyzing mod list");
            //var th1 = new Thread(() => armor.Analyze());
            var th2 = new Thread(() => weapon.Analyze());
            
            //th1.Start();
            th2.Start();
            //th1.Join();
            th2.Join();
            
            Console.WriteLine("Generating armor enchantments");
            //armor.Generate();
            
            Console.WriteLine("Generating weapon enchantments");
            weapon.newGenerate();
            
        }
    }
}