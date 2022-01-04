using System.Collections.Generic;

namespace reltools.json
{
    public class RelSection
    {
        public string Path;
        public int SectionID;
        public bool HasCode;
        public bool IsBSS;
        public bool Expand;
        public int ExpandSize;
    }
    public class RelInfo
    {
        public string Name;
        public string Type;
        public int ModuleID;
        public int ModuleAlign;
        public int BSSAlign;
        public int FixSize;

        public List<RelSection> Sections;
    }
    public class DolSection
    {
        public string Path;
        public string LoadAddress;
    }
    public class DolInfo
    {
        public string Name;
        public string Type;
        public string Entrypoint;
        public string BSSAddress;
        public string BSSSize;
        public List<DolSection> Sections;
    }
}
