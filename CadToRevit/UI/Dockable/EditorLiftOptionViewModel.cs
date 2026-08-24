using System;

namespace CadToRevit.UI.Dockable
{
    public sealed class EditorLiftOptionViewModel
    {
        public string Key { get; set; }

        public string DisplayName { get; set; }

        public string LiftKind { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? "-" : DisplayName;
        }
    }
}
