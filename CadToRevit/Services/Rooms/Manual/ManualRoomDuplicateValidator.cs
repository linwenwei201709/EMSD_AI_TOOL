using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms.Manual
{
    public sealed class ManualRoomDuplicateValidationResult
    {
        public bool IsDuplicate { get; set; }

        public string Message { get; set; }
    }

    public static class ManualRoomDuplicateValidator
    {
        private const double OverlapThreshold = 0.85;
        private const int SampleGridSize = 36;

        public static ManualRoomDuplicateValidationResult Validate(
            Document doc,
            ManualRoomRecord candidate,
            IList<ManualRoomValidationRoomInfo> currentRooms)
        {
            ManualRoomDuplicateValidationResult signatureResult = ValidateBoundarySignature(doc, candidate);
            if (signatureResult.IsDuplicate)
            {
                return signatureResult;
            }

            ManualRoomDuplicateValidationResult overlapResult = ValidateGeometryOverlap(doc, candidate, currentRooms);
            if (overlapResult.IsDuplicate)
            {
                return overlapResult;
            }

            return new ManualRoomDuplicateValidationResult();
        }

        private static ManualRoomDuplicateValidationResult ValidateBoundarySignature(Document doc, ManualRoomRecord candidate)
        {
            string candidateSignature = ResolveBoundarySignature(candidate);
            if (string.IsNullOrWhiteSpace(candidateSignature))
            {
                return new ManualRoomDuplicateValidationResult();
            }

            foreach (ManualRoomRecord existing in ManualRoomStorageService.Load(doc))
            {
                if (existing == null || !IsSameLevel(candidate.LevelIdValue, existing.LevelIdValue))
                {
                    continue;
                }

                string existingSignature = ResolveBoundarySignature(existing);
                if (string.Equals(candidateSignature, existingSignature, StringComparison.OrdinalIgnoreCase))
                {
                    return new ManualRoomDuplicateValidationResult
                    {
                        IsDuplicate = true,
                        Message = "A manual room with the same boundary already exists. Please delete the existing manual room first if you need to recreate it."
                    };
                }
            }

            return new ManualRoomDuplicateValidationResult();
        }

        private static ManualRoomDuplicateValidationResult ValidateGeometryOverlap(
            Document doc,
            ManualRoomRecord candidate,
            IList<ManualRoomValidationRoomInfo> currentRooms)
        {
            RoomSemanticRecord candidateRoom = candidate != null ? candidate.ToSemanticRecord() : null;
            if (!CanCompare(candidateRoom))
            {
                return new ManualRoomDuplicateValidationResult();
            }

            List<ManualRoomValidationRoomInfo> rooms = new List<ManualRoomValidationRoomInfo>();
            foreach (ManualRoomValidationRoomInfo info in currentRooms ?? new List<ManualRoomValidationRoomInfo>())
            {
                if (info != null)
                {
                    rooms.Add(info);
                }
            }

            foreach (ManualRoomRecord manual in ManualRoomStorageService.Load(doc))
            {
                if (manual == null)
                {
                    continue;
                }

                rooms.Add(new ManualRoomValidationRoomInfo
                {
                    Room = manual.ToSemanticRecord(),
                    LevelIdValue = manual.LevelIdValue,
                    SourceType = "Manual"
                });
            }

            foreach (ManualRoomValidationRoomInfo info in rooms)
            {
                RoomSemanticRecord existing = info != null ? info.Room : null;
                if (!CanCompare(existing) || !IsSameLevel(candidate.LevelIdValue, info.LevelIdValue))
                {
                    continue;
                }

                double ratio = EstimateOverlapRatio(candidateRoom, existing);
                if (ratio >= OverlapThreshold)
                {
                    return new ManualRoomDuplicateValidationResult
                    {
                        IsDuplicate = true,
                        Message = "A room already exists in this area. Please delete the existing room first if you need to recreate it."
                    };
                }
            }

            return new ManualRoomDuplicateValidationResult();
        }

        private static string ResolveBoundarySignature(ManualRoomRecord room)
        {
            if (room == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(room.BoundarySignature))
            {
                return room.BoundarySignature;
            }

            return string.Join(
                ",",
                (room.BoundaryWalls ?? new List<RoomBoundaryWallReference>())
                    .Where(x => x != null && x.ElementId > 0)
                    .Select(x => x.ElementId)
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(x => x.ToString(CultureInfo.InvariantCulture)));
        }

        private static bool CanCompare(RoomSemanticRecord room)
        {
            return room != null &&
                   room.LoopPoints != null &&
                   room.LoopPoints.Count >= 3 &&
                   room.AreaM2 > 0.0;
        }

        private static bool IsSameLevel(int a, int b)
        {
            if (a > 0 && b > 0)
            {
                return a == b;
            }

            return true;
        }

        private static double EstimateOverlapRatio(RoomSemanticRecord candidate, RoomSemanticRecord existing)
        {
            BoundingBoxXYZ box = candidate.BBox;
            if (box == null || box.Min == null || box.Max == null || !BoundingBoxesOverlapXY(candidate.BBox, existing.BBox))
            {
                return 0.0;
            }

            double minX = box.Min.X;
            double maxX = box.Max.X;
            double minY = box.Min.Y;
            double maxY = box.Max.Y;
            double spanX = maxX - minX;
            double spanY = maxY - minY;
            if (spanX <= 1e-9 || spanY <= 1e-9)
            {
                return 0.0;
            }

            int candidateHits = 0;
            int overlapHits = 0;
            for (int ix = 0; ix < SampleGridSize; ix++)
            {
                double x = minX + spanX * (ix + 0.5) / SampleGridSize;
                for (int iy = 0; iy < SampleGridSize; iy++)
                {
                    double y = minY + spanY * (iy + 0.5) / SampleGridSize;
                    XYZ point = new XYZ(x, y, 0.0);
                    if (!ContainsPointXY(candidate.LoopPoints, point))
                    {
                        continue;
                    }

                    candidateHits++;
                    if (ContainsPointXY(existing.LoopPoints, point))
                    {
                        overlapHits++;
                    }
                }
            }

            return candidateHits > 0 ? (double)overlapHits / candidateHits : 0.0;
        }

        private static bool BoundingBoxesOverlapXY(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null || a.Min == null || a.Max == null || b.Min == null || b.Max == null)
            {
                return true;
            }

            return a.Min.X <= b.Max.X &&
                   a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y &&
                   a.Max.Y >= b.Min.Y;
        }

        private static bool ContainsPointXY(IList<XYZ> polygon, XYZ point)
        {
            bool inside = false;
            int count = polygon != null ? polygon.Count : 0;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                XYZ pi = polygon[i];
                XYZ pj = polygon[j];
                if (pi == null || pj == null)
                {
                    continue;
                }

                bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                                  (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-12) + pi.X);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
