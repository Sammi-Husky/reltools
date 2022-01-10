using BrawlLib.SSBB.ResourceNodes;
using reltools.Symbols;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace reltools
{
    public enum APP_MODE
    {
        DUMP,
        BUILD,
        GENMAP,
        INVALID
    }
    public class Program
    {
        private static readonly List<string> targets = new List<string>();
        private static readonly List<string> mapfiles = new List<string>();
        private static readonly List<string> defsyms = new List<string>();

        private static string output = null;
        private static APP_MODE mode = APP_MODE.DUMP;
        private static SymbolMap map;

        public static void Main(string[] args)
        {
            if (args.Length < 1 || ParseArguments(args))
            {
                PrintHelp();
                return;
            }

            if (mapfiles.Count > 0)
            {
                map = SymbolMap.FromFiles(mapfiles.ToArray());
            }

            if (mode == APP_MODE.DUMP)
            {
                DumpTargets(output ?? "dump");
            }
            else if (mode == APP_MODE.BUILD)
            {
                BuildTargets(output ?? "build");
            }
            else if (mode == APP_MODE.GENMAP)
            {
                GenMapsForTargets(output ?? "maps");
            }
        }
        private static void DumpTargets(string outputFolder)
        {
            // create directory if it doesn't exist
            Directory.CreateDirectory(outputFolder);

            if (targets.Count == 1 && Directory.Exists(targets[0]))
            {
                var folder = targets[0];
                targets.Clear();
                targets.AddRange(GatherFiles(folder, "*.rel", true));
            }

            foreach (var target in targets)
            {
                RELNode node = (RELNode)NodeFactory.FromFile(null, target);
                ModuleDumper.DumpRel(node, Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(target)), map);

                // race condition where linked branches will be
                // null or 0 if we don't do this?????
                node.Dispose();
            }
        }
        private static void BuildTargets(string outputFolder)
        {
            // create directory if it doesn't exist
            Directory.CreateDirectory(outputFolder);

            if (targets.Count == 1 && Directory.Exists(targets[0]))
            {
                var folder = targets[0];
                targets.Clear();
                targets.AddRange(GatherFiles(folder, "*.json", true));
            }

            foreach (var target in targets)
            {
                ModuleBuilder.BuildRel(target, outputFolder, map, defsyms.ToArray());
            }
        }
        private static void GenMapsForTargets(string outputFolder)
        {
            // create directory if it doesn't exist
            Directory.CreateDirectory(outputFolder);

            if (targets.Count == 1 && Directory.Exists(targets[0]))
            {
                var folder = targets[0];
                targets.Clear();
                targets.AddRange(GatherFiles(folder, "*.rel", true));
            }

            foreach (var target in targets)
            {
                string mapFile = $"{Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(target))}.map";

                Console.WriteLine($"Generating map file for {target}");

                using (var writer = File.CreateText(mapFile))
                {
                    var node = (RELNode)NodeFactory.FromFile(null, target);
                    writer.Write(ModuleDumper.GenerateMap(node));

                    // race condition where linked branches will be
                    // null or 0 if we don't do this?????
                    node.Dispose();
                }
            }
        }
        /// <summary>
        /// Parses program arguments
        /// </summary>
        /// <param name="args"></param>
        /// <returns>True if args are invalid and program should exit</returns>
        private static bool ParseArguments(string[] args)
        {
            bool shouldExit = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        if (i + 1 >= args.Length)
                        {
                            shouldExit = true;
                        }
                        else
                        {
                            output = args[i + 1];
                            i++;
                        }
                        break;
                    case "-r":
                    case "--rebuild":
                        mode = APP_MODE.BUILD;
                        if (i + 1 >= args.Length)
                        {
                            shouldExit = true;
                        }
                        else
                        {
                            for (int x = i + 1; x < args.Length; x++)
                            {
                                if (args[x].StartsWith("-"))
                                    break;

                                if (args[x].EndsWith(".json"))
                                {
                                    targets.Add(args[x]);
                                    i++;
                                }
                                else if (Directory.Exists(args[x]))
                                {
                                    targets.AddRange(GatherFiles(args[x], "*.json", true));
                                    i++;
                                }
                            }
                        }
                        break;
                    case "-x":
                    case "--extract":
                        mode = APP_MODE.DUMP;
                        if (i + 1 >= args.Length)
                        {
                            shouldExit = true;
                        }
                        else
                        {
                            for (int x = i + 1; x < args.Length; x++)
                            {
                                if (args[x].StartsWith("-"))
                                    break;

                                if (args[x].EndsWith(".rel"))
                                {
                                    targets.Add(args[x]);
                                    i++;
                                }
                                else if (Directory.Exists(args[x]))
                                {
                                    targets.AddRange(GatherFiles(args[x], "*.rel", true));
                                    i++;
                                }
                            }
                        }
                        break;
                    case "-m":
                    case "--map":
                        if (i + 1 >= args.Length)
                        {
                            shouldExit = true;
                        }
                        else
                        {
                            for (int x = i + 1; x < args.Length; x++)
                            {
                                if (args[x].StartsWith("-"))
                                    break;

                                if (args[x].EndsWith(".map"))
                                {
                                    mapfiles.Add(args[x]);
                                    i++;
                                }
                                else if (Directory.Exists(args[x]))
                                {
                                    mapfiles.AddRange(GatherFiles(args[x], "*.map", true));
                                    i++;
                                }
                            }
                        }
                        break;
                    case "-g":
                    case "--genmap":
                        mode = APP_MODE.GENMAP;
                        break;
                    case "-d":
                    case "--def":
                        if (i + 1 >= args.Length)
                        {
                            shouldExit = true;
                        }
                        else
                        {
                            defsyms.Add(args[++i]);
                        }
                        break;
                    default:
                        targets.Add(args[i]);
                        break;
                }
            }

            if (targets.Count == 0)
            {
                shouldExit = true;
            }
            return shouldExit;
        }
        private static void PrintHelp()
        {
            Console.WriteLine("reltools: Copyright (c) SammiHusky 2021-2022");
            Console.WriteLine("options:");
            Console.WriteLine("  -x, --extract [file(s)/folder]: (default)");
            Console.WriteLine("      Extracts and dissassembles rel file(s) to text files.");
            Console.WriteLine("  -r, --rebuild:");
            Console.WriteLine("      Rebuilds rel file(s) from source files when given a json target.");
            Console.WriteLine("  -m, --map [file(s)/folder]:");
            Console.WriteLine("      Specifies map file(s) used for resolving symbols.");
            Console.WriteLine("  -g, --genmap:");
            Console.WriteLine("      Generates a map file from the target(s)");
            Console.WriteLine("  -o, --output [path]:");
            Console.WriteLine("      Sets output directory");
            Console.WriteLine("  -D, --def [symbol]:");
            Console.WriteLine("      Defines 'symbol' and passes it to the assembler when rebuilding. " +
                              "      Example: \"-D Release\" will define symbol \"Release\". Used in conditional statements(.ifdef/.endif)");
            Console.WriteLine("\nUsage: reltools.exe [options] [targets]");
        }
        private static string[] GatherFiles(string directory, string pattern, bool recursive)
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(directory, pattern, option).ToArray();
        }
    }
}