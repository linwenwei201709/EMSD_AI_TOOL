using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CadToRevit.Services.Rooms
{
    internal static class Room3DVisualizationTextService
    {
        internal sealed class TextCreateResult
        {
            public bool Created { get; set; }
            public string Reason { get; set; }
            public XYZ Position { get; set; }
            public string TypeName { get; set; }
            public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
        }

        internal static TextCreateResult TryCreate(
            Document doc,
            RoomSemanticRecord room,
            XYZ markerPoint,
            bool highlight)
        {
            TextCreateResult result = new TextCreateResult
            {
                Created = false,
                Reason = "Unknown"
            };

            if (doc == null || room == null)
            {
                result.Reason = "DocOrRoomInvalid";
                return result;
            }

            XYZ p = ResolveTextPosition(room, markerPoint);
            if (p == null)
            {
                result.Reason = "PositionMissing";
                return result;
            }

            result.Position = p;

            string roomName = (room.RoomName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roomName))
            {
                result.Reason = "RoomNameEmpty";
                return result;
            }

            FamilySymbol symbol = EnsureTextFamilySymbol(doc, highlight, out string symbolReason);
            if (symbol == null)
            {
                result.Reason = symbolReason ?? "TextSymbolMissing";
                return result;
            }

            result.TypeName = symbol.Name ?? string.Empty;

            FamilyInstance instance = doc.Create.NewFamilyInstance(
                p,
                symbol,
                StructuralType.NonStructural);
            if (instance == null)
            {
                result.Reason = "CreateReturnedNull";
                return result;
            }

            Room3DVisualizationMetadataService.ApplyMetadata(
                instance,
                Room3DVisualizationMetadataService.BuildTextName(room.Key),
                Room3DVisualizationMetadataService.BuildTextDataId(room.Key));
            TrySetTextParameters(instance, roomName, room.Key);

            result.ElementId = instance.Id;
            result.Created = true;
            result.Reason = "Created";
            return result;
        }

        private static FamilySymbol EnsureTextFamilySymbol(Document doc, bool highlight, out string reason)
        {
            reason = "Unknown";
            if (doc == null)
            {
                reason = "DocInvalid";
                return null;
            }

            List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x != null)
                .Where(IsTextFamilySymbol)
                .ToList();
            FamilySymbol symbol = SelectSymbol(symbols, highlight);
            if (symbol == null)
            {
                string familyPath = ResolveTextFamilyPath();
                if (string.IsNullOrWhiteSpace(familyPath) || !File.Exists(familyPath))
                {
                    reason = "TextFamilyFileMissing";
                    DiagnosticRecorder.AppendDebug("[Room3DVis] Text family file missing.");
                    return null;
                }

                if (!doc.LoadFamily(familyPath, out Family loadedFamily) || loadedFamily == null)
                {
                    reason = "TextFamilyLoadFailed";
                    DiagnosticRecorder.AppendDebug("[Room3DVis] Text family load failed, Path=" + familyPath);
                    return null;
                }

                symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(x => x != null)
                    .Where(IsTextFamilySymbol)
                    .ToList();
                symbol = SelectSymbol(symbols, highlight);
            }

            if (symbol == null)
            {
                reason = "TextSymbolMissing";
                return null;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            reason = "Resolved";
            return symbol;
        }

        private static XYZ ResolveTextPosition(RoomSemanticRecord room, XYZ markerPoint)
        {
            XYZ basePoint = markerPoint ?? room?.Centroid;
            if (basePoint == null)
            {
                return null;
            }

            // Keep text on floor baseline with only tiny anti-z-fighting lift.
            double floorZ = markerPoint != null
                ? markerPoint.Z
                : (room?.BBox?.Min?.Z ?? basePoint.Z);
            double z = floorZ + Room3DVisualizationConstants.TextOffsetMm * Room3DVisualizationConstants.MmToFeet;
            return new XYZ(basePoint.X, basePoint.Y, z);
        }

        private static bool IsTextFamilySymbol(FamilySymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            string familyName = symbol.FamilyName ?? string.Empty;
            if (familyName.IndexOf(Room3DVisualizationConstants.TextFamilyName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            Family family = symbol.Family;
            string familyElementName = family != null ? (family.Name ?? string.Empty) : string.Empty;
            return familyElementName.IndexOf(Room3DVisualizationConstants.TextFamilyName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static FamilySymbol SelectSymbol(List<FamilySymbol> symbols, bool highlight)
        {
            if (symbols == null || symbols.Count == 0)
            {
                return null;
            }

            if (highlight)
            {
                FamilySymbol highlightSymbol = symbols.FirstOrDefault(x =>
                    string.Equals(x.Name ?? string.Empty, Room3DVisualizationConstants.TextHighlightTypeName, StringComparison.OrdinalIgnoreCase));
                if (highlightSymbol != null)
                {
                    return highlightSymbol;
                }
            }

            FamilySymbol defaultSymbol = symbols.FirstOrDefault(x =>
                string.Equals(x.Name ?? string.Empty, Room3DVisualizationConstants.TextDefaultTypeName, StringComparison.OrdinalIgnoreCase));
            return defaultSymbol ?? symbols.FirstOrDefault();
        }

        private static string ResolveTextFamilyPath()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            string baseDir = Path.GetDirectoryName(dllPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                return string.Empty;
            }

            string[] candidates = new[]
            {
                Path.Combine(baseDir, Room3DVisualizationConstants.TextFamilyFileName),
                Path.Combine(baseDir, "Resources", Room3DVisualizationConstants.TextFamilyFileName),
                Path.Combine(baseDir, "Families", Room3DVisualizationConstants.TextFamilyFileName)
            };
            return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static void TrySetTextParameters(FamilyInstance instance, string roomName, string roomKey)
        {
            if (instance == null)
            {
                return;
            }

            // Custom family parameter (e.g. Model Text linked to ROOMNAMEPARAM in family editor).
            bool roomNameParamOk = TrySetRoomNameParamWithDiagnostics(instance, roomName, roomKey);

            // Write semantic payload to commonly used text parameters for cross-family compatibility.
            string[] nameCandidates = new[] { "Text", "TEXT", "Label", "RoomName", "ROOM_NAME", "Name" };
            foreach (string candidate in nameCandidates)
            {
                Parameter p = FindParameterByDefinitionName(instance, candidate);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    try
                    {
                        p.Set(roomName);
                        DiagnosticRecorder.AppendDebug(
                            "[Room3DVis][TextParam] RoomKey=" + (roomKey ?? string.Empty) +
                            ", Param=" + candidate + ", Scope=Instance, OK=true");
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[Room3DVis][TextParam] RoomKey=" + (roomKey ?? string.Empty) +
                            ", Param=" + candidate + ", Scope=Instance, OK=false, Error=" + ex.Message);
                    }
                }
            }

            if (!roomNameParamOk)
            {
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] Summary RoomKey=" + (roomKey ?? string.Empty) +
                    ", ValueLen=" + (roomName != null ? roomName.Length : 0) +
                    ", InstanceWriteOK=false — check family: param must be instance (or we set type), StorageType String, not read-only, name exactly ROOMNAMEPARAM.");
            }

            Parameter comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments != null && !comments.IsReadOnly)
            {
                comments.Set(Room3DVisualizationMetadataService.BuildTextDataId(roomKey));
            }
        }

        private const string RoomNameParamKey = "ROOMNAMEPARAM";

        /// <summary>
        /// Sets ROOMNAMEPARAM on instance and/or type symbol, with detailed diagnostics to mvp1_debug log.
        /// </summary>
        private static bool TrySetRoomNameParamWithDiagnostics(FamilyInstance instance, string roomName, string roomKey)
        {
            bool anySuccess = false;
            string key = roomKey ?? string.Empty;

            Parameter onInstance = FindParameterByDefinitionName(instance, RoomNameParamKey);
            if (onInstance != null)
            {
                anySuccess |= LogAndTrySetStringParameter(onInstance, roomName, key, "Instance");
            }
            else
            {
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + key + ", Scope=Instance, Found=false");
            }

            FamilySymbol symbol = instance.Symbol;
            Parameter onType = symbol != null ? FindParameterByDefinitionName(symbol, RoomNameParamKey) : null;
            if (onType != null)
            {
                bool typeOk = LogAndTrySetStringParameter(onType, roomName, key, "TypeSymbol");
                anySuccess |= typeOk;
                if (typeOk)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[Room3DVis][ROOMNAMEPARAM] Note RoomKey=" + key +
                        ", wrote Type parameter — all instances of this type share the same value.");
                }
            }
            else if (symbol != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + key + ", Scope=TypeSymbol, Found=false");
            }

            if (!anySuccess)
            {
                AppendRoomNameParamHint(instance, symbol, key);
            }

            return anySuccess;
        }

        private static void AppendRoomNameParamHint(FamilyInstance instance, FamilySymbol symbol, string roomKey)
        {
            List<string> hints = CollectParameterNameHints(instance, symbol, "ROOM");
            string joined = hints.Count > 0 ? string.Join(" | ", hints.Take(24)) : "(no matching names)";
            DiagnosticRecorder.AppendDebug(
                "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + roomKey + ", Hints(names containing ROOM, first 24)=" + joined);
        }

        private static List<string> CollectParameterNameHints(FamilyInstance instance, FamilySymbol symbol, string contains)
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(contains))
            {
                string c = contains.ToUpperInvariant();
                void addFrom(Element e)
                {
                    if (e == null)
                    {
                        return;
                    }

                    foreach (Parameter p in e.Parameters)
                    {
                        if (p?.Definition == null)
                        {
                            continue;
                        }

                        string n = p.Definition.Name ?? string.Empty;
                        if (n.ToUpperInvariant().IndexOf(c, StringComparison.Ordinal) >= 0)
                        {
                            list.Add(n + "[RO=" + p.IsReadOnly + ",ST=" + p.StorageType + "]");
                        }
                    }
                }

                addFrom(instance);
                addFrom(symbol);
            }

            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool LogAndTrySetStringParameter(Parameter p, string roomName, string roomKey, string scopeLabel)
        {
            string defName = p.Definition != null ? (p.Definition.Name ?? string.Empty) : string.Empty;
            if (p.IsReadOnly)
            {
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + roomKey + ", Scope=" + scopeLabel +
                    ", Param=" + defName + ", ReadOnly=true, OK=false");
                return false;
            }

            if (p.StorageType != StorageType.String)
            {
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + roomKey + ", Scope=" + scopeLabel +
                    ", Param=" + defName + ", StorageType=" + p.StorageType + " (need String), OK=false");
                return false;
            }

            try
            {
                p.Set(roomName ?? string.Empty);
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + roomKey + ", Scope=" + scopeLabel +
                    ", Param=" + defName + ", OK=true, Len=" + (roomName != null ? roomName.Length : 0));
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[Room3DVis][ROOMNAMEPARAM] RoomKey=" + roomKey + ", Scope=" + scopeLabel +
                    ", Param=" + defName + ", OK=false, Error=" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// LookupParameter plus scan by Definition.Name (case-insensitive) for family/shared params.
        /// </summary>
        private static Parameter FindParameterByDefinitionName(Element element, string paramName)
        {
            if (element == null || string.IsNullOrWhiteSpace(paramName))
            {
                return null;
            }

            Parameter direct = element.LookupParameter(paramName);
            if (direct != null)
            {
                return direct;
            }

            foreach (Parameter p in element.Parameters)
            {
                if (p?.Definition == null)
                {
                    continue;
                }

                if (string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return null;
        }
    }
}
