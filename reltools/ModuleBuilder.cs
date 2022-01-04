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

            _info = (RelInfo)JsonConvert.DeserializeObject(json, typeof(RelInfo));
            _symbolMap = symbolMap ?? new SymbolMap();
            _rootPath = Path.GetDirectoryName(jsonFilepath);
            _symbolInfo = new Dictionary<string, SDefLine>();
            _sections = new SortedList<int, SDefLine[]>(_info.Sections.Count);
        }

        private SymbolMap SymbolMap { get => _symbolMap; set => _symbolMap = value; }
        private SymbolMap _symbolMap;
        private Dictionary<string, SDefLine> localLabels { get => _symbolInfo; set => _symbolInfo = value; }
        Dictionary<string, SDefLine> _symbolInfo;
        public SortedList<int, SDefLine[]> Sections { get => _sections; set => _sections = value; }
        SortedList<int, SDefLine[]> _sections;
        public RelInfo Info { get => _info; set => _info = value; }
        RelInfo _info;
        public string RootPath { get => _rootPath; set => _rootPath = value; }
        string _rootPath;

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
            foreach (var sectionInfo in Info.Sections)
            {
                if (sectionInfo is null)
                    continue;

                var sectionPath = Path.Combine(RootPath, sectionInfo.Path);
                SDefLine[] lines = GetSectionLines(sectionPath, sectionInfo.SectionID);
                Sections.Add(sectionInfo.SectionID, lines);
            }

            Console.WriteLine("    Generate label map");

            // generate labels
            ParseLabels();

            // prolog
            n._prologOffset = localLabels["__entry"].offset;
            n._prologSection = (byte)localLabels[$"__entry"].section;

            // epilog
            n._epilogOffset = localLabels["__exit"].offset;
            n._epilogSection = (byte)localLabels[$"__exit"].section;

            // unresolved
            n._unresolvedOffset = localLabels["__unresolved"].offset;
            n._unresolvedSection = (byte)localLabels[$"__unresolved"].section;

            // even empty sections need a manager or brawllib will throw a fit
            for (int i = 0; i < n.Sections.Length; i++)
            {
                var sectionInfo = Info.Sections[i];
                ModuleSectionNode s = new ModuleSectionNode(0)
                {
                    _manager = new RelocationManager(null)
                };

                if (sectionInfo != null)
                {
                    Console.WriteLine($"    Building: {sectionInfo.Path}");
                    s.ExpandSection = sectionInfo.Expand;
                    s._endBufferSize = sectionInfo.ExpandSize;
                    s._isBSSSection = sectionInfo.IsBSS;
                    s._isCodeSection = sectionInfo.HasCode;

                    foreach (SDefLine line in Sections[sectionInfo.SectionID])
                    {
                        if (line.data.Contains("["))
                        {
                            RelCommand cmd = ParseCommand(line, s);

                            if (cmd != null)
                            {
                                s._manager.SetCommand((int)line.offset / 4, cmd);
                            }

                            // no longer need relocation tag
                            line.data = line.data.Remove(line.data.LastIndexOf("["));
                        }
                    }

                    byte[] sectionData = Compile(string.Join("\n", Sections[sectionInfo.SectionID].Select(x => x.data)));
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
            n.Rebuild();

            n.Export(Path.Combine(outputFolder, $"{Info.Name}.rel"));

            Console.WriteLine("Exporting map file..");
            ExportMap(Path.Combine(outputFolder, $"{Info.Name}.map"));
            n.Dispose();
        }
        private RelCommand ParseCommand(SDefLine line, ModuleSectionNode section)
        {
            var raw = line.data.Substring(line.data.LastIndexOf("["));
            RelTag tag = RelTag.FromString(raw);

            if (tag is null)
                return null;

            string mangled = SymbolUtils.MangleSymbol((int)tag.TargetModule, tag.TargetSection, (int)line.offset, tag.Label);
            uint targetOffset;
            Symbol sym = SymbolMap.GetSymbol(tag.TargetModule, tag.TargetSection, tag.Label);
            if (sym != null)
            {
                targetOffset = sym.Offset;
            }
            else if (localLabels.ContainsKey(mangled))
            {
                // NOTE: specially handled symbols (entry, exit, unresolved)
                // CANNOT be referenced via reltag.
                targetOffset = localLabels[mangled].offset;
            }
            else
            {
                targetOffset = Convert.ToUInt32(tag.Label.Substring(4), 16);
            }

            RELLink link = new RELLink()
            {
                _section = Convert.ToByte(tag.TargetSection),
                _type = (RELLinkType)tag.Command,
                _value = targetOffset,
            };
            return new RelCommand(tag.TargetModule, section, link);
        }
        private static SDefLine[] GetSectionLines(string filepath, int section, in uint sectionOffset = 0)
        {
            var lines = File.ReadAllLines(filepath);
            List<SDefLine> output = new List<SDefLine>();
            uint curOffset = sectionOffset;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // handle .include ourselves since gnu assembler
                // can't process our files on it's own anyways
                if (line.StartsWith(".include"))
                {
                    var path = line.Substring(line.IndexOf('"')).Trim('"');
                    output.AddRange(GetSectionLines(path, section, in curOffset));

                    // so we don't add the line to our final output
                    continue;
                }

                SDefLine sdef = new SDefLine()
                {
                    section = section,
                    offset = curOffset,
                    data = line.Trim()
                };

                bool isReserved = line.EndsWith(":") || line.StartsWith(".");
                bool isComment = line.StartsWith("#") || line.StartsWith("//");
                bool isEmpty = string.IsNullOrWhiteSpace(line);

                if (!isReserved && !isComment && !isEmpty)
                {
                    curOffset += 4;
                }

                output.Add(sdef);
            }
            return output.ToArray();
        }
        private void ParseLabels()
        {
            foreach (var section in Sections)
            {
                foreach (SDefLine line in section.Value)
                {
                    string linedata = line.data.Trim();
                    if (line.data.EndsWith(":"))
                    {
                        string rawLabel = linedata.Substring(0, line.data.IndexOf(":"));

                        if (localLabels.ContainsKey(rawLabel))
                            continue;

                        switch (rawLabel)
                        {
                            case "__entry":
                            case "__exit":
                            case "__unresolved":
                                localLabels.Add(rawLabel, line);
                                break;
                            default:
                                localLabels.Add(SymbolUtils.MangleSymbol(Info.ModuleID, line.section, (int)line.offset, rawLabel), line);
                                break;
                        }
                    }
                }
            }
        }
        private void ExportMap(string filepath)
        {
            using (TextWriter writer = File.CreateText(filepath))
            {
                writer.WriteLine($".module {Info.ModuleID}");
                foreach (var section in Sections)
                {
                    writer.WriteLine($".section {section.Key}");
                    foreach (SDefLine line in localLabels.Values.Where(x => x.section == section.Key))
                    {
                        if (Info.Sections[section.Key].HasCode && line.data.StartsWith("loc_"))
                            continue;
                        writer.WriteLine($"{line.offset:X8} {line.data.TrimEnd(':')}");
                    }
                }
            }
        }
        private static byte[] Compile(string asm)
        {
            // asm source must end with newline or some data will be lost
            if (!asm.EndsWith("\n"))
                asm += "\n";

            var tmpSrc = Path.GetTempFileName();
            var tmpBin = Path.GetTempFileName();
            var tmpOut = Path.GetTempFileName();
            File.WriteAllText(tmpSrc, asm);
            StringBuilder sb = new StringBuilder();
            sb.Append(Util.StartProcess("lib/powerpc-eabi-as.exe", $"-mgekko -mregnames \"{tmpSrc}\" -o \"{tmpBin}\""));
            sb.Append(Util.StartProcess("lib/powerpc-eabi-objcopy.exe", $"-O binary \"{tmpBin}\" \"{tmpOut}\""));
            if (sb.Length > 0)
            {
                Console.WriteLine(sb.ToString());
            }

            if (File.Exists(tmpOut))
            {
                return File.ReadAllBytes(tmpOut);
            }
            else
            {
                Console.WriteLine("Failed to compile");
            }
            return null;
        }
    }
}
