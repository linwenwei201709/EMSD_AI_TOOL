using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class RoomLabelParser
    {
        public static RoomLabel Parse(CadText source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Text))
            {
                return null;
            }

            string[] lines = source.Text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            if (lines.Length == 0)
            {
                return null;
            }

            string number = lines.FirstOrDefault(x => IsRoomNumberLine(x));
            string name = string.Join(" ", lines.Where(x => !string.Equals(x, number, StringComparison.OrdinalIgnoreCase))).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = lines[0];
            }

            return new RoomLabel
            {
                RawText = source.Text,
                RoomName = name,
                RoomNumber = number ?? string.Empty,
                SourceLayer = source.RawLayerName,
                Position = source.Position
            };
        }

        private static bool IsRoomNumberLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string text = line.Trim();
            return text.StartsWith("RM", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("R-", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("R.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
