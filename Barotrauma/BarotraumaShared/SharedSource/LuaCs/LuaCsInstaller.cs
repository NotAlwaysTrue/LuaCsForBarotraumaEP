
using System;
using System.IO;
using System.Linq;

namespace Barotrauma
{
    static partial class LuaCsInstaller
    {
        private static string[] trackingFiles = new string[]
        { 
            /* Barotrauma */
            "Barotrauma.dll", 
            "Barotrauma.deps.json", 
            "Barotrauma.pdb", 
            "BarotraumaCore.dll", 
            "BarotraumaCore.pdb",
            
            /* HarmonyX Package */
            "0Harmony.dll", 
            "Mono.Cecil.dll",
            "Sigil.dll",
            "Mono.Cecil.Mdb.dll", 
            "Mono.Cecil.Pdb.dll",
            "Mono.Cecil.Rocks.dll",
            "MonoMod.Backports.dll",
            "MonoMod.Core.dll",
            "MonoMod.ILHelpers.dll",
            "MonoMod.RuntimeDetour.dll",
            "MonoMod.Utils.dll",
            "MonoMod.Iced.dll",
            
            /* MoonSharp */
            "MoonSharp.Interpreter.dll", 
            "MoonSharp.VsCodeDebugger.dll",

            /* Microsoft SDKs */
            "Microsoft.CodeAnalysis.dll", 
            "Microsoft.CodeAnalysis.CSharp.dll",
            "Microsoft.CodeAnalysis.CSharp.Scripting.dll", 
            "Microsoft.CodeAnalysis.Scripting.dll",
            "Microsoft.Toolkit.Diagnostics.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "System.Reflection.Metadata.dll", 
            "System.Collections.Immutable.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",

            /* Assembly Script Dependencies */
            "Publicized/DedicatedServer.dll", 
            "Publicized/Barotrauma.dll", 
            "Publicized/BarotraumaCore.dll",
            
            /* Other NuGet Packages */
            "Basic.Reference.Assemblies.Net80.dll",
            "FluentResults.dll",
            "LightInject.dll",
            "OneOf.dll"
        };

        private static void CreateMissingDirectory()
        {
            Directory.CreateDirectory("Temp/Original");
            Directory.CreateDirectory("Temp/ToDelete");
            Directory.CreateDirectory("Temp/ToDelete/Publicized");
            Directory.CreateDirectory("Temp/Old");
            Directory.CreateDirectory("Temp/Old/Publicized");
            Directory.CreateDirectory("Publicized");
        }

    }
}
