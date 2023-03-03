using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace reltools.Symbols
{
    internal class Symbol
    {
        public Symbol(uint offset, string name)
        {
            Offset = offset;
            Name = name;
        }
        public uint Offset;
        public string Name;
    }
    internal class SectionMap
    {
        public SectionMap(int sectionID)
        {
            SectionID = sectionID;
            OffsetMapping = new Dictionary<uint, Symbol>();
            NameMapping = new Dictionary<string, Symbol>();
        }
        public int SectionID { get; private set; }
        public Dictionary<uint, Symbol> OffsetMapping { get; set; }
        private Dictionary<string, Symbol> NameMapping { get; set; }
        public Symbol GetSymbol(string name)
        {
            if (NameMapping.ContainsKey(name))
                return NameMapping[name];
            else
                return null;
        }
        public Symbol GetSymbol(uint offset)
        {
            if (OffsetMapping.ContainsKey(offset))
                return OffsetMapping[offset];
            else
                return null;
        }

        public void AddSymbol(Symbol sym, bool replace)
        {
            if (OffsetMapping.ContainsKey(sym.Offset) || NameMapping.ContainsKey(sym.Name))
            {
                OffsetMapping[sym.Offset] = sym;
                NameMapping[sym.Name] = sym;
            }
            else
            {
                OffsetMapping.Add(sym.Offset, sym);
                NameMapping.Add(sym.Name, sym);
            }
        }
    }
    internal class ModuleMap
    {
        public ModuleMap(uint moduleID, string name)
        {
            Sections = new Dictionary<int, SectionMap>();
            ModuleID = moduleID;
            ModuleName = name;
        }
        public uint ModuleID { get; private set; }
        public string ModuleName { get; private set; }
        private Dictionary<int, SectionMap> Sections { get; set; }

        public Symbol GetSymbol(int section, string name)
        {
            if (Sections.ContainsKey(section))
                return Sections[section].GetSymbol(name);

            return null;
        }
        public Symbol GetSymbol(int section, uint offset)
        {
            if (Sections.ContainsKey(section))
                return Sections[section].GetSymbol(offset);

            return null;
        }

        public void AddSymbol(int section, uint offset, string label, bool replace)
        {
            AddSymbol(section, new Symbol(offset, label), replace);
        }
        public void AddSymbol(int section, Symbol sym, bool replace)
        {
            if (!Sections.ContainsKey(section))
                AddSection(new SectionMap(section));

            Sections[section].AddSymbol(sym, replace);
        }
        public Dictionary<uint, Symbol> GetSymbols(int section)
        {
            return Sections[section].OffsetMapping;
        }
        public void AddSection(SectionMap section)
        {
            Sections.Add(section.SectionID, section);
        }
        public void ClearSections()
        {
            Sections.Clear();
        }
    }
    internal class SymbolMap
    {
        public SymbolMap()
        {
            Modules = new Dictionary<uint, ModuleMap>();
        }
        public SymbolMap(string filepath)
        {
            Modules = ParseFile(filepath);
        }
        public SymbolMap(string[] filepaths) : this()
        {
            foreach (string filepath in filepaths)
            {
                Modules = Modules.Concat(ParseFile(filepath)).ToDictionary(x => x.Key, x => x.Value);
            }
        }
        private Dictionary<uint, ModuleMap> Modules { get; set; }
        private static Dictionary<uint, ModuleMap> ParseFile(string filepath)
        {
            Dictionary<uint, ModuleMap> modules = new Dictionary<uint, ModuleMap>();
            using (var reader = File.OpenText(filepath))
            {
                ModuleMap curModule = null;
                SectionMap curSection = null;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine().Trim();
                    if (line.StartsWith(".section"))
                    {
                        if (curModule != null && curSection != null)
                            curModule.AddSection(curSection);

                        string section = line.Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).Last().Trim();
                        curSection = new SectionMap(Convert.ToInt32(section));
                        continue;
                    }
                    else if (line.StartsWith(".module"))
                    {
                        if (curModule != null)
                            modules.Add(curModule.ModuleID, curModule);

                        string moduleName = Path.GetFileNameWithoutExtension(filepath);
                        string module = line.Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).Last().Trim();
                        if (line.Contains(','))
                        {
                            moduleName = line.Split(',').Where(x => !string.IsNullOrEmpty(x)).Last().Trim();
                            module = line.Split(' ')[1].Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).First().Trim();
                        }
                        else
                        {
                            module = line.Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).Last().Trim();
                        }

                        curModule = new ModuleMap(Convert.ToUInt32(module), moduleName);
                        Console.WriteLine($"{module} : {filepath}");
                        continue;
                    }

                    if (curSection is null)
                    {
                        throw new Exception($"Could not resolve symbol {line}:\nno .section directive associated with this symbol");
                    }
                    if (curModule is null)
                    {
                        throw new Exception($"Could not resolve symbol {line}:\nno .module directive associated with this symbol");
                    }

                    var parts = line.Split(' ').Where(x => !string.IsNullOrWhiteSpace(x));
                    uint offset = Convert.ToUInt32(parts.First().Trim(), 16);
                    string label = parts.Last().Trim();
                    curSection.AddSymbol(new Symbol(offset, label), false);
                }
                curModule.AddSection(curSection);
                modules.Add(curModule.ModuleID, curModule);
            }
            return modules;
        }
        public static SymbolMap FromFile(string filename)
        {
            return new SymbolMap(filename);
        }
        public static SymbolMap FromFiles(string[] paths)
        {
            return new SymbolMap(paths);
        }
        public Symbol GetSymbol(uint module, int section, uint offset)
        {
            if (module == 0)
                section = 1;

            if (Modules.ContainsKey(module))
                return Modules[module].GetSymbol(section, offset);

            return null;
        }
        public Symbol GetSymbol(uint module, int section, string name)
        {
            if (module == 0)
                section = 1;

            if (Modules.ContainsKey(module))
                return Modules[module].GetSymbol(section, name);

            return null;
        }
        public void AddSymbol(uint module, int section, uint offset, string label, bool replace)
        {
            AddSymbol(module, section, new Symbol(offset, label), replace);
        }
        public void AddSymbol(uint module, int section, Symbol sym, bool replace)
        {
            if (!Modules.ContainsKey(module))
                AddModule(new ModuleMap(module, null));

            Modules[module].AddSymbol(section, sym, replace);
        }
        public void AddModule(ModuleMap module)
        {
            Modules.Add(module.ModuleID, module);
        }
        public Dictionary<uint, Symbol> GetSymbolsForSection(uint module, int section)
        {
            return Modules[module].GetSymbols(section);
        }
        public uint GetModuleIDFromName(string name)
        {
            var map = Modules.Values.FirstOrDefault(x => x.ModuleName == name);
            if (map != null)
                return map.ModuleID;
            else
                return 0xffffffff;
        }
        public string GetModuleNameFromID(uint ID)
        {
            var map = Modules.Values.FirstOrDefault(x => x.ModuleID == ID);
            if (map != null)
                return map.ModuleName;
            else
                return null;
        }
    }
    internal static class SymbolManager
    {
        public static SymbolMap Map
        {
            get
            {
                if (_map == null)
                {
                    _map = new SymbolMap();
                }

                return _map;
            }
            set { _map = value; }
        }
        private static SymbolMap _map;
        public static string MangleSymbol(int moduleID, int sectionID, string symbol)
        {
            string module = moduleID.ToString();
            string section = sectionID.ToString();
            return $"__M{module.Length}{module}S{section.Length}{section}L{symbol.Length}{symbol}";
        }
        public static string DemangleSymbol(string symbol)
        {
            var matches = Regex.Matches(symbol, @"([MSL]{1}\d+)");
            if (matches.Count == 4)
            {
                var len = Convert.ToInt32(matches[2].Value.Substring(1));
                return symbol.Substring(matches[2].Index + matches[2].Length, len);
            }
            return symbol;
        }
        public static uint GetModuleIDFromName(string name)
        {
            return Map.GetModuleIDFromName(name);
        }
        public static string GetModuleNameFromID(uint ID)
        {
            return Map.GetModuleNameFromID(ID);
        }
    }
}
