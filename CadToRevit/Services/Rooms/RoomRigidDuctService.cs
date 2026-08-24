using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomRigidDuctService
    {
        internal sealed class WallPointPickResult
        {
            public bool Succeeded { get; set; }

            public bool Canceled { get; set; }

            public string Message { get; set; }

            public ElementId WallElementId { get; set; } = ElementId.InvalidElementId;

            public XYZ PickPoint { get; set; }

            public string DisplayName { get; set; }
        }

        internal sealed class RigidDuctOptions
        {
            public double VerticalRiseMm { get; set; } = 1200.0;

            public double HorizontalStartOffsetMm { get; set; } = 300.0;

            public double WallPenetrationMm { get; set; } = 150.0;

            public double FallbackWidthMm { get; set; } = 700.0;

            public double FallbackHeightMm { get; set; } = 250.0;
        }

        internal sealed class CreateRigidDuctResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public ElementId VerticalDuctId { get; set; } = ElementId.InvalidElementId;

            public ElementId HorizontalDuctId { get; set; } = ElementId.InvalidElementId;

            public ElementId MiddleDuctId { get; set; } = ElementId.InvalidElementId;

            public ElementId ElbowFittingId { get; set; } = ElementId.InvalidElementId;

            public ElementId SecondElbowFittingId { get; set; } = ElementId.InvalidElementId;
        }

        internal sealed class CreateDuctWorkResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public List<ElementId> CreatedElementIds { get; } = new List<ElementId>();
        }

        private enum DuctConnectorRole
        {
            Any,
            SupplyAir,
            ReturnAir
        }

        internal static WallPointPickResult PickWallPoint(UIDocument uiDoc)
        {
            WallPointPickResult result = new WallPointPickResult();
            if (uiDoc == null || uiDoc.Document == null)
            {
                result.Message = "Active document is not available.";
                return result;
            }

            try
            {
                Reference pickedReference = uiDoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new WallPointSelectionFilter(),
                    "Pick a point on a wall for the duct end.");

                if (pickedReference == null)
                {
                    result.Message = "No wall point was selected.";
                    return result;
                }

                Element wall = uiDoc.Document.GetElement(pickedReference.ElementId);
                if (!(wall is Wall))
                {
                    result.Message = "The selected element is not a wall.";
                    return result;
                }

                result.Succeeded = true;
                result.WallElementId = wall.Id;
                result.PickPoint = pickedReference.GlobalPoint;
                result.DisplayName = BuildWallDisplayName(wall);
                result.Message = "Success";
                return result;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                result.Canceled = true;
                result.Message = "Wall point selection canceled.";
                return result;
            }
        }

        internal static CreateRigidDuctResult CreateThreePieceDuctToWall(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId wallElementId,
            XYZ wallPoint,
            RigidDuctOptions options)
        {
            return CreateThreePieceDuctToWall(
                doc,
                equipmentInstanceId,
                wallElementId,
                wallPoint,
                options,
                DuctConnectorRole.Any,
                null,
                "Manual");
        }

        internal static CreateDuctWorkResult CreateSupplyReturnDuctWork(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId sadWallElementId,
            string sadSizeText,
            ElementId radWallElementId,
            string radSizeText,
            RigidDuctOptions options)
        {
            CreateDuctWorkResult result = new CreateDuctWorkResult();
            if (doc == null)
            {
                result.Message = "Document is null.";
                return result;
            }

            // Capture all existing duct-related elements before creation. Revit can create
            // additional fittings/accessories implicitly while building the duct route. Those
            // generated elements must be tracked as part of this operation so Remove/Regenerate
            // can delete the entire batch instead of leaving orphaned grey fittings behind.
            HashSet<int> ductWorkElementIdsBeforeCreate = CaptureDuctWorkElementIds(doc);

            options = options ?? new RigidDuctOptions();
            using (TransactionGroup group = new TransactionGroup(doc, "Create Supply Return Ductwork"))
            {
                group.Start();

                FamilyInstance equipment = doc.GetElement(equipmentInstanceId) as FamilyInstance;
                Connector supplyConnector = equipment != null ? FindDuctConnector(equipment, DuctConnectorRole.SupplyAir) : null;
                Connector returnConnector = equipment != null ? FindDuctConnector(equipment, DuctConnectorRole.ReturnAir) : null;

                Wall sadWall = doc.GetElement(sadWallElementId) as Wall;
                Wall radWall = doc.GetElement(radWallElementId) as Wall;
                XYZ sadWallPoint = BuildAutoWallFacePoint(sadWall, supplyConnector != null ? supplyConnector.Origin : null);
                XYZ radWallPoint = BuildAutoWallFacePoint(radWall, returnConnector != null ? returnConnector.Origin : null);

                CreateRigidDuctResult sadResult = CreateThreePieceDuctToWall(
                    doc,
                    equipmentInstanceId,
                    sadWallElementId,
                    sadWallPoint,
                    options,
                    DuctConnectorRole.SupplyAir,
                    sadSizeText,
                    "SAD");

                if (sadResult == null || !sadResult.Succeeded)
                {
                    group.RollBack();
                    result.Message = sadResult != null && !string.IsNullOrWhiteSpace(sadResult.Message)
                        ? sadResult.Message
                        : "Create SAD duct failed.";
                    return result;
                }

                CreateRigidDuctResult radResult = CreateThreePieceDuctToWall(
                    doc,
                    equipmentInstanceId,
                    radWallElementId,
                    radWallPoint,
                    options,
                    DuctConnectorRole.ReturnAir,
                    radSizeText,
                    "RAD");

                if (radResult == null || !radResult.Succeeded)
                {
                    group.RollBack();
                    result.Message = radResult != null && !string.IsNullOrWhiteSpace(radResult.Message)
                        ? radResult.Message
                        : "Create RAD duct failed.";
                    return result;
                }

                foreach (ElementId elementId in new[]
                {
                    sadResult.VerticalDuctId,
                    sadResult.MiddleDuctId,
                    sadResult.HorizontalDuctId,
                    sadResult.ElbowFittingId,
                    sadResult.SecondElbowFittingId,
                    radResult.VerticalDuctId,
                    radResult.MiddleDuctId,
                    radResult.HorizontalDuctId,
                    radResult.ElbowFittingId,
                    radResult.SecondElbowFittingId
                })
                {
                    AddCreatedElementId(result.CreatedElementIds, elementId);
                }

                group.Assimilate();

                // Add every duct-related element that appeared during this create operation,
                // including fittings/accessories that Revit may have generated automatically and
                // which are not represented by the explicit IDs returned above.
                AppendNewDuctWorkElementIds(
                    doc,
                    ductWorkElementIdsBeforeCreate,
                    result.CreatedElementIds);

                DiagnosticRecorder.AppendDebug(
                    "[RigidDuct] CreateSupplyReturnDuctWork tracked element count=" +
                    result.CreatedElementIds.Count.ToString(CultureInfo.InvariantCulture));

                result.Succeeded = true;
                result.Message = "Success";
                return result;
            }
        }

        private static HashSet<int> CaptureDuctWorkElementIds(Document doc)
        {
            HashSet<int> ids = new HashSet<int>();
            if (doc == null)
            {
                return ids;
            }

            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory
            };

            foreach (BuiltInCategory category in categories)
            {
                try
                {
                    ICollection<ElementId> categoryIds = new FilteredElementCollector(doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .ToElementIds();

                    foreach (ElementId id in categoryIds)
                    {
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            ids.Add(id.IntegerValue);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[RigidDuct] Capture ductwork category failed. Category=" +
                        category.ToString() +
                        ", Error=" + ex.Message);
                }
            }

            return ids;
        }

        private static void AppendNewDuctWorkElementIds(
            Document doc,
            HashSet<int> beforeIds,
            List<ElementId> target)
        {
            if (doc == null || target == null)
            {
                return;
            }

            beforeIds = beforeIds ?? new HashSet<int>();
            HashSet<int> afterIds = CaptureDuctWorkElementIds(doc);

            foreach (int idValue in afterIds)
            {
                if (beforeIds.Contains(idValue))
                {
                    continue;
                }

                Element element = doc.GetElement(new ElementId(idValue));
                if (element == null)
                {
                    continue;
                }

                AddCreatedElementId(target, element.Id);

                DiagnosticRecorder.AppendDebug(
                    "[RigidDuct] Auto-created ductwork element tracked. ElementId=" +
                    idValue.ToString(CultureInfo.InvariantCulture) +
                    ", Category=" +
                    (element.Category != null ? element.Category.Name : "(none)") +
                    ", Type=" + element.GetType().Name);
            }
        }

        private static void AddCreatedElementId(List<ElementId> target, ElementId elementId)
        {
            if (target == null || elementId == null || elementId == ElementId.InvalidElementId)
            {
                return;
            }

            if (!target.Any(x => x != null && x.IntegerValue == elementId.IntegerValue))
            {
                target.Add(elementId);
            }
        }

        private static CreateRigidDuctResult CreateThreePieceDuctToWall(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId wallElementId,
            XYZ wallPoint,
            RigidDuctOptions options,
            DuctConnectorRole connectorRole,
            string explicitSizeText,
            string runLabel)
        {
            CreateRigidDuctResult result = new CreateRigidDuctResult();
            if (doc == null)
            {
                result.Message = "Document is null.";
                return result;
            }

            options = options ?? new RigidDuctOptions();

            FamilyInstance equipment = doc.GetElement(equipmentInstanceId) as FamilyInstance;
            if (equipment == null)
            {
                result.Message = "Current equipment instance was not found.";
                return result;
            }

            Wall wall = doc.GetElement(wallElementId) as Wall;
            if (wall == null)
            {
                result.Message = "Selected wall was not found.";
                return result;
            }

            if (wallPoint == null)
            {
                result.Message = "Wall point is missing.";
                return result;
            }

            Connector equipmentConnector = FindDuctConnector(equipment, connectorRole);
            if (equipmentConnector == null)
            {
                result.Message = "No " + GetConnectorClassificationText(connectorRole) + " connector was found on the selected equipment.";
                return result;
            }

            DuctType ductType = ResolveDuctType(doc);
            if (ductType == null)
            {
                result.Message = "No duct type is available in the current model. Please load a rectangular duct type, such as Rectangular Duct Default, and try again.";
                return result;
            }

            MechanicalSystemType systemType = ResolveMechanicalSystemType(doc, connectorRole);
            if (systemType == null)
            {
                result.Message = "No " + GetConnectorClassificationText(connectorRole) + " mechanical system type is available in the current model.";
                return result;
            }

            ElementId levelId = ResolveLevelId(doc, equipment, wall);
            if (levelId == ElementId.InvalidElementId)
            {
                result.Message = "No level is available for duct creation.";
                return result;
            }

            DuctSize size = ResolveDuctSize(equipmentConnector, options, explicitSizeText);
            DuctRoutePoints route = BuildRoutePoints(equipmentConnector, wall, wallPoint, options);
            if (!route.IsValid)
            {
                result.Message = route.ErrorMessage;
                return result;
            }

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] Run=" + (runLabel ?? string.Empty) +
                ", ConnectorRole=" + connectorRole.ToString() +
                ", ExplicitSizeText=" + (explicitSizeText ?? string.Empty) +
                ", WallId=" + wall.Id.IntegerValue.ToString());
            LogPreCreateDiagnostics(equipmentConnector, size, route, ductType, systemType);

            using (Transaction tx = new Transaction(doc, "Create Ventilation Duct"))
            {
                tx.Start();
                try
                {
                    Duct verticalDuct = Duct.Create(doc, systemType.Id, ductType.Id, levelId, route.P0, route.P1);
                    if (verticalDuct == null)
                    {
                        throw new InvalidOperationException("Failed to create vertical duct segment.");
                    }

                    Duct horizontalDuct = Duct.Create(doc, systemType.Id, ductType.Id, levelId, route.P2, route.P3);
                    if (horizontalDuct == null)
                    {
                        throw new InvalidOperationException("Failed to create horizontal duct segment.");
                    }

                    SetRectangularDuctSize(verticalDuct, size);
                    SetRectangularDuctSize(horizontalDuct, size);
                    doc.Regenerate();

                    // DEMO mode: reproduce the old presentation shape: a vertical connector stub,
                    // one rectangular elbow, and a horizontal duct that reaches the selected wall.
                    // Do not ConnectTo() the AHU connector; only place the duct body at the connector
                    // origin. This avoids Revit's strict AHU-family network validation while keeping
                    // the visual output close to the requested screenshot.
                    TryAlignDuctRollToConnector(doc, verticalDuct, route.P0, route.P1, equipmentConnector, route.P0);
                    doc.Regenerate();

                    Connector verticalEnd = FindNearestConnector(verticalDuct.ConnectorManager, route.P1);
                    Connector horizontalStart = FindNearestConnector(horizontalDuct.ConnectorManager, route.P2);
                    FamilyInstance elbow = null;
                    if (verticalEnd != null && horizontalStart != null)
                    {
                        try
                        {
                            LogElbowDiagnostics(equipmentConnector, verticalEnd, horizontalStart, route, size, ductType, systemType);
                            elbow = doc.Create.NewElbowFitting(verticalEnd, horizontalStart);
                            doc.Regenerate();
                        }
                        catch (Exception elbowEx)
                        {
                            DiagnosticRecorder.AppendDebug("[RigidDuct] Demo elbow insertion failed; keeping two visible duct runs. " + elbowEx.Message);
                        }
                    }
                    else
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[RigidDuct] Demo elbow skipped because connector resolution failed. " +
                            "VerticalEnd=" + (verticalEnd != null).ToString() +
                            ", HorizontalStart=" + (horizontalStart != null).ToString());
                    }

                    ApplyDemoDuctColorOverrides(doc, connectorRole, verticalDuct.Id, horizontalDuct.Id, elbow != null ? elbow.Id : ElementId.InvalidElementId);

                    result.Succeeded = true;
                    result.VerticalDuctId = verticalDuct.Id;
                    result.MiddleDuctId = ElementId.InvalidElementId;
                    result.HorizontalDuctId = horizontalDuct.Id;
                    result.ElbowFittingId = elbow != null ? elbow.Id : ElementId.InvalidElementId;
                    result.SecondElbowFittingId = ElementId.InvalidElementId;
                    result.Message = "Success";

                    DiagnosticRecorder.AppendDebug(
                        "[RigidDuct] Created demo old-style shape Vertical=" + result.VerticalDuctId.IntegerValue.ToString() +
                        ", Horizontal=" + result.HorizontalDuctId.IntegerValue.ToString() +
                        ", Elbow=" + result.ElbowFittingId.IntegerValue.ToString());

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    DiagnosticRecorder.AppendDebug("[RigidDuct] Create failed=" + ex);
                    result.Message = ex.Message;
                }
            }

            return result;
        }

        private static void ApplyDemoDuctColorOverrides(Document doc, DuctConnectorRole role, params ElementId[] elementIds)
        {
            if (doc == null || doc.ActiveView == null || elementIds == null || elementIds.Length == 0)
            {
                return;
            }

            Color roleColor = ResolveDemoDuctColor(role);
            ElementId solidFillPatternId = GetSolidFillPatternId(doc);

            OverrideGraphicSettings overrides = new OverrideGraphicSettings();
            overrides.SetProjectionLineColor(roleColor);
            overrides.SetSurfaceForegroundPatternColor(roleColor);
            overrides.SetCutForegroundPatternColor(roleColor);
            overrides.SetSurfaceTransparency(0);

            if (solidFillPatternId != ElementId.InvalidElementId)
            {
                overrides.SetSurfaceForegroundPatternId(solidFillPatternId);
                overrides.SetCutForegroundPatternId(solidFillPatternId);
            }

            foreach (ElementId id in elementIds)
            {
                if (id == null || id == ElementId.InvalidElementId)
                {
                    continue;
                }

                try
                {
                    if (doc.GetElement(id) != null)
                    {
                        doc.ActiveView.SetElementOverrides(id, overrides);
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[RigidDuct] Failed to apply demo duct color override. ElementId=" + id.IntegerValue.ToString(CultureInfo.InvariantCulture) + ", Error=" + ex.Message);
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] Applied demo duct color override Role=" + role.ToString() +
                ", Color=" + ColorToHex(roleColor));
        }

        private static Color ResolveDemoDuctColor(DuctConnectorRole role)
        {
            if (role == DuctConnectorRole.SupplyAir)
            {
                // S / Supply Air: #C000C0
                return new Color(0xC0, 0x00, 0xC0);
            }

            if (role == DuctConnectorRole.ReturnAir)
            {
                // R / Return Air: #0000FF
                return new Color(0x00, 0x00, 0xFF);
            }

            return new Color(0x80, 0x80, 0x80);
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            try
            {
                FillPatternElement solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(x => x != null && x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);

                return solidFill != null ? solidFill.Id : ElementId.InvalidElementId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        private static string ColorToHex(Color color)
        {
            if (color == null)
            {
                return string.Empty;
            }

            return "#" +
                color.Red.ToString("X2", CultureInfo.InvariantCulture) +
                color.Green.ToString("X2", CultureInfo.InvariantCulture) +
                color.Blue.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static string BuildWallDisplayName(Element wall)
        {
            if (wall == null)
            {
                return "No wall selected";
            }

            string name = wall.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Wall";
            }

            return name + " (" + wall.Id.IntegerValue.ToString() + ")";
        }

        private static Connector FindDuctConnector(FamilyInstance equipment, DuctConnectorRole role)
        {
            List<Connector> connectors = GetEquipmentDuctConnectors(equipment)
                .OrderByDescending(x => x.Origin != null ? x.Origin.Z : 0.0)
                .ToList();

            if (connectors.Count == 0)
            {
                return null;
            }

            if (role == DuctConnectorRole.SupplyAir)
            {
                // Do not fall back to the first connector here. Supply Air must be matched explicitly,
                // otherwise SAD/RAD may swap and Revit can invalidate the duct direction.
                return connectors.FirstOrDefault(IsSupplyAirConnector);
            }

            if (role == DuctConnectorRole.ReturnAir)
            {
                // Do not fall back to the first connector here. Return Air must be matched explicitly.
                return connectors.FirstOrDefault(IsReturnAirConnector);
            }

            Connector preferred = connectors.FirstOrDefault(IsSupplyAirConnector);
            return preferred ?? connectors.FirstOrDefault();
        }

        private static IEnumerable<Connector> GetEquipmentDuctConnectors(FamilyInstance equipment)
        {
            ConnectorSet connectorSet = equipment?.MEPModel?.ConnectorManager?.Connectors;
            if (connectorSet == null)
            {
                return Enumerable.Empty<Connector>();
            }

            return connectorSet
                .Cast<Connector>()
                .Where(x => x != null && x.Domain == Domain.DomainHvac)
                .Where(x => x.ConnectorType == ConnectorType.End);
        }

        private static bool IsSupplyAirConnector(Connector connector)
        {
            string classification = ReadConnectorClassificationText(connector).ToLowerInvariant();
            return classification.Contains("supply");
        }

        private static bool IsReturnAirConnector(Connector connector)
        {
            string classification = ReadConnectorClassificationText(connector).ToLowerInvariant();
            return classification.Contains("return");
        }

        private static string ReadConnectorClassificationText(Connector connector)
        {
            if (connector == null)
            {
                return string.Empty;
            }

            List<string> values = new List<string>();
            AddReflectionPropertyValue(values, connector, "DuctSystemType");
            AddReflectionPropertyValue(values, connector, "AssignedDuctSystemType");
            AddReflectionPropertyValue(values, connector, "SystemClassification");
            AddReflectionPropertyValue(values, connector, "SystemType");

            try
            {
                MEPSystem system = connector.MEPSystem;
                if (system != null)
                {
                    values.Add(system.Name ?? string.Empty);
                }
            }
            catch
            {
            }

            try
            {
                object info = InvokeReflectionMethod(connector, "GetMEPConnectorInfo");
                if (info != null)
                {
                    AddReflectionPropertyValue(values, info, "DuctSystemType");
                    AddReflectionPropertyValue(values, info, "SystemClassification");
                    AddReflectionPropertyValue(values, info, "SystemType");
                }
            }
            catch
            {
            }

            string joined = string.Join(" | ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] ConnectorClassification Origin=" + FormatPointMm(connector.Origin) +
                ", Values=" + joined);
            return joined;
        }

        private static void AddReflectionPropertyValue(List<string> values, object instance, string propertyName)
        {
            if (values == null || instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            try
            {
                System.Reflection.PropertyInfo property = instance.GetType().GetProperty(propertyName);
                if (property == null || !property.CanRead)
                {
                    return;
                }

                object value = property.GetValue(instance, null);
                if (value != null)
                {
                    values.Add(value.ToString());
                }
            }
            catch
            {
            }
        }

        private static object InvokeReflectionMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
            {
                return null;
            }

            try
            {
                System.Reflection.MethodInfo method = instance.GetType().GetMethod(methodName, Type.EmptyTypes);
                return method != null ? method.Invoke(instance, null) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetConnectorClassificationText(DuctConnectorRole role)
        {
            if (role == DuctConnectorRole.SupplyAir)
            {
                return "Supply Air";
            }

            if (role == DuctConnectorRole.ReturnAir)
            {
                return "Return Air";
            }

            return "HVAC duct";
        }

        private static DuctType ResolveDuctType(Document doc)
        {
            List<DuctType> ductTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>()
                .Where(x => x != null)
                .ToList();

            if (ductTypes.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[RigidDuct] No DuctType elements were found in the current document.");
                return null;
            }

            DuctType ductType = ductTypes
                .OrderByDescending(PreferredDuctTypeScore)
                .ThenBy(x => x.Name ?? string.Empty)
                .FirstOrDefault();

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] Selected DuctType=" + (ductType != null ? ductType.Name : string.Empty) +
                ", Score=" + PreferredDuctTypeScore(ductType).ToString() +
                ", Routing=" + DescribeRoutingPreferences(ductType) +
                ", AvailableDuctTypes=" + string.Join(" | ", ductTypes.Select(DescribeDuctTypeCandidate)));

            // Revit can create straight Duct elements with a very plain DuctType, but NewElbowFitting()
            // normally depends on that DuctType's routing preferences. Prefer a type that already has
            // elbow routing rules, for example "Radius Elbows / Taps" or "Mitered Elbows / Tees".
            // Do not reject a valid DuctType only because its localized/display name does not contain
            // "Rectangular"; some templates name the type simply "Default".
            return ductType;
        }

        private static int PreferredDuctTypeScore(DuctType ductType)
        {
            if (ductType == null)
            {
                return -1;
            }

            int score = PreferredDuctTypeNameScore(ductType.Name);
            string shapeText = ReadDuctTypeShapeText(ductType);
            if (ContainsRectangularToken(shapeText))
            {
                score += 10;
            }

            int elbowRuleCount = GetRoutingRuleCount(ductType, RoutingPreferenceRuleGroupType.Elbows);
            if (elbowRuleCount > 0)
            {
                // This is the most important condition for this command. A type named "Default" may
                // exist and may create straight ducts, but NewElbowFitting() will fail when the duct
                // type has no elbow routing rules.
                score += 100 + Math.Min(elbowRuleCount, 10);
            }

            return score;
        }

        private static int PreferredDuctTypeNameScore(string name)
        {
            string normalized = (name ?? string.Empty).ToLowerInvariant();
            int score = 0;

            if (ContainsRectangularToken(normalized))
            {
                score += 5;
            }

            if (normalized.Contains("radius"))
            {
                score += 4;
            }

            if (normalized.Contains("miter") || normalized.Contains("mitre"))
            {
                score += 3;
            }

            if (normalized.Contains("elbow"))
            {
                score += 3;
            }

            if (normalized.Contains("default") || normalized.Contains("standard"))
            {
                score += 2;
            }

            if (normalized.Contains("round") || normalized.Contains("oval"))
            {
                score -= 5;
            }

            return score;
        }

        private static string DescribeDuctTypeCandidate(DuctType ductType)
        {
            if (ductType == null)
            {
                return "(null)";
            }

            return (ductType.Name ?? string.Empty) +
                "[shape=" + ReadDuctTypeShapeText(ductType) +
                ",elbows=" + GetRoutingRuleCount(ductType, RoutingPreferenceRuleGroupType.Elbows).ToString() +
                ",score=" + PreferredDuctTypeScore(ductType).ToString() +
                "]";
        }

        private static int GetRoutingRuleCount(DuctType ductType, RoutingPreferenceRuleGroupType groupType)
        {
            if (ductType == null)
            {
                return 0;
            }

            try
            {
                RoutingPreferenceManager manager = ductType.RoutingPreferenceManager;
                return manager != null ? manager.GetNumberOfRules(groupType) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool ContainsRectangularToken(string text)
        {
            string normalized = (text ?? string.Empty).ToLowerInvariant();
            return normalized.Contains("rectangular") ||
                normalized.Contains("rectangle") ||
                normalized.Contains("rect");
        }

        private static string ReadDuctTypeShapeText(DuctType ductType)
        {
            if (ductType == null)
            {
                return string.Empty;
            }

            string[] preferredParameterNames =
            {
                "Shape",
                "Duct Shape"
            };

            foreach (string parameterName in preferredParameterNames)
            {
                string value = ReadParameterText(ductType.LookupParameter(parameterName));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            try
            {
                foreach (Parameter parameter in ductType.Parameters)
                {
                    string parameterName = parameter != null && parameter.Definition != null ? parameter.Definition.Name : string.Empty;
                    if (parameterName.IndexOf("shape", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string value = ReadParameterText(parameter);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string ReadParameterText(Parameter parameter)
        {
            if (parameter == null)
            {
                return string.Empty;
            }

            try
            {
                string valueString = parameter.AsValueString();
                if (!string.IsNullOrWhiteSpace(valueString))
                {
                    return valueString;
                }
            }
            catch
            {
            }

            try
            {
                string stringValue = parameter.AsString();
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    return stringValue;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static MechanicalSystemType ResolveMechanicalSystemType(Document doc, DuctConnectorRole role)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .OrderByDescending(x => PreferredSystemScore(x != null ? x.Name : string.Empty, role))
                .ThenBy(x => x != null ? x.Name : string.Empty)
                .FirstOrDefault();
        }

        private static int PreferredSystemScore(string name, DuctConnectorRole role)
        {
            string normalized = (name ?? string.Empty).ToLowerInvariant();
            if (role == DuctConnectorRole.ReturnAir)
            {
                if (normalized.Contains("return air"))
                {
                    return 4;
                }

                if (normalized.Contains("return"))
                {
                    return 3;
                }
            }

            if (role == DuctConnectorRole.SupplyAir || role == DuctConnectorRole.Any)
            {
                if (normalized.Contains("supply air"))
                {
                    return 4;
                }

                if (normalized.Contains("supply"))
                {
                    return 3;
                }
            }

            if (normalized.Contains("supply air"))
            {
                return 2;
            }

            if (normalized.Contains("air"))
            {
                return 1;
            }

            return 0;
        }

        private static ElementId ResolveLevelId(Document doc, FamilyInstance equipment, Element wall)
        {
            if (equipment != null && equipment.LevelId != ElementId.InvalidElementId)
            {
                return equipment.LevelId;
            }

            if (wall != null && wall.LevelId != ElementId.InvalidElementId)
            {
                return wall.LevelId;
            }

            Level activeLevel = doc.ActiveView != null ? doc.ActiveView.GenLevel : null;
            if (activeLevel != null)
            {
                return activeLevel.Id;
            }

            Level firstLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
            return firstLevel != null ? firstLevel.Id : ElementId.InvalidElementId;
        }

        private static DuctSize ResolveDuctSize(Connector connector, RigidDuctOptions options, string explicitSizeText)
        {
            // SAD/RAD Size dropdown values are demo placeholders. Always prefer the AHU connector
            // size, e.g. 700 x 250, so the generated duct matches the family connector exactly.
            options = options ?? new RigidDuctOptions();
            double widthMm = options.FallbackWidthMm;
            double heightMm = options.FallbackHeightMm;
            bool usedConnectorSize = false;

            try
            {
                if (connector != null && connector.Width > 0 && connector.Height > 0)
                {
                    widthMm = UnitUtils.ConvertFromInternalUnits(connector.Width, UnitTypeId.Millimeters);
                    heightMm = UnitUtils.ConvertFromInternalUnits(connector.Height, UnitTypeId.Millimeters);
                    usedConnectorSize = true;
                }
            }
            catch
            {
            }

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] FinalDuctSize Source=" + (usedConnectorSize ? "Connector" : "Fallback") +
                ", IgnoredUiSize=" + (explicitSizeText ?? string.Empty) +
                ", WidthMm=" + FormatNumber(widthMm) +
                ", HeightMm=" + FormatNumber(heightMm));

            return new DuctSize
            {
                WidthFeet = UnitUtils.ConvertToInternalUnits(widthMm, UnitTypeId.Millimeters),
                HeightFeet = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters),
                WidthMm = widthMm,
                HeightMm = heightMm
            };
        }

        private static bool TryParseRectangularSizeText(string sizeText, out double widthMm, out double heightMm)
        {
            widthMm = 0.0;
            heightMm = 0.0;
            if (string.IsNullOrWhiteSpace(sizeText))
            {
                return false;
            }

            string normalized = sizeText.ToLowerInvariant()
                .Replace("mm", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("×", "x")
                .Replace("*", "x");
            string[] parts = normalized.Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out widthMm) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out heightMm))
            {
                return false;
            }

            return widthMm > 0.0 && heightMm > 0.0;
        }

        private static DuctRoutePoints BuildRoutePoints(Connector equipmentConnector, Wall wall, XYZ wallPoint, RigidDuctOptions options)
        {
            DuctRoutePoints result = new DuctRoutePoints();
            if (equipmentConnector == null || equipmentConnector.Origin == null)
            {
                result.ErrorMessage = "Equipment connector is missing.";
                return result;
            }

            options = options ?? new RigidDuctOptions();

            // DEMO FIXED ROUTE:
            // Return to the stable presentation shape requested by the user:
            //   P0 -> P1  : fixed vertical rise from the AHU connector origin
            //   P1/P2     : elbow corner
            //   P2 -> P3  : horizontal run perpendicular into the selected wall
            // This intentionally ignores the connector BasisX/BasisY for route shape. The connector
            // still supplies the duct size. Skipping AHU ConnectTo() keeps Revit from rejecting the
            // custom family network, while the geometry matches the old screenshot effect.
            double verticalRise = UnitUtils.ConvertToInternalUnits(1200.0, UnitTypeId.Millimeters);
            double wallPenetration = UnitUtils.ConvertToInternalUnits(Math.Max(options.WallPenetrationMm, 150.0), UnitTypeId.Millimeters);
            double minLength = UnitUtils.ConvertToInternalUnits(100.0, UnitTypeId.Millimeters);

            XYZ p0 = equipmentConnector.Origin;
            XYZ p1 = new XYZ(p0.X, p0.Y, p0.Z + verticalRise);

            XYZ targetWallPoint = wallPoint ?? BuildAutoWallFacePoint(wall, p1);
            if (targetWallPoint == null)
            {
                result.ErrorMessage = "Wall face point is missing.";
                return result;
            }

            XYZ horizontalDirection = ResolveWallNormalTowardPoint(wall, p1, targetWallPoint);
            XYZ wallContactPoint = null;
            if (horizontalDirection != null)
            {
                double signedDistanceToPickedFace = DotHorizontal(targetWallPoint - p1, horizontalDirection);
                if (signedDistanceToPickedFace < 0.0)
                {
                    horizontalDirection = horizontalDirection * -1.0;
                    signedDistanceToPickedFace = -signedDistanceToPickedFace;
                }

                if (signedDistanceToPickedFace >= minLength)
                {
                    wallContactPoint = p1 + (horizontalDirection * signedDistanceToPickedFace);
                }
            }

            if (horizontalDirection == null || wallContactPoint == null)
            {
                XYZ wallAtElevation = new XYZ(targetWallPoint.X, targetWallPoint.Y, p1.Z);
                XYZ fallbackDirection = new XYZ(wallAtElevation.X - p1.X, wallAtElevation.Y - p1.Y, 0.0);
                if (fallbackDirection.GetLength() < minLength)
                {
                    // Demo fallback: force a visible horizontal run even if the wall point is almost
                    // above the connector. Pick the longest horizontal connector basis if available;
                    // otherwise run along project X.
                    XYZ basisFallback = ResolveConnectorRouteDirection(equipmentConnector, targetWallPoint);
                    if (basisFallback == null)
                    {
                        basisFallback = XYZ.BasisX;
                    }

                    fallbackDirection = basisFallback * UnitUtils.ConvertToInternalUnits(1800.0, UnitTypeId.Millimeters);
                    wallAtElevation = p1 + fallbackDirection;
                }

                horizontalDirection = fallbackDirection.Normalize();
                wallContactPoint = wallAtElevation;
                DiagnosticRecorder.AppendDebug("[RigidDuct] Demo old-style route used direct/fallback wall direction.");
            }

            XYZ p2 = p1;
            XYZ p3 = wallContactPoint + (horizontalDirection * wallPenetration);
            if (p0.DistanceTo(p1) < minLength || p2.DistanceTo(p3) < minLength)
            {
                result.ErrorMessage = "Duct path is too short.";
                return result;
            }

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] DemoOldStyleElbowRoute WallNormal=" + FormatVector(horizontalDirection) +
                ", P0=" + FormatPointMm(p0) +
                ", P1=" + FormatPointMm(p1) +
                ", P2=" + FormatPointMm(p2) +
                ", P3=" + FormatPointMm(p3) +
                ", VerticalRiseMm=1200" +
                ", WallPenetrationMm=" + FormatNumber(UnitUtils.ConvertFromInternalUnits(wallPenetration, UnitTypeId.Millimeters)));

            result.IsValid = true;
            result.IsSingleSegment = false;
            result.P0 = p0;
            result.P1 = p1;
            result.P2 = p2;
            result.P3 = p3;
            return result;
        }

        private static XYZ ResolveWallTangent(Wall wall, XYZ wallNormal)
        {
            try
            {
                LocationCurve locationCurve = wall != null ? wall.Location as LocationCurve : null;
                Curve curve = locationCurve != null ? locationCurve.Curve : null;
                Line line = curve as Line;
                if (line != null)
                {
                    XYZ tangent = FlattenAndNormalize(line.Direction);
                    if (tangent != null)
                    {
                        return tangent;
                    }
                }
            }
            catch
            {
            }

            if (wallNormal == null)
            {
                return null;
            }

            return FlattenAndNormalize(new XYZ(-wallNormal.Y, wallNormal.X, 0.0));
        }

        private static XYZ BuildAutoWallFacePoint(Wall wall, XYZ fromPoint)
        {
            if (wall == null)
            {
                return null;
            }

            XYZ wallCenter = null;
            try
            {
                LocationCurve locationCurve = wall.Location as LocationCurve;
                Curve curve = locationCurve != null ? locationCurve.Curve : null;
                if (curve != null)
                {
                    wallCenter = curve.Evaluate(0.5, true);
                }
            }
            catch
            {
                wallCenter = null;
            }

            if (wallCenter == null)
            {
                try
                {
                    BoundingBoxXYZ box = wall.get_BoundingBox(null);
                    if (box != null)
                    {
                        wallCenter = (box.Min + box.Max) * 0.5;
                    }
                }
                catch
                {
                    wallCenter = null;
                }
            }

            if (wallCenter == null)
            {
                return null;
            }

            XYZ referencePoint = fromPoint ?? wallCenter;
            XYZ normal = ResolveWallNormalTowardPoint(wall, referencePoint, wallCenter);
            if (normal == null)
            {
                return new XYZ(wallCenter.X, wallCenter.Y, referencePoint.Z);
            }

            double halfWidth = 0.0;
            try
            {
                halfWidth = Math.Max(wall.Width * 0.5, 0.0);
            }
            catch
            {
                halfWidth = 0.0;
            }

            XYZ facePoint = wallCenter + (normal * halfWidth);
            return new XYZ(facePoint.X, facePoint.Y, referencePoint.Z);
        }

        private static XYZ ResolveWallNormalTowardPoint(Wall wall, XYZ fromPoint, XYZ targetPoint)
        {
            XYZ normal = null;
            try
            {
                normal = wall != null ? wall.Orientation : null;
            }
            catch
            {
                normal = null;
            }

            normal = FlattenAndNormalize(normal);
            if (normal == null)
            {
                normal = ResolveWallNormalFromLocationCurve(wall);
            }

            if (normal == null || fromPoint == null || targetPoint == null)
            {
                return normal;
            }

            XYZ towardTarget = FlattenAndNormalize(targetPoint - fromPoint);
            if (towardTarget != null && normal.DotProduct(towardTarget) < 0.0)
            {
                normal = normal * -1.0;
            }

            return normal;
        }

        private static XYZ ResolveWallNormalFromLocationCurve(Wall wall)
        {
            try
            {
                LocationCurve locationCurve = wall != null ? wall.Location as LocationCurve : null;
                Curve curve = locationCurve != null ? locationCurve.Curve : null;
                Line line = curve as Line;
                if (line == null)
                {
                    return null;
                }

                XYZ tangent = FlattenAndNormalize(line.Direction);
                if (tangent == null)
                {
                    return null;
                }

                return new XYZ(-tangent.Y, tangent.X, 0.0).Normalize();
            }
            catch
            {
                return null;
            }
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
            {
                return null;
            }

            XYZ flattened = new XYZ(vector.X, vector.Y, 0.0);
            return flattened.GetLength() > 1.0e-9 ? flattened.Normalize() : null;
        }

        private static XYZ ResolveConnectorRouteDirection(Connector connector, XYZ targetPoint)
        {
            if (connector == null || connector.Origin == null)
            {
                return null;
            }

            XYZ targetDirection = FlattenAndNormalize(targetPoint - connector.Origin);
            List<XYZ> candidates = new List<XYZ>();

            try
            {
                Transform transform = connector.CoordinateSystem;
                if (transform != null)
                {
                    AddConnectorDirectionCandidate(candidates, transform.BasisX);
                    AddConnectorDirectionCandidate(candidates, transform.BasisX != null ? transform.BasisX.Negate() : null);
                    AddConnectorDirectionCandidate(candidates, transform.BasisY);
                    AddConnectorDirectionCandidate(candidates, transform.BasisY != null ? transform.BasisY.Negate() : null);
                    AddConnectorDirectionCandidate(candidates, transform.BasisZ);
                    AddConnectorDirectionCandidate(candidates, transform.BasisZ != null ? transform.BasisZ.Negate() : null);
                }
            }
            catch
            {
            }

            if (candidates.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RigidDuct] ConnectorDirectionFallbackToTarget TargetDirection=" + FormatVector(targetDirection));
                return targetDirection;
            }

            if (targetDirection == null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[RigidDuct] ConnectorDirectionFallbackFirst BestDirection=" + FormatVector(candidates[0]));
                return candidates[0];
            }

            XYZ best = null;
            double bestScore = double.NegativeInfinity;
            foreach (XYZ candidate in candidates)
            {
                double score = candidate.DotProduct(targetDirection);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] ConnectorDirectionResolved TargetDirection=" + FormatVector(targetDirection) +
                ", BestDirection=" + FormatVector(best) +
                ", BestScore=" + FormatNumber(bestScore) +
                ", Candidates=" + string.Join(" | ", candidates.Select(FormatVector)));

            return best;
        }

        private static void AddConnectorDirectionCandidate(List<XYZ> candidates, XYZ vector)
        {
            XYZ flattened = FlattenAndNormalize(vector);
            if (flattened == null)
            {
                return;
            }

            foreach (XYZ existing in candidates)
            {
                if (existing != null && existing.IsAlmostEqualTo(flattened))
                {
                    return;
                }
            }

            candidates.Add(flattened);
        }

        private static double DotHorizontal(XYZ left, XYZ right)
        {
            if (left == null || right == null)
            {
                return 0.0;
            }

            return (left.X * right.X) + (left.Y * right.Y);
        }

        private static void SetRectangularDuctSize(Duct duct, DuctSize size)
        {
            if (duct == null || size == null)
            {
                return;
            }

            Parameter widthParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            if (widthParam != null && !widthParam.IsReadOnly)
            {
                widthParam.Set(size.WidthFeet);
            }

            Parameter heightParam = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            if (heightParam != null && !heightParam.IsReadOnly)
            {
                heightParam.Set(size.HeightFeet);
            }
        }

        private static void TryConnectEquipmentToDuct(Document doc, Connector equipmentConnector, Connector ductConnector)
        {
            if (equipmentConnector == null || ductConnector == null)
            {
                return;
            }

            try
            {
                equipmentConnector.ConnectTo(ductConnector);
            }
            catch
            {
                try
                {
                    doc.Create.NewElbowFitting(equipmentConnector, ductConnector);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[RigidDuct] Equipment connect failed=" + ex.Message);
                }
            }
        }

        private static bool TryAlignDuctRollToConnector(
            Document doc,
            Duct duct,
            XYZ axisStart,
            XYZ axisEnd,
            Connector targetConnector,
            XYZ ductConnectorReferencePoint)
        {
            if (doc == null || duct == null || axisStart == null || axisEnd == null || targetConnector == null)
            {
                return false;
            }

            try
            {
                XYZ axis = axisEnd - axisStart;
                if (axis.GetLength() < 1.0e-9)
                {
                    return false;
                }

                axis = axis.Normalize();
                Connector ductConnector = FindNearestConnector(duct.ConnectorManager, ductConnectorReferencePoint ?? axisStart);
                if (ductConnector == null)
                {
                    return false;
                }

                XYZ targetWidthAxis = ProjectVectorToPlane(TryGetConnectorBasisX(targetConnector), axis);
                XYZ ductWidthAxis = ProjectVectorToPlane(TryGetConnectorBasisX(ductConnector), axis);

                if (targetWidthAxis == null || ductWidthAxis == null)
                {
                    targetWidthAxis = ProjectVectorToPlane(TryGetConnectorBasisY(targetConnector), axis);
                    ductWidthAxis = ProjectVectorToPlane(TryGetConnectorBasisY(ductConnector), axis);
                }

                if (targetWidthAxis == null || ductWidthAxis == null)
                {
                    return false;
                }

                double angle = SignedAngleOnPlane(ductWidthAxis, targetWidthAxis, axis);
                if (Math.Abs(angle) < (Math.PI / 180.0 * 0.5))
                {
                    DiagnosticRecorder.AppendDebug(
                        "[RigidDuct] Duct roll alignment skipped. AngleDeg=" +
                        FormatNumber(angle * 180.0 / Math.PI));
                    return false;
                }

                Line rotationAxis = Line.CreateBound(axisStart, axisEnd);
                ElementTransformUtils.RotateElement(doc, duct.Id, rotationAxis, angle);
                DiagnosticRecorder.AppendDebug(
                    "[RigidDuct] Duct roll aligned. DuctId=" + duct.Id.IntegerValue.ToString() +
                    ", AngleDeg=" + FormatNumber(angle * 180.0 / Math.PI) +
                    ", TargetBasisX=" + FormatVector(TryGetConnectorBasisX(targetConnector)) +
                    ", BeforeDuctBasisX=" + FormatVector(TryGetConnectorBasisX(ductConnector)));
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RigidDuct] Duct roll alignment failed=" + ex.Message);
                return false;
            }
        }

        private static XYZ TryGetConnectorBasisX(Connector connector)
        {
            try
            {
                Transform transform = connector != null ? connector.CoordinateSystem : null;
                return transform != null ? transform.BasisX : null;
            }
            catch
            {
                return null;
            }
        }

        private static XYZ TryGetConnectorBasisY(Connector connector)
        {
            try
            {
                Transform transform = connector != null ? connector.CoordinateSystem : null;
                return transform != null ? transform.BasisY : null;
            }
            catch
            {
                return null;
            }
        }

        private static XYZ ProjectVectorToPlane(XYZ vector, XYZ planeNormal)
        {
            if (vector == null || planeNormal == null)
            {
                return null;
            }

            XYZ normal = planeNormal;
            if (normal.GetLength() < 1.0e-9)
            {
                return null;
            }

            normal = normal.Normalize();
            XYZ projected = vector - (normal * vector.DotProduct(normal));
            return projected.GetLength() > 1.0e-9 ? projected.Normalize() : null;
        }

        private static double SignedAngleOnPlane(XYZ fromVector, XYZ toVector, XYZ planeNormal)
        {
            if (fromVector == null || toVector == null || planeNormal == null)
            {
                return 0.0;
            }

            XYZ from = fromVector.Normalize();
            XYZ to = toVector.Normalize();
            XYZ normal = planeNormal.Normalize();
            double sin = normal.DotProduct(from.CrossProduct(to));
            double cos = Clamp(from.DotProduct(to), -1.0, 1.0);
            return Math.Atan2(sin, cos);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static void LogPreCreateDiagnostics(
            Connector equipmentConnector,
            DuctSize size,
            DuctRoutePoints route,
            DuctType ductType,
            MechanicalSystemType systemType)
        {
            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] PreCreate EquipmentOrigin=" + FormatPointMm(equipmentConnector != null ? equipmentConnector.Origin : null) +
                ", EquipmentDirection=" + FormatVector(TryGetConnectorDirection(equipmentConnector)) +
                ", DuctWidthMm=" + FormatNumber(size != null ? size.WidthMm : 0.0) +
                ", DuctHeightMm=" + FormatNumber(size != null ? size.HeightMm : 0.0) +
                ", P0=" + FormatPointMm(route != null ? route.P0 : null) +
                ", P1=" + FormatPointMm(route != null ? route.P1 : null) +
                ", P2=" + FormatPointMm(route != null ? route.P2 : null) +
                ", P3=" + FormatPointMm(route != null ? route.P3 : null) +
                ", DuctType=" + (ductType != null ? ductType.Name : string.Empty) +
                ", SystemType=" + (systemType != null ? systemType.Name : string.Empty) +
                ", Routing=" + DescribeRoutingPreferences(ductType));
        }

        private static void LogElbowDiagnostics(
            Connector equipmentConnector,
            Connector verticalEnd,
            Connector horizontalStart,
            DuctRoutePoints route,
            DuctSize size,
            DuctType ductType,
            MechanicalSystemType systemType)
        {
            double distanceMm = verticalEnd != null && horizontalStart != null
                ? UnitUtils.ConvertFromInternalUnits(verticalEnd.Origin.DistanceTo(horizontalStart.Origin), UnitTypeId.Millimeters)
                : 0.0;

            DiagnosticRecorder.AppendDebug(
                "[RigidDuct] ElbowAttempt EquipmentOrigin=" + FormatPointMm(equipmentConnector != null ? equipmentConnector.Origin : null) +
                ", EquipmentDirection=" + FormatVector(TryGetConnectorDirection(equipmentConnector)) +
                ", DuctWidthMm=" + FormatNumber(size != null ? size.WidthMm : 0.0) +
                ", DuctHeightMm=" + FormatNumber(size != null ? size.HeightMm : 0.0) +
                ", P0=" + FormatPointMm(route != null ? route.P0 : null) +
                ", P1=" + FormatPointMm(route != null ? route.P1 : null) +
                ", P2=" + FormatPointMm(route != null ? route.P2 : null) +
                ", P3=" + FormatPointMm(route != null ? route.P3 : null) +
                ", VerticalEndOrigin=" + FormatPointMm(verticalEnd != null ? verticalEnd.Origin : null) +
                ", VerticalEndDirection=" + FormatVector(TryGetConnectorDirection(verticalEnd)) +
                ", HorizontalStartOrigin=" + FormatPointMm(horizontalStart != null ? horizontalStart.Origin : null) +
                ", HorizontalStartDirection=" + FormatVector(TryGetConnectorDirection(horizontalStart)) +
                ", ConnectorDistanceMm=" + FormatNumber(distanceMm) +
                ", DuctType=" + (ductType != null ? ductType.Name : string.Empty) +
                ", SystemType=" + (systemType != null ? systemType.Name : string.Empty) +
                ", Routing=" + DescribeRoutingPreferences(ductType));
        }

        private static void LogElbowFailureDiagnostics(
            Exception ex,
            Connector equipmentConnector,
            Connector verticalEnd,
            Connector horizontalStart,
            DuctRoutePoints route,
            DuctSize size,
            DuctType ductType,
            MechanicalSystemType systemType)
        {
            LogElbowDiagnostics(equipmentConnector, verticalEnd, horizontalStart, route, size, ductType, systemType);
            DiagnosticRecorder.AppendDebug("[RigidDuct] ElbowFailure Exception=" + ex);
        }

        private static string DescribeRoutingPreferences(DuctType ductType)
        {
            if (ductType == null)
            {
                return "DuctTypeMissing";
            }

            try
            {
                RoutingPreferenceManager manager = ductType.RoutingPreferenceManager;
                if (manager == null)
                {
                    return "RoutingManagerMissing";
                }

                int count = manager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                return "ElbowRules=" + count.ToString();
            }
            catch (Exception ex)
            {
                return "RoutingReadFailed:" + ex.GetType().Name;
            }
        }

        private static XYZ TryGetConnectorDirection(Connector connector)
        {
            try
            {
                Transform transform = connector != null ? connector.CoordinateSystem : null;
                if (transform != null && transform.BasisZ != null)
                {
                    return transform.BasisZ;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string FormatPointMm(XYZ point)
        {
            if (point == null)
            {
                return "(null)";
            }

            return "(" +
                FormatNumber(UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters)) + ", " +
                FormatNumber(UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters)) + ", " +
                FormatNumber(UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters)) + ")mm";
        }

        private static string FormatVector(XYZ vector)
        {
            if (vector == null)
            {
                return "(null)";
            }

            return "(" + FormatNumber(vector.X) + ", " + FormatNumber(vector.Y) + ", " + FormatNumber(vector.Z) + ")";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static Connector FindNearestConnector(ConnectorManager connectorManager, XYZ referencePoint)
        {
            ConnectorSet connectorSet = connectorManager != null ? connectorManager.Connectors : null;
            if (connectorSet == null || referencePoint == null)
            {
                return null;
            }

            return connectorSet
                .Cast<Connector>()
                .Where(x => x != null && x.Origin != null)
                .OrderBy(x => x.Origin.DistanceTo(referencePoint))
                .FirstOrDefault();
        }

        private sealed class WallPointSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Wall;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }

        private sealed class DuctSize
        {
            public double WidthFeet { get; set; }

            public double HeightFeet { get; set; }

            public double WidthMm { get; set; }

            public double HeightMm { get; set; }
        }

        private sealed class DuctRoutePoints
        {
            public bool IsValid { get; set; }

            public string ErrorMessage { get; set; }

            public XYZ P0 { get; set; }

            public XYZ P1 { get; set; }

            public XYZ P2 { get; set; }

            public XYZ P3 { get; set; }


            public bool IsSingleSegment { get; set; }
        }
    }
}
