﻿using BrawlLib.Internal;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.SSBB.Types;
using Newtonsoft.Json;
using reltools.json;
using reltools.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NCalc;

namespace reltools
{
    public struct SDefLine
    {
        public int section;
        public string data;
        public uint offset;
    }
    internal class ModuleBuilder
    {
        public ModuleBuilder(string jsonFilepath, IEnumerable<string> defines = null)
        {
            string json = File.ReadAllText(jsonFilepath);

            this.Info = (RelInfo)JsonConvert.DeserializeObject(json, typeof(RelInfo));
            this.SymbolMap = SymbolManager.Map;
            this.Defines = defines ?? Array.Empty<string>();
            this.RootPath = Path.GetDirectoryName(jsonFilepath);
            this.LocalLabels = new Dictionary<string, SDefLine>();
            this.Tags = new List<(SDefLine, RelTag)>();
            this.LocalTags = new Dictionary<int, string>();
            this.LogOutput = new StringBuilder();
        }

        private StringBuilder LogOutput { get; set; }

        /// <summary>
        /// Symbol map used for external symbol resolution and
        /// overriding of locally generated labels
        /// </summary>
        private SymbolMap SymbolMap { get; set; }
        /// <summary>
        /// Map containing local symbol information from all sections
        /// </summary>
        private Dictionary<string, SDefLine> LocalLabels { get; set; }
        /// <summary>
        /// Symbol defines to pass to the assembler
        /// </summary>
        private IEnumerable<string> Defines { get; set; }
        /// <summary>
        /// Relocation tags and the lines they belong to
        /// </summary>
        private List<(SDefLine, RelTag)> Tags { get; set; }
        /// <summary>
        /// Map of line numbers to raw relocation tags
        /// </summary>
        private Dictionary<int, string> LocalTags { get; set; }
        /// <summary>
        /// Information about the rel file to build
        /// </summary>
        public RelInfo Info { get; set; }
        /// <summary>
        /// Root directory of rel project to build
        /// </summary>
        public string RootPath { get; set; }
        public static string BuildRel(string jsonFilepath, string outputFolder, IEnumerable<string> defines = null)
        {
            var builder = new ModuleBuilder(jsonFilepath, defines);
            return builder.BuildRel(outputFolder);
        }
        public string BuildRel(string outputFolder)
        {
            LogOutput.AppendLine($"Building: {Info.Name}");
            RELNode n = new RELNode
            {
                Name = Info.Name,
                _id = (uint)Info.ModuleID,
                _version = 3,
                _fixSize = (uint)Info.FixSize,
                _moduleAlign = (uint)Info.ModuleAlign,
                _bssAlign = (uint)Info.BSSAlign,
                _numSections = (uint)Info.Sections.Count,
                _sections = new ModuleSectionNode[Info.Sections.Count]
            };

            // parse sections
            LogOutput.AppendLine("    Parsing section definitions");
            for (int sectionID = 0; sectionID < Info.Sections.Count; sectionID++)
            {
                LocalTags.Clear();

                // even empty sections need a manager or brawllib will throw a fit
                ModuleSectionNode s = new ModuleSectionNode(0)
                {
                    _manager = new RelocationManager(null)
                };

                var sectionInfo = Info.Sections[sectionID];
                if (sectionInfo != null)
                {
                    LogOutput.AppendLine($"    Building: {sectionInfo.Path}");

                    s.ExpandSection = sectionInfo.Expand;
                    s._endBufferSize = sectionInfo.ExpandSize;
                    s._isBSSSection = sectionInfo.IsBSS;
                    s._isCodeSection = sectionInfo.HasCode;

                    string sectionPath = Path.Combine(RootPath, sectionInfo.Path);
                    string asm = GetSource(sectionPath);

                    // Transforms source so reltags are
                    // references into a dictionary. This
                    // gets around GAS listing line limit
                    asm = HandleReltags(asm);

                    // compiles asm source and returns
                    // GAS Listing + section bytes
                    byte[] sectionData = Compile(asm, this.Defines, out string listing);

                    // processes the listing file to gather
                    // labels and reltags for this section
                    ProcessListing(listing, sectionID);

                    // if compilation succeeded, write section data to rel
                    if (sectionData != null)
                    {
                        s.InitBuffer((uint)sectionData.Length, new DataSource(new MemoryStream(sectionData)).Address);
                    }
                    else
                    {
                        LogOutput.AppendLine("    Error building section!");
                    }
                }

                n._sections[sectionID] = s;
                n.AddChild(s);
            }

            // link all relocations
            foreach ((SDefLine line, RelTag tag) in Tags)
            {
                AddCommandToSection(tag, line, n.Sections[line.section]);
            }

            n._prologOffset = LocalLabels["__entry"].offset;
            n._prologSection = (byte)LocalLabels[$"__entry"].section;

            n._epilogOffset = LocalLabels["__exit"].offset;
            n._epilogSection = (byte)LocalLabels[$"__exit"].section;

            n._unresolvedOffset = LocalLabels["__unresolved"].offset;
            n._unresolvedSection = (byte)LocalLabels[$"__unresolved"].section;


            // build the rel file and export
            n.Rebuild();

            // Export rel and map
            n.Export(Path.Combine(outputFolder, $"{Info.Name}.rel"));
            ExportMap(Path.Combine(outputFolder, $"{Info.Name}.map"));

            // call dispose explicitely here to prevent
            // a strange race condition in brawllib
            //n.Dispose();
            return LogOutput.ToString();
        }

        private string HandleReltags(string asm)
        {
            var lines = asm.Split('\n').Select(x => x.Trim()).ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains("[") && !line.Contains("]"))
                    continue;

                Match m = RelTag.TagRegex.Match(line);
                if (m.Success)
                {
                    LocalTags.Add(i, m.Value);
                    lines[i] = RelTag.TagRegex.Replace(line, $"#!RT![{i}]");
                }

            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Compiles GNU assembly code input and generates 
        /// a listing file for further processing.
        /// </summary>
        /// <param name="asm">GNU Assembly source code</param>
        /// <param name="listing">compiled machine code bytes</param>
        /// <returns>Compiled machine code</returns>
        private byte[] Compile(string asm, IEnumerable<string> defines, out string listing)
        {
            listing = "";

            // remove all comments
            asm = Regex.Replace(asm, @"#(?!\!RT\!).*", "", RegexOptions.Compiled);

            // convert tabs to spaces
            asm = asm.Replace("\t", "    ");

            // write modified source to temp file
            var tmpSrc = Path.GetTempFileName();
            var tmpBin = Path.GetTempFileName();
            var tmpOut = Path.GetTempFileName();
            var tmpLst = Path.GetTempFileName();
            File.WriteAllText(tmpSrc, asm);

            string _defs = string.Join("", defines.Select(x => $"--defsym {x}=1"));

            // assemble source code + generate listing
            ProcResult asResult = Util.StartProcess("lib/powerpc-eabi-as.exe",
                                                    "-mgekko",
                                                    "-mregnames",
                                                    $"-alc=\"{tmpLst}\"",
                                                    "--listing-rhs-width=900",
                                                    $"{_defs}",
                                                    $"\"{tmpSrc}\"",
                                                    $"-o \"{tmpBin}\"");

            // copy instruction bytes to raw bin file
            ProcResult cpResult = Util.StartProcess("lib/powerpc-eabi-objcopy.exe",
                                                    $"-O binary",
                                                    $"\"{tmpBin}\"",
                                                    $"\"{tmpOut}\"");


            if (asResult.ExitCode == 0 && cpResult.ExitCode == 0)
            {
                listing = File.ReadAllText(tmpLst);
                if (!string.IsNullOrWhiteSpace(asResult.StandardOutput))
                {
                    LogOutput.AppendLine($"    [Assembler] {asResult.StandardOutput}");
                }
                return File.ReadAllBytes(tmpOut);
            }
            else
            {
                LogOutput.Append(asResult.StandardError);
                LogOutput.Append(cpResult.StandardError);
                Console.WriteLine(LogOutput.ToString());
                throw new Exception($"Failed to assemble source file");
            }
        }
        private byte[] Compile(string asm, out string listing)
        {
            return Compile(asm, Array.Empty<string>(), out listing);
        }
        /// <summary>
        /// Gathers labels and Relocation tags 
        /// from GNU Assembly Listing
        /// </summary>
        /// <param name="listing">GAS Listing</param>
        /// <param name="sectionID">SectionID</param>
        private void ProcessListing(string listing, int sectionID)
        {
            // GAS listing does not provide offsets for labels
            // so we iterate in reverse order and keep track
            // of the last known offset and assign to labels.
            var lines = listing.Split('\n').Select(x => x.Trim()).Reverse();
            int curOffset = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("GAS"))
                    continue;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // GAS leaves the .include line in 
                // after processing so we remove it
                if (line.Contains(".include"))
                    continue;

                // [lineNo] [offset] [hex] \t [asm]
                var lineParts = line.Split('\t')
                                    .Select(x => x.Trim())
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .ToArray();

                // blank lines in source code will 
                // only have a line number.
                if (lineParts.Length != 2)
                    continue;

                string lineInfo = lineParts[0];
                string asmLine = lineParts[1];

                // make sure line info actually
                // has more than just line number
                if (lineInfo.Contains(" "))
                {
                    curOffset = Convert.ToInt32(lineInfo.Split(' ')[1].Trim(), 16);
                }

                // sdef line contains unprocessed source line
                var sdefLine = new SDefLine()
                {
                    offset = (uint)curOffset,
                    data = asmLine,
                    section = sectionID
                };

                if (asmLine.StartsWith("#"))
                    continue;

                // remove comments (this includes reltags)
                // so we can assume a line ending in ":" as a label.
                // Also handle reltags here
                if (asmLine.Contains("#!RT!["))
                {
                    try
                    {
                        Match m = Regex.Match(asmLine, @"#!RT!\[(\d+)\]");
                        if (m.Success)
                        {
                            int index = Convert.ToInt32(m.Groups[1].Value);
                            string raw = LocalTags[index];
                            //var tag = raw.Substring(raw.IndexOf('['), raw.IndexOf("]") - raw.IndexOf('[') + 1);
                            var t = (sdefLine, RelTag.FromString(raw));
                            Tags.Add(t);
                        }

                    }
                    catch
                    {
                        throw new Exception($"Failed to parse RelTag\n{line}");
                    }
                }

                // remove comments if normal comment
                if (asmLine.Contains("#"))
                {
                    asmLine = asmLine.Substring(0, asmLine.IndexOf("#")).Trim();
                }

                // if line is a label, add it to the local label map
                if (asmLine.EndsWith(":"))
                {
                    // don't save local labels (all numbers)
                    string trimmed = asmLine.Trim(':');
                    if (!int.TryParse(trimmed, out _))
                    {
                        AddLabel(trimmed, sdefLine);
                    }
                }
            }
        }
        private void AddLabel(string label, SDefLine line)
        {
            string mangled = SymbolManager.MangleSymbol(Info.ModuleID, line.section, label);
            if (LocalLabels.ContainsKey(label) || LocalLabels.ContainsKey(mangled))
            {
                return;
            }

            switch (label)
            {
                case "__entry":
                case "__exit":
                case "__unresolved":
                    LocalLabels.Add(label, line);
                    break;
                default:
                    LocalLabels.Add(mangled, line);
                    break;
            }
        }
        private void AddCommandToSection(RelTag tag, SDefLine line, ModuleSectionNode section)
        {
            uint targetOffset;
            string mangled = SymbolManager.MangleSymbol((int)tag.TargetModule, tag.TargetSection, tag.Label);
            Symbol sym = SymbolMap.GetSymbol(tag.TargetModule, tag.TargetSection, tag.Label);

            // give local labels priority over symbol map
            if (LocalLabels.ContainsKey(mangled))
            {
                // NOTE: specially handled symbols (entry, exit, unresolved)
                // CANNOT be referenced via reltag.
                targetOffset = LocalLabels[mangled].offset;
            }
            else if (sym != null)
            {
                targetOffset = sym.Offset;
            }
            else if (tag.Label.StartsWith("loc_"))
            {
                targetOffset = Convert.ToUInt32(tag.Label.Substring(4), 16);
            }
            else
            {
                throw new Exception($"Couldn't resolve symbol for RelTag target: {tag.Label}");
            }

            if (!string.IsNullOrEmpty(tag.Expression))
            {
                try
                {
                    var str = targetOffset.ToString() + Util.HexNumbersToDecimal(tag.Expression);
                    Expression expr = new Expression(str);
                    targetOffset = Convert.ToUInt32(expr.Evaluate());
                }
                catch
                {
                    throw new Exception($"Could not evaluate expression for reltag symbol\n{tag}");
                }
            }

            RELLink link = new RELLink()
            {
                _section = (byte)tag.TargetSection,
                _type = (RELLinkType)tag.Command,
                _value = targetOffset,
            };
            var cmd = new RelCommand(tag.TargetModule, section, link);

            section._manager.SetCommand((int)line.offset / 4, cmd);
        }
        /// <summary>
        /// Exports a compatible symbol map from the symbol
        /// information gathered while building the rel.
        /// </summary>
        /// <param name="filepath"></param>
        private void ExportMap(string filepath)
        {
            // C# is extremely verbose sometimes..
            LocalLabels = LocalLabels.Reverse().ToDictionary(x => x.Key, x => x.Value);

            using (TextWriter writer = File.CreateText(filepath))
            {
                writer.WriteLine($".module {Info.ModuleID}");
                foreach (var section in Info.Sections)
                {
                    if (section is null)
                        continue;

                    writer.WriteLine($".section {section.SectionID}");
                    foreach (SDefLine line in LocalLabels.Values.Where(x => x.section == section.SectionID))
                    {
                        if (section.HasCode && line.data.Contains("loc_"))
                            continue;

                        writer.WriteLine($"{line.offset:X8} {line.data.TrimEnd(':')}");
                    }
                }
            }
        }
        private string GetSource(string filepath)
        {
            var baseDir = Path.GetDirectoryName(filepath);
            StringBuilder sb = new StringBuilder();
            using (var reader = File.OpenText(filepath))
            {
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    // handle includes by just copying the included
                    // file contents at the current location. This
                    // is functionally identical to GAS
                    if (line.Contains(".include"))
                    {
                        string includePath = Regex.Match(line, "\"([^\"]*)\"").Groups[1].Value;
                        sb.Append(GetSource(Path.Combine(baseDir, includePath)));
                        continue;
                    }

                    sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }
    }
}
