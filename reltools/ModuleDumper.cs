using BrawlLib.Internal;
using BrawlLib.Internal.PowerPCAssembly;
using BrawlLib.SSBB.ResourceNodes;
using Newtonsoft.Json;
using reltools.json;
using reltools.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace reltools
{
    internal class ModuleDumper
    {
        public ModuleDumper(RELNode node, SymbolMap map = null)
        {
            _node = node;
            LogOutput = new StringBuilder();
            _labelMap = map ?? new SymbolMap();
            _labelMap.AddSymbol(node.ModuleID, (int)node.PrologSection, new Symbol(node._prologOffset, "__entry"), true);
            _labelMap.AddSymbol(node.ModuleID, (int)node.EpilogSection, new Symbol(node._epilogOffset, "__exit"), true);
            _labelMap.AddSymbol(node.ModuleID, (int)node.UnresolvedSection, new Symbol(node._unresolvedOffset, "__unresolved"), true);
        }
        private static readonly string[] SectionNames = new string[]
        {
            "init",
            "text",
            "ctors",
            "dtors",
            "rodata",
            "data",
            "bss"
        };
        public SymbolMap LabelMap
        {
            get => _labelMap;
            private set => _labelMap = value;
        }
        private SymbolMap _labelMap;
        private RELNode Node
        {
            get => _node;
            set => _node = value;
        }
        private RELNode _node;

        private StringBuilder LogOutput { get; set; }

        #region static methods
        public static string DumpRel(RELNode node, string outputFolder)
        {
            ModuleDumper dumper = new ModuleDumper(node);
            return dumper.DumpRel(outputFolder);
        }
        public static string DumpRel(RELNode node, string outpuFolder, SymbolMap labelMap)
        {
            ModuleDumper dumper = new ModuleDumper(node, labelMap);
            return dumper.DumpRel(outpuFolder);
        }
        public static unsafe string GenerateMap(RELNode node)
        {
            StringBuilder sb = new StringBuilder();
            var addr = node.WorkingUncompressed.Address;
            sb.AppendLine($".module {node.ModuleID}");
            foreach (var section in node.Sections)
            {
                if (section.UncompressedSize == 0)
                    continue;

                sb.AppendLine($".section {section.Index}");
                if (section.IsBSS)
                {
                    addr = section._dataBuffer.Address;
                }
                for (int i = 0; i < section.UncompressedSize / 4; i++)
                {
                    uint data = *(buint*)(addr + i * 4);
                    var linked = section._manager.GetLinked(i);
                    var branched = section._manager.GetBranched(i);
                    if (linked != null || branched != null)
                    {
                        sb.AppendLine($"{i * 4:X8} loc_{i * 4:X}");
                    }
                }
            }
            return sb.ToString();
        }
        private static string[] GetAsm(ModuleSectionNode node)
        {
            var tmpPath = Path.GetTempFileName();
            node.Export(tmpPath);
            string[] output = null;
            string tmp = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // use vdappc to disassemble the section data
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Path.GetDirectoryName(tmp), "lib/vdappc.exe"),
                    Arguments = $"{tmpPath} 0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            string asm = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            output = asm.Split('\n')
                  .Where(x => !string.IsNullOrWhiteSpace(x))
                  .Select(x => x.Trim())
                  .ToArray();

            // convert tab to space
            return output.Select(x => x.Replace('\t', ' ')).ToArray();
        }
        #endregion

        #region instance methods
        public string DumpRel(string outputFolder)
        {
            // create our output directory if it doesn't exist.
            Directory.CreateDirectory(outputFolder);

            string relname = Path.GetFileNameWithoutExtension(Node.FileName);

            // write module info file
            var relinfo = new RelInfo()
            {
                Name = relname,
                Type = "rel",
                ModuleID = (int)Node.ModuleID,
                ModuleAlign = (int)Node.ModuleAlign,
                BSSAlign = (int)Node.BSSAlign,
                FixSize = (int)Node.FixSize,
                Sections = new List<RelSection>(Node.Sections.Length)
            };

            LogOutput.AppendLine($"Unpacking: {Node.FilePath}:");
            for (int i = 0; i < Node.Sections.Length; i++)
            {
                var section = Node.Sections[i];

                string sectionFN = $"Section[{i}].asm";
                // While it would be more correct to name the sections
                // after what they really are, it would be confusing
                // for brawl modders to sudenly switch after all this time
                //if (i < SectionNames.Length)
                //    sectionFN = $"{SectionNames[i]}.asm";

                string sectionFP = Path.Combine(outputFolder, sectionFN);

                // only populate the section info if it's
                // not a null section.
                if (section.WorkingUncompressed.Length == 0)
                {
                    relinfo.Sections.Add(null);
                    continue;
                }

                var sectionInfo = new RelSection()
                {
                    Path = sectionFN,
                    SectionID = i,
                    HasCode = section.HasCode,
                    IsBSS = section.IsBSS,
                    ExpandSize = section.EndBufferSize
                };
                relinfo.Sections.Add(sectionInfo);

                LogOutput.AppendLine($"    {sectionFP}");

                // dump sections to file
                if (section.HasCode)
                {
                    DumpCode(section, sectionFP);
                }
                else
                {
                    DumpSection(section, sectionFP);
                }
            }

            using (var writer = File.CreateText(Path.Combine(outputFolder, $"{relname}.json")))
            {
                writer.Write(JsonConvert.SerializeObject(relinfo, Formatting.Indented));
            }
            return LogOutput.ToString();
        }
        private unsafe void DumpSection(ModuleSectionNode node, string filepath)
        {
            using (var writer = File.CreateText(filepath))
            {
                StringBuilder sb = new StringBuilder();
                var addr = node.WorkingUncompressed.Address;

                if (node.IsBSS)
                {
                    addr = node._dataBuffer.Address;
                }

                for (int i = 0; i < node.UncompressedSize / 4; i++)
                {
                    uint data = *(buint*)(addr + i * 4);
                    var command = node._manager.GetCommand(i);
                    var linked = node._manager.GetLinked(i);
                    Symbol sym = LabelMap.GetSymbol(node.ModuleID, node.Index, (uint)i * 4);
                    //bool needsAlign = false;

                    // if this location is referenced write the label for it
                    if (sym != null)
                    {
                        sb.AppendLine($"{sym.Name}:");
                    }
                    else if (linked != null)
                    {
                        sb.AppendLine($"loc_{i * 4:X}:");
                    }

                    string dataStr = $"    .4byte 0x{data:X8}";

                    //// is data at addr a string?
                    //byte tmp = *(byte*)(addr + i * 4);
                    //if (Util.IsAscii(tmp))
                    //{
                    //    int x = 0;
                    //    List<char> chars = new List<char>();
                    //    while (tmp != 0 && Util.IsAscii(tmp))
                    //    {
                    //        chars.Add((char)tmp);
                            
                    //        x++;
                    //        tmp = *(byte*)(addr + x + (i * 4));
                    //    }

                    //    // threshold
                    //    if (chars.Count >= 4)
                    //    {
                    //        dataStr = $"    .asciz \"{new string(chars.ToArray())}\"";
                    //        if (x % 4 > 0)
                    //        {
                    //            x = x.RoundUp(4);
                    //            needsAlign = true;
                    //        }
                    //        i += (x / 4) - 1; // subtract one as for loop will advance the index itself
                    //    }
                    //}


                    // write relocation tag to end of line
                    if (command != null)
                    {
                        Symbol relSymbol = LabelMap.GetSymbol(command._moduleID, (int)command.TargetSectionID, command.TargetOffset);
                        var tag = new RelTag(command, relSymbol?.Name ?? $"loc_{ command.TargetOffset:X}");
                        sb.AppendLine($"    {dataStr,-30}{tag}");
                    }
                    else
                    {
                        sb.AppendLine($"    {dataStr}");
                    }

                    //if (needsAlign)
                    //{
                    //    sb.AppendLine("        .balign 4");
                    //}

                }
                writer.Write(sb.ToString());
            }
        }
        private unsafe void DumpCode(ModuleSectionNode node, string filepath)
        {
            var lines = GetAsm(node);
            using (var writer = File.CreateText(filepath))
            {
                StringBuilder sb = new StringBuilder();

                var addr = node.WorkingUncompressed.Address;
                for (int i = 0; i < node.UncompressedSize / 4; i++)
                {
                    PPCOpCode opcode = PowerPC.Disassemble(*(buint*)(addr + i * 4));
                    string opStr = lines[i];
                    var command = node._manager.GetCommand(i);
                    var linked = node._manager.GetLinked(i);
                    List<RelocationTarget> branches = node._manager.GetBranched(i);

                    // if this location is referenced write the label for it
                    Symbol sym = LabelMap.GetSymbol(node.ModuleID, node.Index, (uint)i * 4);
                    if (sym != null)
                    {
                        sb.AppendLine($"{sym.Name}:");
                    }
                    else if (branches != null || linked != null)
                    {
                        sb.AppendLine($"loc_{i * 4:X}:");
                    }
                    // if this is a branch, replace offset with label
                    if (opcode is PPCBranch b && !(opcode is PPCblr))
                    {
                        uint targetOffset = (uint)((i * 4) + b.DataOffset);
                        string label = $"loc_{targetOffset:X}";
                        Symbol targetSym = LabelMap.GetSymbol(node.ModuleID, node.Index, targetOffset);

                        if (targetSym != null)
                        {
                            label = targetSym.Name;
                        }

                        // operand type is private in brawllib so we have to replicate
                        // format behavior here 1:1 for this to work
                        string toReplace = (targetOffset < 0 ? "-" : "") + $"0x{Math.Abs(targetOffset):x}";
                        opStr = opStr.Replace(toReplace, $"{label}");
                    }

                    // write relocation tag to end of line
                    if (command != null)
                    {
                        Symbol relSymbol = LabelMap.GetSymbol(command._moduleID, (int)command.TargetSectionID, command.TargetOffset);
                        var tag = new RelTag(command, relSymbol?.Name ?? $"loc_{ command.TargetOffset:X}");
                        sb.AppendLine($"    {opStr,-60}{tag}");
                    }
                    else
                    {
                        sb.AppendLine($"    {opStr}");
                    }
                }
                writer.Write(sb.ToString());
            }
        }
        #endregion
    }
}
