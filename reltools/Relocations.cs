using System;using System.Text.RegularExpressions;using BrawlLib.SSBB.ResourceNodes;namespace reltools{
    internal class RelTag
    {
        public RelTag(PPCRelType command, uint targetModule, int targetSection, string label)
        {
            this.Command = command;
            this.TargetModule = targetModule;
            this.TargetSection = targetSection;
            this.Label = label;
        }
        public RelTag(RelCommand command, string label)
        {
            this.Command = (PPCRelType)command.Command;
            this.TargetModule = command._moduleID;
            this.TargetSection = (int)command.TargetSectionID;
            this.Label = label;
        }
        public PPCRelType Command
        {
            get
            {
                return this._command;
            }
            set
            {
                this._command = value;
            }
        }
        private PPCRelType _command;
        public uint TargetModule
        {
            get
            {
                return this._targetModule;
            }
            set
            {
                this._targetModule = value;
            }
        }
        private uint _targetModule;
        public int TargetSection
        {
            get
            {
                return this._targetSection;
            }
            set
            {
                this._targetSection = value;
            }
        }
        private int _targetSection;
        public string Label
        {
            get
            {
                return this._label;
            }
            set
            {
                this._label = value;
            }
        }
        private string _label;

        public static RelTag FromString(string input)
        {
            var matches = Regex.Matches(input, "[\\w+\\.]+");
            var command = (PPCRelType)Enum.Parse(typeof(PPCRelType), matches[0].Value);
            uint targetModule = Convert.ToUInt32(matches[1].Value);
            int targetSection = Convert.ToInt32(matches[2].Value);
            string value = matches[3].Value;

            return new RelTag(command, targetModule, targetSection, value);
        }

        public override string ToString()
        {
            return $"[{this.Command}({this.TargetModule}, {this.TargetSection}, \"{this.Label}\")]";
        }

    }
    public enum PPCRelType
    {
        R_PPC_NONE,
        R_PPC_ADDR32,
        R_PPC_ADDR24,
        R_PPC_ADDR16,
        R_PPC_ADDR16_LO,
        R_PPC_ADDR16_HI,
        R_PPC_ADDR16_HA,
        R_PPC_ADDR14,
        R_PPC_ADDR14_BRTAKEN,
        R_PPC_ADDR14_BRNTAKEN,
        R_PPC_REL24,
        R_PPC_REL14,
        R_PPC_REL14_BRTAKEN,
        R_PPC_REL14_BRNTAKEN,
        R_PPC_GOT16,
        R_PPC_GOT16_LO,
        R_PPC_GOT16_HI,
        R_PPC_GOT16_HA,
        R_PPC_PLTREL24,
        R_PPC_COPY,
        R_PPC_GLOB_DAT,
        R_PPC_JMP_SLOT,
        R_PPC_RELATIVE,
        R_PPC_LOCAL24PC,
        R_PPC_UADDR32,
        R_PPC_UADDR16,
        R_PPC_REL32,
        R_PPC_PLT32,
        R_PPC_PLTREL32,
        R_PPC_PLT16_LO,
        R_PPL_PLT16_HI,
        R_PPC_PLT16_HA,
        R_PPC_SDAREL16,
        R_PPC_SECTOFF,
        R_PPC_SECTOFF_LO,
        R_PPC_SECTOFF_HI,
        R_PPC_SECTOFF_HA,
        R_PPC_ADDR30,
        R_DOLPHIN_NOP = 201,
        R_DOLPHIN_SECTION = 202,
        R_DOLPHIN_END = 203,
        R_DOLPHIN_MRKREF = 204
    }}