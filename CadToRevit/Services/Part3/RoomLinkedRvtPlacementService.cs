using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Part3
{
    internal sealed class RoomLinkedRvtPlacementResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public ElementId LinkInstanceId { get; set; }
    }

    internal static class RoomLinkedRvtPlacementService
    {
        private const double SolidVolumeTolerance = 1e-9;

        internal static RoomLinkedRvtPlacementResult LinkRvtToRoomCenter(
            Document hostDoc,
            UIDocument uiDoc,
            RoomSemanticRecord selectedRoom,
            string rvtPath)
        {
            if (hostDoc == null)
            {
                return Failure("No active Revit document.");
            }

            if (selectedRoom == null)
            {
                return Failure("Please select a room first.");
            }

            if (string.IsNullOrWhiteSpace(rvtPath) || !File.Exists(rvtPath))
            {
                return Failure("Selected RVT file does not exist.");
            }

            XYZ roomCenter = ResolveRoomCenter(selectedRoom);
            if (roomCenter == null)
            {
                return Failure("Cannot resolve the selected room center point.");
            }

            double roomBaseZ = ResolveRoomBaseZ(selectedRoom, roomCenter.Z);

            RevitLinkInstance linkInstance = null;
            try
            {
                using (Transaction tx = new Transaction(hostDoc, "Link RVT to Room"))
                {
                    tx.Start();
                    linkInstance = CreateLinkInstance(hostDoc, rvtPath);
                    if (linkInstance == null)
                    {
                        tx.RollBack();
                        return Failure("Failed to create Revit link instance.");
                    }

                    Document linkedDoc = linkInstance.GetLinkDocument();
                    if (linkedDoc == null)
                    {
                        tx.RollBack();
                        return Failure("Linked RVT was created, but its document cannot be read.");
                    }

                    BoundingBoxXYZ coreBox = TryGetTaggedAhuCoreBox(linkedDoc);
                    bool usedTaggedCore = coreBox != null;
                    if (coreBox == null)
                    {
                        coreBox = TryGetEffectiveSolidBox(linkedDoc);
                    }

                    if (coreBox == null)
                    {
                        tx.RollBack();
                        return Failure("The selected RVT does not contain usable 3D solid geometry for placement.");
                    }

                    XYZ coreCenter = GetBoxCenter(coreBox);
                    Transform linkTransform = linkInstance.GetTransform() ?? Transform.Identity;
                    XYZ currentCoreCenter = linkTransform.OfPoint(coreCenter);
                    XYZ currentCoreBottom = linkTransform.OfPoint(new XYZ(coreCenter.X, coreCenter.Y, coreBox.Min.Z));

                    XYZ targetCoreCenter = new XYZ(roomCenter.X, roomCenter.Y, currentCoreCenter.Z);
                    XYZ moveVector = new XYZ(
                        targetCoreCenter.X - currentCoreCenter.X,
                        targetCoreCenter.Y - currentCoreCenter.Y,
                        roomBaseZ - currentCoreBottom.Z);

                    if (moveVector.GetLength() > 0.000001)
                    {
                        ElementTransformUtils.MoveElement(hostDoc, linkInstance.Id, moveVector);
                    }

                    tx.Commit();

                    if (uiDoc != null)
                    {
                        try
                        {
                            uiDoc.Selection.SetElementIds(new List<ElementId> { linkInstance.Id });
                            uiDoc.ShowElements(linkInstance.Id);
                        }
                        catch
                        {
                            try
                            {
                                uiDoc.Selection.SetElementIds(new List<ElementId> { linkInstance.Id });
                            }
                            catch
                            {
                            }
                        }
                    }

                    string fileName = Path.GetFileName(rvtPath);
                    string coreMode = usedTaggedCore ? "Tagged AHU core" : "effective solid geometry";
                    return new RoomLinkedRvtPlacementResult
                    {
                        Success = true,
                        LinkInstanceId = linkInstance.Id,
                        Message = "RVT linked to selected room center successfully." + Environment.NewLine +
                                  "File: " + fileName + Environment.NewLine +
                                  "Placement box: " + coreMode
                    };
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LinkRvtToRoom] Failed. Path=" + rvtPath + ", Error=" + ex);
                return Failure("Failed to link RVT to selected room." + Environment.NewLine + ex.Message);
            }
        }

        private static RevitLinkInstance CreateLinkInstance(Document hostDoc, string rvtPath)
        {
            ElementId linkTypeId = TryFindExistingLinkTypeId(hostDoc, rvtPath);
            if (linkTypeId == ElementId.InvalidElementId)
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(rvtPath);
                RevitLinkOptions options = new RevitLinkOptions(false);
                LinkLoadResult loadResult = RevitLinkType.Create(hostDoc, modelPath, options);
                if (loadResult == null || loadResult.ElementId == null || loadResult.ElementId == ElementId.InvalidElementId)
                {
                    throw new InvalidOperationException("Revit link type creation failed.");
                }

                linkTypeId = loadResult.ElementId;
            }

            RevitLinkInstance instance = RevitLinkInstance.Create(hostDoc, linkTypeId);
            if (instance == null)
            {
                throw new InvalidOperationException("Revit link instance creation failed.");
            }

            return instance;
        }

        private static ElementId TryFindExistingLinkTypeId(Document hostDoc, string rvtPath)
        {
            if (hostDoc == null || string.IsNullOrWhiteSpace(rvtPath))
            {
                return ElementId.InvalidElementId;
            }

            string fullPath = Path.GetFullPath(rvtPath);
            foreach (RevitLinkType linkType in new FilteredElementCollector(hostDoc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                if (linkType == null || !linkType.IsValidObject)
                {
                    continue;
                }

                try
                {
                    ExternalFileReference reference = ExternalFileUtils.GetExternalFileReference(hostDoc, linkType.Id);
                    if (reference != null)
                    {
                        ModelPath modelPath = reference.GetAbsolutePath();
                        string existingPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                        if (!string.IsNullOrWhiteSpace(existingPath) &&
                            string.Equals(Path.GetFullPath(existingPath), fullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return linkType.Id;
                        }
                    }
                }
                catch
                {
                }
            }

            return ElementId.InvalidElementId;
        }

        private static XYZ ResolveRoomCenter(RoomSemanticRecord room)
        {
            XYZ loopCenter = ResolveLoopCentroid(room != null ? room.LoopPoints : null);
            if (loopCenter != null)
            {
                double z = ResolveRoomBaseZ(room, loopCenter.Z);
                return new XYZ(loopCenter.X, loopCenter.Y, z);
            }

            if (room != null && room.Centroid != null)
            {
                return room.Centroid;
            }

            if (room != null && room.BBox != null)
            {
                return GetBoxCenter(room.BBox);
            }

            return null;
        }

        private static XYZ ResolveLoopCentroid(IList<XYZ> points)
        {
            if (points == null || points.Count < 3)
            {
                return null;
            }

            double signedArea = 0.0;
            double cx = 0.0;
            double cy = 0.0;
            double z = 0.0;
            int count = 0;

            for (int i = 0; i < points.Count; i++)
            {
                XYZ p0 = points[i];
                XYZ p1 = points[(i + 1) % points.Count];
                if (p0 == null || p1 == null)
                {
                    continue;
                }

                double cross = p0.X * p1.Y - p1.X * p0.Y;
                signedArea += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
                z += p0.Z;
                count++;
            }

            if (Math.Abs(signedArea) < 0.0000001 || count == 0)
            {
                double sx = 0.0;
                double sy = 0.0;
                double sz = 0.0;
                int validCount = 0;
                foreach (XYZ p in points)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    sx += p.X;
                    sy += p.Y;
                    sz += p.Z;
                    validCount++;
                }

                return validCount > 0 ? new XYZ(sx / validCount, sy / validCount, sz / validCount) : null;
            }

            signedArea *= 0.5;
            cx /= 6.0 * signedArea;
            cy /= 6.0 * signedArea;
            return new XYZ(cx, cy, z / count);
        }

        private static double ResolveRoomBaseZ(RoomSemanticRecord room, double fallbackZ)
        {
            if (room != null && room.BBox != null && room.BBox.Min != null)
            {
                return room.BBox.Min.Z;
            }

            if (room != null && room.Centroid != null)
            {
                return room.Centroid.Z;
            }

            return fallbackZ;
        }

        private static BoundingBoxXYZ TryGetTaggedAhuCoreBox(Document linkedDoc)
        {
            try
            {
                IEnumerable<DirectShape> shapes = new FilteredElementCollector(linkedDoc)
                    .OfClass(typeof(DirectShape))
                    .WhereElementIsNotElementType()
                    .Cast<DirectShape>();

                foreach (DirectShape shape in shapes)
                {
                    if (!IsTaggedAhuCore(shape))
                    {
                        continue;
                    }

                    BoundingBoxXYZ box = TryGetElementSolidBox(shape);
                    if (box != null)
                    {
                        return box;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsTaggedAhuCore(DirectShape shape)
        {
            if (shape == null)
            {
                return false;
            }

            try
            {
                if (string.Equals(shape.ApplicationId, AhuTestRvtModelService.ApplicationId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(shape.ApplicationDataId, AhuTestRvtModelService.CoreDataId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                Parameter comments = shape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && comments.StorageType == StorageType.String)
                {
                    string value = comments.AsString();
                    if (string.Equals(value, AhuTestRvtModelService.CoreDataId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static BoundingBoxXYZ TryGetEffectiveSolidBox(Document linkedDoc)
        {
            BoundingBoxAccumulator accumulator = new BoundingBoxAccumulator();

            IEnumerable<Element> elements = new FilteredElementCollector(linkedDoc)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(IsUsableModelElement);

            foreach (Element element in elements)
            {
                BoundingBoxXYZ box = TryGetElementSolidBox(element);
                if (box == null)
                {
                    continue;
                }

                accumulator.Add(box.Min);
                accumulator.Add(box.Max);
            }

            return accumulator.ToBoundingBox();
        }

        private static bool IsUsableModelElement(Element element)
        {
            if (element == null || !element.IsValidObject || element.ViewSpecific)
            {
                return false;
            }

            Category category = element.Category;
            if (category == null || category.CategoryType != CategoryType.Model)
            {
                return false;
            }

            int categoryId = category.Id != null ? category.Id.IntegerValue : 0;
            if (categoryId == (int)BuiltInCategory.OST_Levels ||
                categoryId == (int)BuiltInCategory.OST_Grids ||
                categoryId == (int)BuiltInCategory.OST_CLines ||
                categoryId == (int)BuiltInCategory.OST_RvtLinks ||
                //categoryId == (int)BuiltInCategory.OST_ImportsInFamilies ||
                categoryId == (int)BuiltInCategory.OST_Cameras)
            {
                return false;
            }

            return true;
        }

        private static BoundingBoxXYZ TryGetElementSolidBox(Element element)
        {
            try
            {
                Options options = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine
                };

                GeometryElement geometry = element.get_Geometry(options);
                if (geometry == null)
                {
                    return null;
                }

                BoundingBoxAccumulator accumulator = new BoundingBoxAccumulator();
                AccumulateSolids(geometry, Transform.Identity, accumulator);
                return accumulator.ToBoundingBox();
            }
            catch
            {
                return null;
            }
        }

        private static void AccumulateSolids(GeometryElement geometry, Transform transform, BoundingBoxAccumulator accumulator)
        {
            if (geometry == null || accumulator == null)
            {
                return;
            }

            Transform currentTransform = transform ?? Transform.Identity;
            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;
                if (solid != null)
                {
                    if (solid.Faces != null && solid.Faces.Size > 0 && solid.Volume > SolidVolumeTolerance)
                    {
                        BoundingBoxXYZ box = solid.GetBoundingBox();
                        AddBox(accumulator, box, currentTransform);
                    }
                    continue;
                }

                GeometryInstance instance = obj as GeometryInstance;
                if (instance != null)
                {
                    Transform instanceTransform = currentTransform.Multiply(instance.Transform ?? Transform.Identity);
                    GeometryElement symbolGeometry = instance.GetSymbolGeometry();
                    AccumulateSolids(symbolGeometry, instanceTransform, accumulator);
                }
            }
        }

        private static void AddBox(BoundingBoxAccumulator accumulator, BoundingBoxXYZ box, Transform parentTransform)
        {
            if (accumulator == null || box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            Transform boxTransform = box.Transform ?? Transform.Identity;
            Transform totalTransform = (parentTransform ?? Transform.Identity).Multiply(boxTransform);
            XYZ min = box.Min;
            XYZ max = box.Max;

            accumulator.Add(totalTransform.OfPoint(new XYZ(min.X, min.Y, min.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(max.X, min.Y, min.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(min.X, max.Y, min.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(max.X, max.Y, min.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(min.X, min.Y, max.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(max.X, min.Y, max.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(min.X, max.Y, max.Z)));
            accumulator.Add(totalTransform.OfPoint(new XYZ(max.X, max.Y, max.Z)));
        }

        private static XYZ GetBoxCenter(BoundingBoxXYZ box)
        {
            if (box == null || box.Min == null || box.Max == null)
            {
                return null;
            }

            return new XYZ(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);
        }

        private static RoomLinkedRvtPlacementResult Failure(string message)
        {
            return new RoomLinkedRvtPlacementResult
            {
                Success = false,
                Message = message ?? "Failed to link RVT to selected room.",
                LinkInstanceId = ElementId.InvalidElementId
            };
        }

        private sealed class BoundingBoxAccumulator
        {
            private bool _hasPoint;
            private double _minX;
            private double _minY;
            private double _minZ;
            private double _maxX;
            private double _maxY;
            private double _maxZ;

            internal void Add(XYZ point)
            {
                if (point == null)
                {
                    return;
                }

                if (!_hasPoint)
                {
                    _minX = _maxX = point.X;
                    _minY = _maxY = point.Y;
                    _minZ = _maxZ = point.Z;
                    _hasPoint = true;
                    return;
                }

                _minX = Math.Min(_minX, point.X);
                _minY = Math.Min(_minY, point.Y);
                _minZ = Math.Min(_minZ, point.Z);
                _maxX = Math.Max(_maxX, point.X);
                _maxY = Math.Max(_maxY, point.Y);
                _maxZ = Math.Max(_maxZ, point.Z);
            }

            internal BoundingBoxXYZ ToBoundingBox()
            {
                if (!_hasPoint)
                {
                    return null;
                }

                return new BoundingBoxXYZ
                {
                    Min = new XYZ(_minX, _minY, _minZ),
                    Max = new XYZ(_maxX, _maxY, _maxZ)
                };
            }
        }
    }
}
