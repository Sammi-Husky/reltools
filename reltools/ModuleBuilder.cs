using BrawlLib.Internal;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.SSBB.Types;
using Newtonsoft.Json;
using reltools.json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using reltools.Symbols;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace reltools
{
    public class SDefLine
    {
        public int section;
        public string data;
        public uint offset;
    }
    internal class ModuleBuilder
    {
        public ModuleBuilder(string jsonFilepath, SymbolMap symbolMap = null)
        {
            string json = File.ReadAllText(jsonFilepath);
            Info = (RelInfo)JsonConvert.DeserializeObject(json, typeof(RelInfo));

            SymbolMap = symbolMap ?? new SymbolMap();
            RootPath = Path.GetDirectoryName(jsonFilepath);
            LocalLabels = new Dictionary<string, SDefLine>();
            Sections = new SortedList<int, SDefLine[]>(Info.Sections.Count);
            Tags = new List<(SDefLine, RelTag)>();
        }

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
        /// Relocation tags and the lines they belong to
        /// </summary>
        private List<(SDefLine, RelTag)> Tags { get; set; }
        /// <summary>
        /// Information about the rel file to build
        /// </summary>
        public RelInfo Info { get; set; }
        /// <summary>
        /// Root directory of rel project to build
        /// </summary>
        public string RootPath { get; set; }
        public static void BuildRel(string jsonFilepath, string outputFolder, SymbolMap map = null)
        {
            var builder = new ModuleBuilder(jsonFilepath, map);
            builder.BuildRel(outputFolder);
        }
        public void BuildRel(string outputFolder)
        {
            Console.WriteLine($"Building: {Info.Name}");
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
            Console.WriteLine("    Parsing section definitions");
            for (int i = 0; i < Info.Sections.Count; i++)
            {
                // even empty sections need a manager or brawllib will throw a fit
                ModuleSectionNode s = new ModuleSectionNode(0)
                {
                    _manager = new RelocationManager(null)
                };

                var sectionInfo = Info.Sections[i];
                if (sectionInfo != null)
                {
                    Console.WriteLine($"    Building: {sectionInfo.Path}");

                    s.ExpandSection = sectionInfo.Expand;
                    s._endBufferSize = sectionInfo.ExpandSize;
                    s._isBSSSection = sectionInfo.IsBSS;
                    s._isCodeSection = sectionInfo.HasCode;

                    string sectionPath = Path.Combine(RootPath, sectionInfo.Path);
                    string asm = File.ReadAllText(sectionPath);

                    // compiles asm source and returns
                    // GAS Listing + section bytes
                    string listing = Compile(asm, out byte[] sectionData);

                    // processes the listing file to gather
                    // labels and reltags for this section
                    ProcessListing(listing, i);

                    // if compilation succeeded, write section data to rel
                    if (sectionData != null)
                    {
                        s.InitBuffer((uint)sectionData.Length, new DataSource(new MemoryStream(sectionData)).Address);
                    }
                    else
                    {
                        Console.WriteLine("    Error building section!");
                    }
                }
                n._sections[i] = s;
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
            n.Dispose();
        }
        /// <summary>
        /// Compiles GNU assembly code input and generates 
        /// a listing file for further processing.
        /// </summary>
        /// <param name="asm">GNU Assembly source code</param>
        /// <param name="sectionData">compiled machine code bytes</param>
        /// <returns>GAS Listing</returns>
        private string Compile(string asm, out byte[] sectionData)
        {
            StringBuilder err = new StringBuilder();
            sectionData = null;

            // comment out reltags since GAS since
            // GAS will error encountering them
            asm = Regex.Replace(asm, @"(\[.*)", @"#$1");

            // convert tabs to spaces
            asm = asm.Replace("\t", "    ");

            // write modified source to temp file
            var tmpSrc = Path.GetTempFileName();
            var tmpBin = Path.GetTempFileName();
            var tmpOut = Path.GetTempFileName();
            File.WriteAllText(tmpSrc, asm);

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lib/powerpc-eabi-as.exe",
                    Arguments = $"-mgekko -mregnames -al --listing-rhs-width=200 \"{tmpSrc}\" -o \"{tmpBin}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var listing = proc.StandardOutput.ReadToEnd();
            err.AppendLine(proc.StandardError.ReadToEnd());
            proc.WaitForExit();

            // call objcopy
            err.AppendLine(Util.StartProcess("lib/powerpc-eabi-objcopy.exe", $"-O binary \"{tmpBin}\" \"{tmpOut}\""));

            // if no errors, get file bytes
            string error = err.ToString();
            if (string.IsNullOrWhiteSpace(error))
            {
                sectionData = File.ReadAllBytes(tmpOut);
            }
            else
            {
                Console.WriteLine(err.ToString());
                return "";
            }

            return listing;
        }
        /// <summary>
        /// Gathers labels and Relocation tags 
        /// from GNU Assembly Listing
        /// </summary>
        /// <param name="listing">GAS Listing</param>
        /// <param name="sectionID">SectionID</param>
        public void ProcessListing(string listing, int sectionID)
        {
            var lines = listing.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x));

            int curOffset = 0;
            foreach (var line in lines.Select(x => x.Trim()).Reverse())
            {
                if (line.StartsWith("GAS"))
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

                // remove comments (this includes reltags)
                // so we can assume a line ending in ":" as a label.
                // Also handle reltags here
                if (asmLine.Contains("#"))
                {
                    if (asmLine.Contains("#["))
                    {
                        var tag = asmLine.Substring(asmLine.IndexOf('['), asmLine.IndexOf("]") - asmLine.IndexOf('[') + 1);
                        var t = (sdefLine, RelTag.FromString(tag));
                        Tags.Add(t);
                    }
                    asmLine = asmLine.Substring(0, asmLine.IndexOf("#")).Trim();
                }

                // if line is a label, add it to the local label map
                if (asmLine.EndsWith(":"))
                {
                    AddLabel(asmLine.Trim(':'), sdefLine);
                }
            }
        }
        private void AddLabel(string label, SDefLine line)
        {
            if (LocalLabels.ContainsKey(label))
                return;

            switch (label)
            {
                case "__entry":
                case "__exit":
                case "__unresolved":
                    LocalLabels.Add(label, line);
                    break;
                default:
                    LocalLabels.Add(SymbolUtils.MangleSymbol(Info.ModuleID, line.section, (int)line.offset, label), line);
                    break;
            }
        }
        private void AddCommandToSection(RelTag tag, SDefLine line, ModuleSectionNode section)
        {
            if (tag is null)
                return;

            string mangled = SymbolUtils.MangleSymbol((int)tag.TargetModule, tag.TargetSection, (int)line.offset, tag.Label);
            uint targetOffset;
            Symbol sym = SymbolMap.GetSymbol(tag.TargetModule, tag.TargetSection, tag.Label);
            if (sym != null)
            {
                targetOffset = sym.Offset;
            }
            else if (LocalLabels.ContainsKey(mangled))
            {
                // NOTE: specially handled symbols (entry, exit, unresolved)
                // CANNOT be referenced via reltag.
                targetOffset = LocalLabels[mangled].offset;
            }
            else
            {
                targetOffset = Convert.ToUInt32(tag.Label.Substring(4), 16);
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
    }
}
