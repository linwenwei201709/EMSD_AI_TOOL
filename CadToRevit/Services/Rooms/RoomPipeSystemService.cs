using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomPipeSystemService
    {
        internal sealed class PipeWallPickResult
        {
            public bool Succeeded { get; set; }

            public bool Canceled { get; set; }

            public string Message { get; set; }

            public ElementId WallElementId { get; set; } = ElementId.InvalidElementId;

            public XYZ PickPoint { get; set; }

            public string DisplayName { get; set; }
        }

        internal sealed class CreatePipeResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public ElementId PipeElementId { get; set; } = ElementId.InvalidElementId;
        }

        internal sealed class CreatePipeWorkResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public List<ElementId> CreatedElementIds { get; } = new List<ElementId>();
        }

        internal sealed class CreatePipeRunResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public List<ElementId> CreatedElementIds { get; } = new List<ElementId>();
        }

        internal sealed class PlaceBuiltInPipeAssemblyResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public string TemplatePath { get; set; }

            public List<ElementId> CreatedElementIds { get; } = new List<ElementId>();
        }

        internal sealed class PipeWorkOptions
        {
            public double VerticalRiseMm { get; set; } = 300.0;

            public double SideOffsetMm { get; set; } = 350.0;

            public double PreWallOffsetMm { get; set; } = 900.0;

            public double NearWallOffsetMm { get; set; } = 220.0;

            public double WallPenetrationMm { get; set; } = 220.0;

            public double FallbackDiameterMm { get; set; } = 65.0;

            public double DemoDiameterMm { get; set; } = 65.0;

            public double ValveBranchLengthMm { get; set; } = 300.0;

            public double ValveBranchDiameterMm { get; set; } = 60.0;

            public double MinSegmentLengthMm { get; set; } = 120.0;
        }

        private const double BuiltInPipeVisualSetbackMm = 30.0;

        private const int BuiltInPipeInsulationTransparency = 70;

        private enum PipeConnectorRole
        {
            Any,
            Chws,
            Chwr
        }

        private sealed class BuiltInPipePortSnapshot
        {
            public ElementId OwnerId { get; set; } = ElementId.InvalidElementId;

            public string OwnerName { get; set; }

            public XYZ Origin { get; set; }

            public XYZ Direction { get; set; }

            public string Classification { get; set; }

            public string InsulationType { get; set; }

            public PipeConnectorRole Role { get; set; }

            public string RoleEvidence { get; set; }

            public bool IsOpen { get; set; }

            public bool IsVertical { get; set; }

            public bool IsFlange { get; set; }
        }

        private sealed class BuiltInPipeAnchorSelection
        {
            public BuiltInPipePortSnapshot SourcePort { get; set; }

            public Connector TargetConnector { get; set; }

            public XYZ Translation { get; set; }

            public double ScoreMm { get; set; }

            public double DirectionDot { get; set; }
        }

        private sealed class BuiltInPipePairSelection
        {
            public BuiltInPipePortSnapshot SourcePortA { get; set; }

            public BuiltInPipePortSnapshot SourcePortB { get; set; }

            public Connector TargetConnectorA { get; set; }

            public Connector TargetConnectorB { get; set; }

            public XYZ Translation { get; set; }

            public double SourceSpacingMm { get; set; }

            public double TargetSpacingMm { get; set; }

            public double SpacingDifferenceMm { get; set; }

            public double AxisAbsDot { get; set; }

            public double EndpointErrorAMm { get; set; }

            public double EndpointErrorBMm { get; set; }

            public double Score { get; set; }

            public bool TargetOrderSwapped { get; set; }

            public XYZ SourceMidpoint { get; set; }

            public XYZ TargetMidpoint { get; set; }

            public double RotationRadians { get; set; }

            public double RotationDegrees { get; set; }

            public double TransformedSourceCenterDistanceMm { get; set; }
        }

        internal static PlaceBuiltInPipeAssemblyResult PlaceBuiltInPipeAssemblyAtEquipmentCenter(
            Document hostDoc,
            ElementId equipmentInstanceId)
        {
            PlaceBuiltInPipeAssemblyResult result = new PlaceBuiltInPipeAssemblyResult();
            if (hostDoc == null)
            {
                result.Message = "Active document is not available.";
                return result;
            }

            if (equipmentInstanceId == null || equipmentInstanceId == ElementId.InvalidElementId)
            {
                result.Message = "AHU equipment instance id is missing.";
                return result;
            }

            FamilyInstance equipment = hostDoc.GetElement(equipmentInstanceId) as FamilyInstance;
            if (equipment == null)
            {
                result.Message = "AHU equipment family instance was not found.";
                return result;
            }

            BoundingBoxXYZ equipmentBox = equipment.get_BoundingBox(null);
            TryGetBoundingBoxCenter(equipmentBox, out XYZ equipmentCenter);

            List<Connector> targetConnectors = GetEquipmentPipeConnectors(equipment)
                .Where(x => x != null && (IsChwsConnector(x) || IsChwrConnector(x)))
                .Where(x => TryGetConnectorOrigin(x, out XYZ _))
                .ToList();

            if (targetConnectors.Count < 2)
            {
                result.Message = "Two AHU CHWS/CHWR piping connectors are required for visual alignment.";
                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.VisualMidpoint] AHU does not expose two CHWS/CHWR target connectors. EquipmentId=" +
                    equipmentInstanceId.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                    ", ConnectorCount=" + targetConnectors.Count.ToString(CultureInfo.InvariantCulture));
                return result;
            }

            string templatePath = FindBuiltInPipeAssemblyPath();
            result.TemplatePath = templatePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
            {
                result.Message = @"RevitLinkInstance\内置管道.rvt was not found.";
                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.VisualMidpoint] Template missing. Expected=RevitLinkInstance\\内置管道.rvt");
                return result;
            }

            Document sourceDoc = null;
            bool closeSourceDoc = false;
            try
            {
                sourceDoc = FindOpenDocumentByPath(hostDoc, templatePath);
                if (sourceDoc == null)
                {
                    // The template is opened only as a background copy source. Opening/closing it
                    // fires DocumentOpened/DocumentClosed, therefore the host Ribbon state is
                    // explicitly restored in finally.
                    sourceDoc = hostDoc.Application.OpenDocumentFile(templatePath);
                    closeSourceDoc = sourceDoc != null;
                }

                if (sourceDoc == null)
                {
                    result.Message = "Failed to open the built-in pipe RVT template.";
                    return result;
                }

                if (sourceDoc.Equals(hostDoc))
                {
                    result.Message = "The built-in pipe template cannot be the same document as the active project.";
                    return result;
                }

                List<ElementId> sourceElementIds = CollectBuiltInPipeAssemblyElementIds(sourceDoc);
                if (sourceElementIds.Count == 0)
                {
                    result.Message = "No supported pipe assembly model elements were found in 内置管道.rvt.";
                    return result;
                }

                BoundingBoxXYZ sourceBox = GetCombinedBoundingBox(sourceDoc, sourceElementIds);
                TryGetBoundingBoxCenter(sourceBox, out XYZ sourceCenter);

                List<BuiltInPipePortSnapshot> sourcePorts =
                    CollectBuiltInPipePortSnapshots(sourceDoc, sourceElementIds);

                // Only use straight/vertical top ports as positioning references. This intentionally
                // excludes the bent side connector visible on the third branch of the template.
                List<BuiltInPipePortSnapshot> verticalPorts = sourcePorts
                    .Where(x => x != null && x.Origin != null && x.IsVertical)
                    .ToList();

                List<BuiltInPipePortSnapshot> verticalOpenPorts = verticalPorts
                    .Where(x => x.IsOpen)
                    .ToList();

                // Some legacy template families do not report IsConnected consistently when the
                // RVT is opened through the API. Prefer open ports, but fall back to vertical ports.
                List<BuiltInPipePortSnapshot> candidatePorts =
                    verticalOpenPorts.Count >= 2 ? verticalOpenPorts : verticalPorts;

                if (candidatePorts.Count < 2)
                {
                    result.Message = "Two vertical pipe connector references were not found in 内置管道.rvt.";
                    return result;
                }

                // Keep only the upper connector zone. For the current standard assembly this keeps
                // the two AHU-facing flange ports and rejects lower/internal MEP connectors.
                double topTolerance = UnitUtils.ConvertToInternalUnits(250.0, UnitTypeId.Millimeters);
                double maxPortZ = candidatePorts.Max(x => x.Origin.Z);
                List<BuiltInPipePortSnapshot> topPorts = candidatePorts
                    .Where(x => maxPortZ - x.Origin.Z <= topTolerance)
                    .OrderByDescending(x => x.IsFlange)
                    .ThenByDescending(x => x.Origin.Z)
                    .ToList();

                if (topPorts.Count < 2)
                {
                    topPorts = candidatePorts
                        .OrderByDescending(x => x.IsFlange)
                        .ThenByDescending(x => x.Origin.Z)
                        .Take(6)
                        .ToList();
                }

                BuiltInPipePairSelection pair = SelectBuiltInPipeVisualMidpointPair(
                    topPorts,
                    targetConnectors,
                    sourceCenter,
                    equipmentCenter);

                if (pair == null ||
                    pair.SourcePortA == null ||
                    pair.SourcePortB == null ||
                    pair.TargetConnectorA == null ||
                    pair.TargetConnectorB == null ||
                    pair.Translation == null)
                {
                    result.Message = "Cannot match the two top pipe connectors in 内置管道.rvt to the AHU connector pair.";
                    return result;
                }

                double visualSetbackFeet = UnitUtils.ConvertToInternalUnits(
                    BuiltInPipeVisualSetbackMm,
                    UnitTypeId.Millimeters);
                XYZ visualSetback = XYZ.BasisZ.Negate() * visualSetbackFeet;
                XYZ finalTranslation = pair.Translation + visualSetback;

                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.VisualMidpoint] Pair selected. SourceA=" +
                    DescribeBuiltInPipePort(pair.SourcePortA) +
                    ", SourceB=" + DescribeBuiltInPipePort(pair.SourcePortB) +
                    ", TargetA=" + DescribeConnector(pair.TargetConnectorA) +
                    ", TargetB=" + DescribeConnector(pair.TargetConnectorB) +
                    ", SourceSpacingMm=" + FormatNumber(pair.SourceSpacingMm) +
                    ", TargetSpacingMm=" + FormatNumber(pair.TargetSpacingMm) +
                    ", SpacingDifferenceMm=" + FormatNumber(pair.SpacingDifferenceMm) +
                    ", AxisAbsDotBeforeRotation=" + FormatNumber(pair.AxisAbsDot) +
                    ", RotationZDeg=" + FormatNumber(pair.RotationDegrees) +
                    ", TransformedSourceCenterDistanceMm=" + FormatNumber(pair.TransformedSourceCenterDistanceMm) +
                    ", AlignmentTranslation=" + FormatPointMm(pair.Translation) +
                    ", VisualSetbackMm=" + FormatNumber(BuiltInPipeVisualSetbackMm) +
                    ", FinalTranslation=" + FormatPointMm(finalTranslation) +
                    ", Rotation=Z_ONLY, Mirror=NONE, ConnectTo=NONE");

                ICollection<ElementId> copiedIds;
                using (Transaction tx = new Transaction(hostDoc, "Place Built-in Pipe Assembly by Connector Pair"))
                {
                    tx.Start();
                    FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                    failureOptions.SetFailuresPreprocessor(
                        new NonCriticalWarningsPreprocessor("BuiltInPipeAssembly.VisualMidpoint.Copy"));
                    failureOptions.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(failureOptions);

                    try
                    {
                        CopyPasteOptions copyOptions = new CopyPasteOptions();
                        copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationDuplicateTypeNamesHandler());

                        // FINAL VISUAL-ONLY STRATEGY:
                        // 1. copy the whole standard RVT assembly as one rigid MEP network;
                        // 2. translate the template connector midpoint onto the AHU connector midpoint;
                        // 3. rotate ONLY around the global Z axis so the two connector axes become parallel;
                        // 4. never mirror and never call Connector.ConnectTo().
                        //
                        // The previous midpoint-only version correctly aligned the centers but could leave the
                        // template connector pair 90 degrees to the AHU pair. A Z-only rotation keeps every
                        // valve/gauge vertical and therefore does not change any element's angle to the ground.
                        Transform copyTransform = Transform.CreateTranslation(finalTranslation);
                        copiedIds = ElementTransformUtils.CopyElements(
                            sourceDoc,
                            sourceElementIds,
                            hostDoc,
                            copyTransform,
                            copyOptions);

                        hostDoc.Regenerate();

                        if (copiedIds != null &&
                            copiedIds.Count > 0 &&
                            pair.TargetMidpoint != null &&
                            Math.Abs(pair.RotationRadians) > 1.0e-8)
                        {
                            Line rotationAxis = Line.CreateBound(
                                pair.TargetMidpoint,
                                pair.TargetMidpoint + XYZ.BasisZ);
                            ElementTransformUtils.RotateElements(
                                hostDoc,
                                copiedIds,
                                rotationAxis,
                                pair.RotationRadians);
                            hostDoc.Regenerate();
                        }

                        ApplyBuiltInPipeViewDisplay(hostDoc, hostDoc.ActiveView);

                        TransactionStatus copyStatus = tx.Commit();
                        if (copyStatus != TransactionStatus.Committed)
                        {
                            result.Message = "The built-in pipe assembly copy transaction was rolled back.";
                            return result;
                        }
                    }
                    catch
                    {
                        if (tx.HasStarted())
                        {
                            tx.RollBack();
                        }

                        throw;
                    }
                }

                if (copiedIds != null)
                {
                    result.CreatedElementIds.AddRange(
                        copiedIds
                            .Where(x => x != null && x != ElementId.InvalidElementId)
                            .Distinct(new ElementIdIntegerComparer()));
                }

                if (result.CreatedElementIds.Count == 0)
                {
                    result.Message = "The built-in pipe RVT was opened, but no elements were copied.";
                    return result;
                }

                // Verify the connector-pair alignment separately from the intentional 30 mm
                // visual setback. The setback deliberately keeps the copied flange from visually
                // penetrating the AHU connector, so it must not be treated as a template mismatch.
                XYZ alignedA = RotatePointAroundZ(
                    pair.SourcePortA.Origin + pair.Translation,
                    pair.TargetMidpoint,
                    pair.RotationRadians);
                XYZ alignedB = RotatePointAroundZ(
                    pair.SourcePortB.Origin + pair.Translation,
                    pair.TargetMidpoint,
                    pair.RotationRadians);
                XYZ movedA = RotatePointAroundZ(
                    pair.SourcePortA.Origin + finalTranslation,
                    pair.TargetMidpoint,
                    pair.RotationRadians);
                XYZ movedB = RotatePointAroundZ(
                    pair.SourcePortB.Origin + finalTranslation,
                    pair.TargetMidpoint,
                    pair.RotationRadians);
                TryGetConnectorOrigin(pair.TargetConnectorA, out XYZ targetAOrigin);
                TryGetConnectorOrigin(pair.TargetConnectorB, out XYZ targetBOrigin);

                double endpointErrorAMm = targetAOrigin != null
                    ? UnitUtils.ConvertFromInternalUnits(alignedA.DistanceTo(targetAOrigin), UnitTypeId.Millimeters)
                    : double.NaN;
                double endpointErrorBMm = targetBOrigin != null
                    ? UnitUtils.ConvertFromInternalUnits(alignedB.DistanceTo(targetBOrigin), UnitTypeId.Millimeters)
                    : double.NaN;
                double postSetbackOriginGapAMm = targetAOrigin != null
                    ? UnitUtils.ConvertFromInternalUnits(movedA.DistanceTo(targetAOrigin), UnitTypeId.Millimeters)
                    : double.NaN;
                double postSetbackOriginGapBMm = targetBOrigin != null
                    ? UnitUtils.ConvertFromInternalUnits(movedB.DistanceTo(targetBOrigin), UnitTypeId.Millimeters)
                    : double.NaN;
                double maxEndpointErrorMm = Math.Max(
                    double.IsNaN(endpointErrorAMm) ? 0.0 : endpointErrorAMm,
                    double.IsNaN(endpointErrorBMm) ? 0.0 : endpointErrorBMm);

                string visualFit = maxEndpointErrorMm <= 2.0
                    ? "Excellent"
                    : maxEndpointErrorMm <= 5.0
                        ? "Good"
                        : maxEndpointErrorMm <= 20.0
                            ? "Acceptable-Warning"
                            : "TemplateMismatch-Warning";

                result.Succeeded = true;
                result.Message = "Success";

                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.VisualMidpoint] Success. Template=" + templatePath +
                    ", EquipmentId=" + equipmentInstanceId.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                    ", SourceCount=" + sourceElementIds.Count.ToString(CultureInfo.InvariantCulture) +
                    ", CreatedCount=" + result.CreatedElementIds.Count.ToString(CultureInfo.InvariantCulture) +
                    ", SourceCenter=" + FormatPointMm(sourceCenter) +
                    ", EquipmentCenter=" + FormatPointMm(equipmentCenter) +
                    ", SourceSpacingMm=" + FormatNumber(pair.SourceSpacingMm) +
                    ", TargetSpacingMm=" + FormatNumber(pair.TargetSpacingMm) +
                    ", SpacingDifferenceMm=" + FormatNumber(pair.SpacingDifferenceMm) +
                    ", AlignmentEndpointErrorAMm=" + FormatNumber(endpointErrorAMm) +
                    ", AlignmentEndpointErrorBMm=" + FormatNumber(endpointErrorBMm) +
                    ", PostSetbackOriginGapAMm=" + FormatNumber(postSetbackOriginGapAMm) +
                    ", PostSetbackOriginGapBMm=" + FormatNumber(postSetbackOriginGapBMm) +
                    ", VisualFit=" + visualFit +
                    ", RotationZDeg=" + FormatNumber(pair.RotationDegrees) +
                    ", TransformedSourceCenterDistanceMm=" + FormatNumber(pair.TransformedSourceCenterDistanceMm) +
                    ", AlignmentTranslation=" + FormatPointMm(pair.Translation) +
                    ", VisualSetbackMm=" + FormatNumber(BuiltInPipeVisualSetbackMm) +
                    ", FinalTranslation=" + FormatPointMm(finalTranslation) +
                    ", PipeInsulationTransparency=" + BuiltInPipeInsulationTransparency.ToString(CultureInfo.InvariantCulture) +
                    ", Rotation=Z_ONLY, Mirror=NONE, ConnectTo=NONE");

                return result;
            }
            catch (Exception ex)
            {
                result.Message = "Failed to place 内置管道.rvt by visual connector midpoint: " + ex.Message;
                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.VisualMidpoint] Failed. Template=" + (templatePath ?? string.Empty) +
                    ", EquipmentId=" + equipmentInstanceId.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                    ", Error=" + ex);
                return result;
            }
            finally
            {
                if (closeSourceDoc && sourceDoc != null)
                {
                    try
                    {
                        sourceDoc.Close(false);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[BuiltInPipeAssembly.VisualMidpoint] Failed to close template document. Error=" + ex.Message);
                    }
                }

                // Background template open/close can temporarily trigger the ordinary-RVT Ribbon
                // rule in App.cs. Restore availability for the actual host project afterwards.
                try
                {
                    CadToRevit.App.UpdateRibbonButtonAvailability(hostDoc);
                    DiagnosticRecorder.AppendDebug(
                        "[BuiltInPipeAssembly.VisualMidpoint] Host ribbon availability restored after background RVT close.");
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[BuiltInPipeAssembly.VisualMidpoint] Failed to restore host ribbon availability. Error=" +
                        ex.Message);
                }
            }
        }

        private static void ApplyBuiltInPipeViewDisplay(Document doc, View view)
        {
            if (doc == null || view == null)
            {
                return;
            }

            try
            {
                Category pipeInsulationCategory = Category.GetCategory(
                    doc,
                    BuiltInCategory.OST_PipeInsulations);
                if (pipeInsulationCategory == null)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[BuiltInPipeAssembly.Display] Pipe Insulations category was not found.");
                    return;
                }

                if (view.CanCategoryBeHidden(pipeInsulationCategory.Id) &&
                    view.GetCategoryHidden(pipeInsulationCategory.Id))
                {
                    view.SetCategoryHidden(pipeInsulationCategory.Id, false);
                }

                OverrideGraphicSettings overrides = view.GetCategoryOverrides(pipeInsulationCategory.Id);
                overrides.SetSurfaceTransparency(BuiltInPipeInsulationTransparency);
                view.SetCategoryOverrides(pipeInsulationCategory.Id, overrides);

                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.Display] Applied Pipe Insulations surface transparency=" +
                    BuiltInPipeInsulationTransparency.ToString(CultureInfo.InvariantCulture) +
                    " to ViewId=" + view.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                    ", ViewName=" + (view.Name ?? string.Empty));
            }
            catch (Exception ex)
            {
                // Display styling is visual-only and must never roll back the successfully copied
                // pipe assembly. Keep the model placement even if this particular view does not
                // support category overrides (for example because of a restrictive view template).
                DiagnosticRecorder.AppendDebug(
                    "[BuiltInPipeAssembly.Display] Failed to apply Pipe Insulations transparency. ViewId=" +
                    view.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                    ", Error=" + ex.Message);
            }
        }

        internal static PipeWallPickResult PickWallPoint(UIDocument uiDoc)
        {
            PipeWallPickResult result = new PipeWallPickResult();
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
                    "Pick a point on a wall for the pipe end.");

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

        internal static CreatePipeWorkResult CreateChilledWaterPipeWork(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId chwsWallElementId,
            string chwsSizeText,
            ElementId chwrWallElementId,
            string chwrSizeText,
            PipeWorkOptions options)
        {
            CreatePipeWorkResult result = new CreatePipeWorkResult();
            if (doc == null)
            {
                result.Message = "Document is null.";
                return result;
            }

            options = options ?? new PipeWorkOptions();
            using (TransactionGroup group = new TransactionGroup(doc, "Create CHW Pipework"))
            {
                group.Start();

                CreatePipeRunResult chwsResult = CreateDemoPipeRunToWall(
                    doc,
                    equipmentInstanceId,
                    chwsWallElementId,
                    chwsSizeText,
                    options,
                    PipeConnectorRole.Chws,
                    "CHWS");

                if (chwsResult == null || !chwsResult.Succeeded)
                {
                    group.RollBack();
                    result.Message = chwsResult != null && !string.IsNullOrWhiteSpace(chwsResult.Message)
                        ? chwsResult.Message
                        : "Create CHWS pipe failed.";
                    return result;
                }

                CreatePipeRunResult chwrResult = CreateDemoPipeRunToWall(
                    doc,
                    equipmentInstanceId,
                    chwrWallElementId,
                    chwrSizeText,
                    options,
                    PipeConnectorRole.Chwr,
                    "CHWR");

                if (chwrResult == null || !chwrResult.Succeeded)
                {
                    group.RollBack();
                    result.Message = chwrResult != null && !string.IsNullOrWhiteSpace(chwrResult.Message)
                        ? chwrResult.Message
                        : "Create CHWR pipe failed.";
                    return result;
                }

                result.CreatedElementIds.AddRange(chwsResult.CreatedElementIds);
                result.CreatedElementIds.AddRange(chwrResult.CreatedElementIds);
                result.Succeeded = true;
                result.Message = "Success";
                group.Assimilate();
                return result;
            }
        }

        internal static CreatePipeResult CreateSinglePipe(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId wallElementId,
            XYZ wallPoint,
            string diameterText)
        {
            CreatePipeResult result = new CreatePipeResult();
            if (doc == null)
            {
                result.Message = "Document is null.";
                return result;
            }

            FamilyInstance equipment = doc.GetElement(equipmentInstanceId) as FamilyInstance;
            if (equipment == null)
            {
                result.Message = "Current equipment instance was not found.";
                return result;
            }

            Element wall = doc.GetElement(wallElementId);
            if (!(wall is Wall))
            {
                result.Message = "Selected wall was not found.";
                return result;
            }

            if (wallPoint == null)
            {
                result.Message = "Wall point is missing.";
                return result;
            }

            Connector equipmentConnector = FindPipeConnector(equipment, PipeConnectorRole.Any);
            if (equipmentConnector == null)
            {
                result.Message = "No pipe connector was found on the current equipment.";
                return result;
            }

            PipeType pipeType = ResolvePipeType(doc);
            if (pipeType == null)
            {
                result.Message = "No pipe type is available in the current model.";
                return result;
            }

            PipingSystemType systemType = ResolvePipingSystemType(doc, PipeConnectorRole.Any);
            if (systemType == null)
            {
                result.Message = "No piping system type is available in the current model.";
                return result;
            }

            ElementId levelId = ResolveLevelId(doc, equipment, wall);
            if (levelId == ElementId.InvalidElementId)
            {
                result.Message = "No level is available for pipe creation.";
                return result;
            }

            XYZ startPoint = equipmentConnector.Origin;
            XYZ endPoint = ProjectPointToWallFace(wall as Wall, wallPoint) ?? wallPoint;
            if (startPoint == null || endPoint == null || startPoint.DistanceTo(endPoint) < 1e-6)
            {
                result.Message = "Pipe path is too short.";
                return result;
            }

            double diameterFeet = ResolvePipeDiameterFeet(equipmentConnector, diameterText, new PipeWorkOptions());

            using (Transaction tx = new Transaction(doc, "Create Room Pipe"))
            {
                tx.Start();
                try
                {
                    Pipe pipe = Pipe.Create(doc, systemType.Id, pipeType.Id, levelId, startPoint, endPoint);
                    if (pipe == null)
                    {
                        throw new InvalidOperationException("Pipe.Create returned null.");
                    }

                    SetPipeDiameter(pipe, diameterFeet);
                    ApplyDemoPipeColorOverrides(doc, PipeConnectorRole.Any, pipe.Id);

                    result.Succeeded = true;
                    result.PipeElementId = pipe.Id;
                    result.Message = "Success";
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    result.Message = ex.Message;
                }
            }

            return result;
        }

        private static CreatePipeRunResult CreateDemoPipeRunToWall(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId wallElementId,
            string uiSizeText,
            PipeWorkOptions options,
            PipeConnectorRole role,
            string label)
        {
            CreatePipeRunResult result = new CreatePipeRunResult();
            if (doc == null)
            {
                result.Message = "Document is null.";
                return result;
            }

            FamilyInstance equipment = doc.GetElement(equipmentInstanceId) as FamilyInstance;
            if (equipment == null)
            {
                result.Message = "Current equipment instance was not found.";
                return result;
            }

            Wall wall = doc.GetElement(wallElementId) as Wall;
            if (wall == null)
            {
                result.Message = label + " wall was not found.";
                return result;
            }

            Connector equipmentConnector = FindPipeConnector(equipment, role);
            if (equipmentConnector == null)
            {
                result.Message = "No " + GetRoleText(role) + " pipe connector was found on the selected equipment.";
                return result;
            }

            PipeType pipeType = ResolvePipeType(doc);
            if (pipeType == null)
            {
                result.Message = "No pipe type is available in the current model.";
                return result;
            }

            PipingSystemType systemType = ResolvePipingSystemType(doc, role);
            if (systemType == null)
            {
                result.Message = "No piping system type is available in the current model.";
                return result;
            }

            ElementId levelId = ResolveLevelId(doc, equipment, wall);
            if (levelId == ElementId.InvalidElementId)
            {
                result.Message = "No level is available for pipe creation.";
                return result;
            }

            List<XYZ> routePoints = BuildDemoPipeRoutePoints(equipmentConnector, wall, options, role);
            if (routePoints == null || routePoints.Count < 2)
            {
                result.Message = "Pipe route points could not be calculated.";
                return result;
            }

            double diameterFeet = ResolvePipeDiameterFeet(equipmentConnector, uiSizeText, options);
            DiagnosticRecorder.AppendDebug(
                "[PipeWork] Create " + label + " route. ConnectorClassification=" + ReadConnectorClassificationText(equipmentConnector) +
                ", DiameterMm=" + FormatNumber(UnitUtils.ConvertFromInternalUnits(diameterFeet, UnitTypeId.Millimeters)) +
                ", IgnoredUiSize=" + (uiSizeText ?? string.Empty));

            using (Transaction tx = new Transaction(doc, "Create " + label + " Pipework"))
            {
                tx.Start();
                try
                {
                    List<Pipe> pipes = new List<Pipe>();
                    for (int i = 0; i < routePoints.Count - 1; i++)
                    {
                        XYZ start = routePoints[i];
                        XYZ end = routePoints[i + 1];
                        if (start == null || end == null || start.DistanceTo(end) < UnitUtils.ConvertToInternalUnits(options.MinSegmentLengthMm, UnitTypeId.Millimeters))
                        {
                            continue;
                        }

                        Pipe pipe = Pipe.Create(doc, systemType.Id, pipeType.Id, levelId, start, end);
                        if (pipe == null)
                        {
                            throw new InvalidOperationException("Pipe.Create returned null for " + label + " segment " + (i + 1).ToString(CultureInfo.InvariantCulture) + ".");
                        }

                        SetPipeDiameter(pipe, diameterFeet);
                        pipes.Add(pipe);
                        result.CreatedElementIds.Add(pipe.Id);
                    }

                    doc.Regenerate();

                    for (int i = 0; i < pipes.Count - 1; i++)
                    {
                        try
                        {
                            XYZ junction = routePoints[Math.Min(i + 1, routePoints.Count - 1)];
                            Connector first = FindNearestConnector(pipes[i], junction);
                            Connector second = FindNearestConnector(pipes[i + 1], junction);
                            if (first != null && second != null)
                            {
                                FamilyInstance elbow = doc.Create.NewElbowFitting(first, second);
                                if (elbow != null)
                                {
                                    result.CreatedElementIds.Add(elbow.Id);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DiagnosticRecorder.AppendDebug("[PipeWork] Demo elbow insertion failed for " + label + "; keeping visible pipe segments. " + ex.Message);
                        }
                    }
                    doc.Regenerate();

                    // DEMO valve mode: auto-load a local Pipe Accessory valve family from
                    // PipeAccessoryFamilyType\\*.rfa when the project has no valve symbol loaded.
                    // Do not ConnectTo() the valve to the pipe network; this keeps the demo stable.
                    TryPlacePipeAccessoryValve(doc, levelId, routePoints, result.CreatedElementIds);
                    doc.Regenerate();

                    // DEMO route mode only: keep the real Revit Pipe elements.
                    // Do not add DirectShape visual sleeves and do not add temporary branch markers.
                    // Route shape remains: connector point -> vertical rise -> short horizontal bend -> high-level run -> wall contact/penetration.
                    ApplyDemoPipeColorOverrides(doc, role, result.CreatedElementIds.ToArray());

                    result.Succeeded = result.CreatedElementIds.Count > 0;
                    result.Message = result.Succeeded ? "Success" : "No pipe segment was created.";
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    DiagnosticRecorder.AppendDebug("[PipeWork] Create " + label + " failed=" + ex);
                    result.Message = ex.Message;
                }
            }

            return result;
        }

        private static List<XYZ> BuildDemoPipeRoutePoints(Connector connector, Wall wall, PipeWorkOptions options, PipeConnectorRole role)
        {
            if (connector == null || connector.Origin == null || wall == null)
            {
                return null;
            }

            options = options ?? new PipeWorkOptions();

            XYZ p0 = connector.Origin;
            double verticalRise = UnitUtils.ConvertToInternalUnits(Math.Max(options.VerticalRiseMm, 300.0), UnitTypeId.Millimeters);
            double firstHorizontalRun = UnitUtils.ConvertToInternalUnits(650.0, UnitTypeId.Millimeters);
            double preWallOffset = UnitUtils.ConvertToInternalUnits(450.0, UnitTypeId.Millimeters);
            double wallPenetration = UnitUtils.ConvertToInternalUnits(Math.Max(options.WallPenetrationMm, 180.0), UnitTypeId.Millimeters);

            // DEMO requirement:
            // 1) P0 -> P1 must be a pure vertical riser from the pipe connector.
            // 2) P1 -> P2/P3 is the high-level dog-leg run.
            // 3) P3 -> P4 is perpendicular to the selected wall, so the pipe visibly touches / penetrates the wall.
            XYZ p1 = p0 + XYZ.BasisZ * verticalRise;

            XYZ wallFacePoint = ProjectPointToWallFace(wall, p1) ?? p1;
            wallFacePoint = new XYZ(wallFacePoint.X, wallFacePoint.Y, p1.Z);

            XYZ wallNormalToConnector = ResolveWallNormalTowardPoint(wall, p1);
            if (wallNormalToConnector == null)
            {
                wallNormalToConnector = FlattenAndNormalize(p1 - wallFacePoint) ?? XYZ.BasisX;
            }

            XYZ preWallPoint = wallFacePoint + wallNormalToConnector * preWallOffset;
            XYZ mainDirection = FlattenAndNormalize(preWallPoint - p1);
            if (mainDirection == null)
            {
                mainDirection = wallNormalToConnector.Negate();
            }

            // Create one short horizontal segment after the vertical riser so the route visibly bends.
            double availableDistance = p1.DistanceTo(preWallPoint);
            double runLength = Math.Min(firstHorizontalRun, Math.Max(availableDistance * 0.35, UnitUtils.ConvertToInternalUnits(350.0, UnitTypeId.Millimeters)));
            XYZ p2 = p1 + mainDirection * runLength;
            XYZ p3 = preWallPoint;
            XYZ p4 = wallFacePoint - wallNormalToConnector * wallPenetration;

            List<XYZ> points = new List<XYZ> { p0, p1, p2, p3, p4 };
            DiagnosticRecorder.AppendDebug(
                "[PipeWork] DemoRoute65Fixed Role=" + role.ToString() +
                ", P0=" + FormatPointMm(p0) +
                ", P1=" + FormatPointMm(p1) +
                ", P2=" + FormatPointMm(p2) +
                ", P3=" + FormatPointMm(p3) +
                ", P4=" + FormatPointMm(p4));
            return points;
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

            return name + " (" + wall.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static Connector FindPipeConnector(FamilyInstance equipment, PipeConnectorRole role)
        {
            List<Connector> connectors = GetEquipmentPipeConnectors(equipment)
                .OrderBy(x => x.Origin != null ? x.Origin.Z : 0.0)
                .ToList();

            if (role == PipeConnectorRole.Chws)
            {
                return connectors.FirstOrDefault(IsChwsConnector);
            }

            if (role == PipeConnectorRole.Chwr)
            {
                return connectors.FirstOrDefault(IsChwrConnector);
            }

            return connectors.FirstOrDefault(IsChwsConnector) ?? connectors.FirstOrDefault(IsChwrConnector) ?? connectors.FirstOrDefault();
        }

        private static bool IsChwsConnector(Connector connector)
        {
            string classification = ReadConnectorClassificationText(connector).ToLowerInvariant();
            return (classification.Contains("supply") || classification.Contains("chws")) && !classification.Contains("return") && !classification.Contains("chwr");
        }

        private static bool IsChwrConnector(Connector connector)
        {
            string classification = ReadConnectorClassificationText(connector).ToLowerInvariant();
            return classification.Contains("return") || classification.Contains("chwr");
        }

        private static IEnumerable<Connector> GetEquipmentPipeConnectors(FamilyInstance equipment)
        {
            ConnectorSet connectorSet = equipment?.MEPModel?.ConnectorManager?.Connectors;
            if (connectorSet == null)
            {
                return Enumerable.Empty<Connector>();
            }

            return connectorSet
                .Cast<Connector>()
                .Where(x => x != null && x.Domain == Domain.DomainPiping)
                .Where(x => x.ConnectorType == ConnectorType.End)
                .ToList();
        }

        private static string ReadConnectorClassificationText(Connector connector)
        {
            if (connector == null)
            {
                return string.Empty;
            }

            List<string> values = new List<string>();
            AddReflectionPropertyValue(values, connector, "PipeSystemType");
            AddReflectionPropertyValue(values, connector, "AssignedPipeSystemType");
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
                    AddReflectionPropertyValue(values, info, "PipeSystemType");
                    AddReflectionPropertyValue(values, info, "SystemClassification");
                    AddReflectionPropertyValue(values, info, "SystemType");
                }
            }
            catch
            {
            }

            string joined = string.Join(" | ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            DiagnosticRecorder.AppendDebug(
                "[PipeWork] ConnectorClassification Origin=" + FormatPointMm(connector.Origin) +
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

        private static PipeType ResolvePipeType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .FirstOrDefault();
        }

        private static PipingSystemType ResolvePipingSystemType(Document doc, PipeConnectorRole role)
        {
            IEnumerable<PipingSystemType> types = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>();

            if (role == PipeConnectorRole.Chws)
            {
                PipingSystemType preferred = types.FirstOrDefault(x => IsChwsSystemName(x != null ? x.Name : string.Empty));
                if (preferred != null)
                {
                    return preferred;
                }
            }

            if (role == PipeConnectorRole.Chwr)
            {
                PipingSystemType preferred = types.FirstOrDefault(x => IsChwrSystemName(x != null ? x.Name : string.Empty));
                if (preferred != null)
                {
                    return preferred;
                }
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .OrderByDescending(x => IsPreferredSystemName(x != null ? x.Name : string.Empty))
                .ThenBy(x => x != null ? x.Name : string.Empty)
                .FirstOrDefault();
        }

        private static bool IsChwsSystemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string normalized = name.ToLowerInvariant();
            return normalized.Contains("chws") || (normalized.Contains("hydronic") && normalized.Contains("supply")) || normalized.Contains("supply");
        }

        private static bool IsChwrSystemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string normalized = name.ToLowerInvariant();
            return normalized.Contains("chwr") || (normalized.Contains("hydronic") && normalized.Contains("return")) || normalized.Contains("return");
        }

        private static int IsPreferredSystemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return 0;
            }

            string normalized = name.ToLowerInvariant();
            return normalized.Contains("hydronic") || normalized.Contains("chw") || normalized.Contains("water") ? 1 : 0;
        }

        private static ElementId ResolveLevelId(Document doc, FamilyInstance equipment, Element wall)
        {
            if (equipment != null && equipment.LevelId != null && equipment.LevelId != ElementId.InvalidElementId)
            {
                return equipment.LevelId;
            }

            if (wall != null && wall.LevelId != null && wall.LevelId != ElementId.InvalidElementId)
            {
                return wall.LevelId;
            }

            Level viewLevel = doc.ActiveView != null ? doc.ActiveView.GenLevel : null;
            if (viewLevel != null)
            {
                return viewLevel.Id;
            }

            Level firstLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
            return firstLevel != null ? firstLevel.Id : ElementId.InvalidElementId;
        }

        private static XYZ ProjectPointToWallFace(Wall wall, XYZ point)
        {
            LocationCurve locationCurve = wall != null ? wall.Location as LocationCurve : null;
            Curve curve = locationCurve != null ? locationCurve.Curve : null;
            if (curve == null || point == null)
            {
                return point;
            }

            IntersectionResult projection = curve.Project(point);
            if (projection == null || projection.XYZPoint == null)
            {
                return point;
            }

            XYZ projected = projection.XYZPoint;
            return new XYZ(projected.X, projected.Y, point.Z);
        }

        private static XYZ ResolveWallNormalTowardPoint(Wall wall, XYZ referencePoint)
        {
            LocationCurve locationCurve = wall != null ? wall.Location as LocationCurve : null;
            Curve curve = locationCurve != null ? locationCurve.Curve : null;
            if (curve == null || referencePoint == null)
            {
                return null;
            }

            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ tangent = FlattenAndNormalize(end - start);
            if (tangent == null)
            {
                return null;
            }

            XYZ normalA = new XYZ(-tangent.Y, tangent.X, 0.0).Normalize();
            XYZ normalB = normalA.Negate();
            XYZ wallPoint = ProjectPointToWallFace(wall, referencePoint) ?? start;
            XYZ toReference = FlattenAndNormalize(referencePoint - wallPoint);
            if (toReference == null)
            {
                return normalA;
            }

            return normalA.DotProduct(toReference) >= normalB.DotProduct(toReference) ? normalA : normalB;
        }

        private static XYZ FlattenAndNormalize(XYZ vector)
        {
            if (vector == null)
            {
                return null;
            }

            XYZ flattened = new XYZ(vector.X, vector.Y, 0.0);
            if (flattened.GetLength() < 1e-9)
            {
                return null;
            }

            return flattened.Normalize();
        }

        private static double ResolvePipeDiameterFeet(Connector connector, string uiDiameterText, PipeWorkOptions options)
        {
            double? parsedDiameterFeet = ParseDiameterFeet(uiDiameterText);
            if (parsedDiameterFeet.HasValue)
            {
                DiagnosticRecorder.AppendDebug(
                    "[PipeWork] FinalPipeDiameter Source=UiSize, DiameterMm=" +
                    FormatNumber(UnitUtils.ConvertFromInternalUnits(parsedDiameterFeet.Value, UnitTypeId.Millimeters)) +
                    ", IgnoredConnectorDiameterMm=" + FormatNumber(ReadConnectorDiameterMm(connector)));

                return parsedDiameterFeet.Value;
            }

            DiagnosticRecorder.AppendDebug(
                "[PipeWork] FinalPipeDiameter Source=Fallback65, DiameterMm=65" +
                ", IgnoredConnectorDiameterMm=" + FormatNumber(ReadConnectorDiameterMm(connector)) +
                ", IgnoredUiSize=" + (uiDiameterText ?? string.Empty));

            return UnitUtils.ConvertToInternalUnits(65.0, UnitTypeId.Millimeters);
        }

        private static double ReadConnectorDiameterMm(Connector connector)
        {
            if (connector == null)
            {
                return 0.0;
            }

            try
            {
                double connectorDiameter = connector.Radius * 2.0;
                if (connectorDiameter > 0.0)
                {
                    return UnitUtils.ConvertFromInternalUnits(connectorDiameter, UnitTypeId.Millimeters);
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static double? ParseDiameterFeet(string diameterText)
        {
            if (string.IsNullOrWhiteSpace(diameterText))
            {
                return null;
            }

            Match match = Regex.Match(diameterText, @"[-+]?\d+(\.\d+)?");
            if (!match.Success)
            {
                return null;
            }

            if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double diameterMm) &&
                !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out diameterMm))
            {
                return null;
            }

            if (diameterMm <= 0.0)
            {
                return null;
            }

            return UnitUtils.ConvertToInternalUnits(diameterMm, UnitTypeId.Millimeters);
        }

        private static void SetPipeDiameter(Pipe pipe, double diameterFeet)
        {
            if (pipe == null || diameterFeet <= 0.0)
            {
                return;
            }

            Parameter diameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diameter != null && !diameter.IsReadOnly)
            {
                diameter.Set(diameterFeet);
            }
        }

        private static void TryCreateDemoPipeValveMarker(
            Document doc,
            PipingSystemType systemType,
            PipeType pipeType,
            ElementId levelId,
            IList<XYZ> routePoints,
            PipeConnectorRole role,
            PipeWorkOptions options,
            IList<ElementId> createdElementIds)
        {
            if (doc == null || systemType == null || pipeType == null || routePoints == null || routePoints.Count < 4 || createdElementIds == null)
            {
                return;
            }

            options = options ?? new PipeWorkOptions();

            try
            {
                // Try a real pipe accessory first. If the family/type is not loaded or not placeable,
                // create a small side branch pipe as a robust DEMO valve marker.
                if (TryPlacePipeAccessoryValve(doc, levelId, routePoints, createdElementIds))
                {
                    return;
                }

                CreatePipeValveBranchMarker(doc, systemType, pipeType, levelId, routePoints, role, options, createdElementIds);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PipeWork] Demo valve marker failed. " + ex.Message);
            }
        }

        private static bool TryPlacePipeAccessoryValve(Document doc, ElementId levelId, IList<XYZ> routePoints, IList<ElementId> createdElementIds)
        {
            FamilySymbol symbol = FindPipeAccessoryValveSymbol(doc);
            if (symbol == null || routePoints == null || routePoints.Count < 4)
            {
                return false;
            }

            Level level = doc.GetElement(levelId) as Level;
            XYZ location = GetMidPoint(routePoints[2], routePoints[3]);
            if (location == null)
            {
                return false;
            }

            try
            {
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }

                FamilyInstance instance = null;
                if (level != null)
                {
                    instance = doc.Create.NewFamilyInstance(location, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                }
                else
                {
                    instance = doc.Create.NewFamilyInstance(location, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                }

                if (instance == null)
                {
                    return false;
                }

                RotateElementToRoute(doc, instance.Id, location, routePoints[3] - routePoints[2]);
                createdElementIds.Add(instance.Id);
                DiagnosticRecorder.AppendDebug("[PipeWork] Demo pipe accessory valve placed. Symbol=" + symbol.FamilyName + " : " + symbol.Name);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PipeWork] Pipe accessory valve placement failed; skipping demo valve placement. " + ex.Message);
                return false;
            }
        }

        private static FamilySymbol FindPipeAccessoryValveSymbol(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            FamilySymbol loadedSymbol = FindLoadedPipeAccessoryValveSymbol(doc);
            if (loadedSymbol != null)
            {
                return loadedSymbol;
            }

            string familyPath = FindLocalPipeAccessoryFamilyPath();
            if (string.IsNullOrWhiteSpace(familyPath) || !File.Exists(familyPath))
            {
                DiagnosticRecorder.AppendDebug(
                    "[PipeWork] No Pipe Accessory valve symbol is loaded, and no local valve .rfa was found under PipeAccessoryFamilyType.");
                return null;
            }

            try
            {
                Family family;
                bool loaded = doc.LoadFamily(familyPath, out family);
                DiagnosticRecorder.AppendDebug(
                    "[PipeWork] Auto-load Pipe Accessory family from " + familyPath +
                    ", Loaded=" + (loaded ? "true" : "false") +
                    ", Family=" + (family != null ? family.Name : "null"));

                FamilySymbol symbolFromFamily = GetBestValveSymbolFromFamily(doc, family);
                if (symbolFromFamily != null)
                {
                    return symbolFromFamily;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PipeWork] Auto-load Pipe Accessory valve family failed. Path=" + familyPath + ", Error=" + ex.Message);
            }

            return FindLoadedPipeAccessoryValveSymbol(doc);
        }

        private static FamilySymbol FindLoadedPipeAccessoryValveSymbol(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PipeAccessory)
                .Cast<FamilySymbol>()
                .Where(x => x != null)
                .ToList();

            return symbols
                .OrderByDescending(GetValveSymbolScore)
                .ThenBy(x => x.FamilyName ?? string.Empty)
                .ThenBy(x => x.Name ?? string.Empty)
                .FirstOrDefault();
        }

        private static FamilySymbol GetBestValveSymbolFromFamily(Document doc, Family family)
        {
            if (doc == null || family == null)
            {
                return null;
            }

            List<FamilySymbol> symbols = new List<FamilySymbol>();
            try
            {
                foreach (ElementId id in family.GetFamilySymbolIds())
                {
                    FamilySymbol symbol = doc.GetElement(id) as FamilySymbol;
                    if (symbol != null)
                    {
                        symbols.Add(symbol);
                    }
                }
            }
            catch
            {
            }

            return symbols
                .Where(x => x != null && x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .OrderByDescending(GetValveSymbolScore)
                .ThenBy(x => x.Name ?? string.Empty)
                .FirstOrDefault();
        }

        private static int GetValveSymbolScore(FamilySymbol symbol)
        {
            if (symbol == null)
            {
                return 0;
            }

            string familyName = symbol.FamilyName ?? string.Empty;
            string typeName = symbol.Name ?? string.Empty;
            int score = 0;

            if (ContainsIgnoreCase(familyName, "globe") || ContainsIgnoreCase(typeName, "globe")) score += 100;
            if (ContainsIgnoreCase(familyName, "valve") || ContainsIgnoreCase(typeName, "valve")) score += 80;
            if (ContainsIgnoreCase(familyName, "gate") || ContainsIgnoreCase(typeName, "gate")) score += 50;
            if (ContainsIgnoreCase(familyName, "ball") || ContainsIgnoreCase(typeName, "ball")) score += 40;
            if (ContainsIgnoreCase(familyName, "check") || ContainsIgnoreCase(typeName, "check")) score += 30;
            if (ContainsIgnoreCase(typeName, "2\"") || ContainsIgnoreCase(typeName, "2 inch") || ContainsIgnoreCase(typeName, "2in")) score += 20;
            if (ContainsIgnoreCase(typeName, "2.5") || ContainsIgnoreCase(typeName, "65")) score += 25;
            if (ContainsIgnoreCase(typeName, "flanged")) score += 5;

            return score;
        }

        private static List<BuiltInPipePortSnapshot> CollectBuiltInPipePortSnapshots(
            Document doc,
            IEnumerable<ElementId> elementIds)
        {
            List<BuiltInPipePortSnapshot> result = new List<BuiltInPipePortSnapshot>();
            if (doc == null || elementIds == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ElementId id in elementIds)
            {
                Element element = id != null ? doc.GetElement(id) : null;
                if (element == null || !element.IsValidObject)
                {
                    continue;
                }

                foreach (Connector connector in GetElementPipingEndConnectors(element))
                {
                    if (connector == null)
                    {
                        continue;
                    }

                    XYZ origin = null;
                    try
                    {
                        origin = connector.Origin;
                    }
                    catch
                    {
                        origin = null;
                    }

                    if (origin == null)
                    {
                        continue;
                    }

                    XYZ direction = null;
                    TryGetConnectorDirection(connector, out direction);

                    string key =
                        element.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(origin.X, 6).ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(origin.Y, 6).ToString(CultureInfo.InvariantCulture) + "|" +
                        Math.Round(origin.Z, 6).ToString(CultureInfo.InvariantCulture);

                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    string ownerName = element.Name ?? string.Empty;
                    string classification = ReadConnectorClassificationText(connector);
                    PipeConnectorRole role = ResolveBuiltInPipePortRole(
                        doc,
                        element,
                        connector,
                        classification,
                        out string insulationType,
                        out string roleEvidence);
                    bool isOpen = IsConnectorAvailableForConnection(connector);
                    bool isVertical = direction != null && Math.Abs(direction.Z) >= 0.85;
                    bool isFlange =
                        ownerName.IndexOf("flange", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ReadFamilyName(element).IndexOf("flange", StringComparison.OrdinalIgnoreCase) >= 0;

                    BuiltInPipePortSnapshot snapshot = new BuiltInPipePortSnapshot
                    {
                        OwnerId = element.Id,
                        OwnerName = string.IsNullOrWhiteSpace(ownerName) ? ReadFamilyName(element) : ownerName,
                        Origin = origin,
                        Direction = direction,
                        Classification = classification,
                        InsulationType = insulationType,
                        Role = role,
                        RoleEvidence = roleEvidence,
                        IsOpen = isOpen,
                        IsVertical = isVertical,
                        IsFlange = isFlange
                    };
                    result.Add(snapshot);

                    DiagnosticRecorder.AppendDebug(
                        "[BuiltInPipeAssembly.VisualMidpoint] TemplatePort " +
                        DescribeBuiltInPipePort(snapshot));
                }
            }

            return result;
        }

        private static PipeConnectorRole ResolveBuiltInPipePortRole(
            Document doc,
            Element owner,
            Connector connector,
            string classification,
            out string insulationType,
            out string roleEvidence)
        {
            insulationType = ReadElementInsulationTypeText(doc, owner);

            // The standard template can contain pipe/fitting type names whose text does not
            // reliably describe the actual CHWS/CHWR branch. Revit's visible "Insulation Type"
            // is therefore treated as the primary source of truth for the template side.
            PipeConnectorRole role = ResolvePipeConnectorRoleFromText(insulationType);
            if (role != PipeConnectorRole.Any)
            {
                roleEvidence = "InsulationType=" + insulationType;
                return role;
            }

            // A flange/open fitting may expose the insulation through the immediately connected
            // pipe instead of on the fitting itself. Inspect one connector hop before falling back
            // to MEP system classification.
            List<string> connectedInsulation = new List<string>();
            if (connector != null)
            {
                try
                {
                    ConnectorSet refs = connector.AllRefs;
                    if (refs != null)
                    {
                        foreach (Connector connected in refs.Cast<Connector>())
                        {
                            Element connectedOwner = null;
                            try
                            {
                                connectedOwner = connected?.Owner;
                            }
                            catch
                            {
                                connectedOwner = null;
                            }

                            if (connectedOwner == null || owner == null || connectedOwner.Id == owner.Id)
                            {
                                continue;
                            }

                            string text = ReadElementInsulationTypeText(doc, connectedOwner);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                connectedInsulation.Add(text);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            string connectedInsulationText = string.Join(
                " | ",
                connectedInsulation
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            role = ResolvePipeConnectorRoleFromText(connectedInsulationText);
            if (role != PipeConnectorRole.Any)
            {
                insulationType = connectedInsulationText;
                roleEvidence = "ConnectedInsulationType=" + connectedInsulationText;
                return role;
            }

            role = ResolvePipeConnectorRoleFromText(classification);
            if (role != PipeConnectorRole.Any)
            {
                roleEvidence = "ConnectorClassification=" + (classification ?? string.Empty);
                return role;
            }

            roleEvidence = "Unresolved";
            return PipeConnectorRole.Any;
        }

        private static PipeConnectorRole ResolvePipeConnectorRoleFromText(string text)
        {
            string value = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
            {
                return PipeConnectorRole.Any;
            }

            // CHWR/return wins when both generic words happen to be present in a concatenated
            // diagnostic string. This mirrors IsChwrConnector/IsChwsConnector on the AHU side.
            if (value.Contains("chwr") || value.Contains("hydronic return") || value.Contains("return"))
            {
                return PipeConnectorRole.Chwr;
            }

            if (value.Contains("chws") || value.Contains("hydronic supply") || value.Contains("supply"))
            {
                return PipeConnectorRole.Chws;
            }

            return PipeConnectorRole.Any;
        }

        private static string ReadElementInsulationTypeText(Document doc, Element element)
        {
            if (doc == null || element == null)
            {
                return string.Empty;
            }

            List<string> values = new List<string>();

            AddElementParameterText(values, doc, element, "Insulation Type");
            AddElementParameterText(values, doc, element, "Insulation Type Name");

            // Pipe insulation is commonly stored as a dependent element. This also handles the
            // Revit property-panel case where a fitting visually reports CHWS/CHWR insulation even
            // when LookupParameter on the host fitting is empty.
            try
            {
                ICollection<ElementId> dependentIds = element.GetDependentElements(null);
                if (dependentIds != null)
                {
                    foreach (ElementId dependentId in dependentIds)
                    {
                        Element dependent = doc.GetElement(dependentId);
                        if (!IsPipeInsulationElement(dependent))
                        {
                            continue;
                        }

                        AddElementIdentityText(values, doc, dependent);
                    }
                }
            }
            catch
            {
            }

            // Revit exposes insulation host relationships through InsulationLiningBase. Invoke the
            // API by reflection so this code remains tolerant of small API signature differences.
            try
            {
                MethodInfo method = typeof(InsulationLiningBase).GetMethod(
                    "GetInsulationIds",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Document), typeof(ElementId) },
                    null);
                object raw = method != null
                    ? method.Invoke(null, new object[] { doc, element.Id })
                    : null;
                IEnumerable<ElementId> insulationIds = raw as IEnumerable<ElementId>;
                if (insulationIds != null)
                {
                    foreach (ElementId insulationId in insulationIds)
                    {
                        Element insulation = doc.GetElement(insulationId);
                        if (insulation != null)
                        {
                            AddElementIdentityText(values, doc, insulation);
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Join(
                " | ",
                values
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static void AddElementParameterText(
            List<string> values,
            Document doc,
            Element element,
            string parameterName)
        {
            if (values == null || doc == null || element == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            try
            {
                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter == null)
                {
                    return;
                }

                string text = null;
                try
                {
                    text = parameter.AsValueString();
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(text) && parameter.StorageType == StorageType.String)
                {
                    try
                    {
                        text = parameter.AsString();
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }

                if (parameter.StorageType == StorageType.ElementId)
                {
                    try
                    {
                        ElementId typeId = parameter.AsElementId();
                        Element typeElement = typeId != null && typeId != ElementId.InvalidElementId
                            ? doc.GetElement(typeId)
                            : null;
                        if (typeElement != null)
                        {
                            values.Add(typeElement.Name ?? string.Empty);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsPipeInsulationElement(Element element)
        {
            string categoryName = element?.Category?.Name ?? string.Empty;
            return categoryName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   categoryName.IndexOf("Insulation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddElementIdentityText(List<string> values, Document doc, Element element)
        {
            if (values == null || doc == null || element == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                values.Add(element.Name);
            }

            try
            {
                ElementId typeId = element.GetTypeId();
                Element typeElement = typeId != null && typeId != ElementId.InvalidElementId
                    ? doc.GetElement(typeId)
                    : null;
                if (typeElement != null && !string.IsNullOrWhiteSpace(typeElement.Name))
                {
                    values.Add(typeElement.Name);
                }
            }
            catch
            {
            }
        }

        private static BuiltInPipePairSelection SelectBuiltInPipeVisualMidpointPair(
            IEnumerable<BuiltInPipePortSnapshot> sourcePorts,
            IEnumerable<Connector> targetConnectors,
            XYZ sourceCenter,
            XYZ equipmentCenter)
        {
            if (sourcePorts == null || targetConnectors == null)
            {
                return null;
            }

            List<BuiltInPipePortSnapshot> sources = sourcePorts
                .Where(x => x != null && x.Origin != null)
                .ToList();
            List<Connector> targets = targetConnectors
                .Where(x => x != null && TryGetConnectorOrigin(x, out XYZ _))
                .ToList();

            if (sources.Count < 2 || targets.Count < 2)
            {
                return null;
            }

            Connector targetChws = targets.FirstOrDefault(IsChwsConnector);
            Connector targetChwr = targets.FirstOrDefault(IsChwrConnector);
            bool targetRolesResolved =
                targetChws != null &&
                targetChwr != null &&
                !ReferenceEquals(targetChws, targetChwr);

            Connector targetA = targetChws;
            Connector targetB = targetChwr;

            if (targetA == null || targetB == null || ReferenceEquals(targetA, targetB))
            {
                // Fallback: use the two water connectors with the largest separation.
                double bestTargetDistance = -1.0;
                for (int i = 0; i < targets.Count - 1; i++)
                {
                    for (int j = i + 1; j < targets.Count; j++)
                    {
                        if (!TryGetConnectorOrigin(targets[i], out XYZ ta) ||
                            !TryGetConnectorOrigin(targets[j], out XYZ tb))
                        {
                            continue;
                        }

                        double distance = ta.DistanceTo(tb);
                        if (distance > bestTargetDistance)
                        {
                            bestTargetDistance = distance;
                            targetA = targets[i];
                            targetB = targets[j];
                        }
                    }
                }
            }

            if (targetA == null || targetB == null ||
                !TryGetConnectorOrigin(targetA, out XYZ targetAOrigin) ||
                !TryGetConnectorOrigin(targetB, out XYZ targetBOrigin))
            {
                return null;
            }

            XYZ targetVector = targetBOrigin - targetAOrigin;
            double targetLength = targetVector.GetLength();
            double targetPlanLength = Math.Sqrt(
                (targetVector.X * targetVector.X) +
                (targetVector.Y * targetVector.Y));
            if (targetLength <= 1.0e-9 || targetPlanLength <= 1.0e-9)
            {
                return null;
            }

            XYZ targetMidpoint = (targetAOrigin + targetBOrigin) * 0.5;
            double targetSpacingMm = UnitUtils.ConvertFromInternalUnits(
                targetLength,
                UnitTypeId.Millimeters);

            bool hasSourceChws = sources.Any(x => x.Role == PipeConnectorRole.Chws);
            bool hasSourceChwr = sources.Any(x => x.Role == PipeConnectorRole.Chwr);
            bool enforceRoleMatching = targetRolesResolved && hasSourceChws && hasSourceChwr;

            DiagnosticRecorder.AppendDebug(
                "[BuiltInPipeAssembly.VisualMidpoint] Role matching. TargetRolesResolved=" +
                targetRolesResolved.ToString() +
                ", SourceCHWS=" + hasSourceChws.ToString() +
                ", SourceCHWR=" + hasSourceChwr.ToString() +
                ", Enforce=" + enforceRoleMatching.ToString());

            BuiltInPipePairSelection best = null;
            for (int i = 0; i < sources.Count - 1; i++)
            {
                for (int j = i + 1; j < sources.Count; j++)
                {
                    BuiltInPipePortSnapshot sourceA = sources[i];
                    BuiltInPipePortSnapshot sourceB = sources[j];
                    XYZ sourceVector = sourceB.Origin - sourceA.Origin;
                    double sourceLength = sourceVector.GetLength();
                    double sourcePlanLength = Math.Sqrt(
                        (sourceVector.X * sourceVector.X) +
                        (sourceVector.Y * sourceVector.Y));
                    if (sourceLength <= 1.0e-9 || sourcePlanLength <= 1.0e-9)
                    {
                        continue;
                    }

                    double sourceSpacingMm = UnitUtils.ConvertFromInternalUnits(
                        sourceLength,
                        UnitTypeId.Millimeters);

                    // Ignore duplicate/internal connector snapshots that are effectively at the
                    // same physical port. The current AHU-facing pair is roughly 742 mm apart.
                    if (sourceSpacingMm < 50.0)
                    {
                        continue;
                    }

                    XYZ sourceMidpoint = (sourceA.Origin + sourceB.Origin) * 0.5;
                    XYZ translation = targetMidpoint - sourceMidpoint;
                    double spacingDifferenceMm = Math.Abs(sourceSpacingMm - targetSpacingMm);
                    double axisAbsDotBeforeRotation = Math.Abs(
                        ((sourceVector.X * targetVector.X) +
                         (sourceVector.Y * targetVector.Y)) /
                        (sourcePlanLength * targetPlanLength));
                    axisAbsDotBeforeRotation = Math.Min(1.0, axisAbsDotBeforeRotation);

                    bool sourcePairHasRoles =
                        (sourceA.Role == PipeConnectorRole.Chws && sourceB.Role == PipeConnectorRole.Chwr) ||
                        (sourceA.Role == PipeConnectorRole.Chwr && sourceB.Role == PipeConnectorRole.Chws);

                    if (enforceRoleMatching && !sourcePairHasRoles)
                    {
                        continue;
                    }

                    // When both template branches can be identified from their actual insulation
                    // types, lock CHWS->Hydronic Supply and CHWR->Hydronic Return. This avoids the
                    // previous geometry-only 180-degree swap that could visually cross the two
                    // services even though the connector spacing was correct. If the template
                    // cannot be classified, retain the old two-order geometry fallback.
                    List<bool> targetOrderOptions = new List<bool>();
                    if (enforceRoleMatching && sourcePairHasRoles)
                    {
                        targetOrderOptions.Add(sourceA.Role == PipeConnectorRole.Chwr);
                    }
                    else
                    {
                        targetOrderOptions.Add(false);
                        targetOrderOptions.Add(true);
                    }

                    foreach (bool swapTargets in targetOrderOptions)
                    {
                        Connector assignedTargetA = swapTargets ? targetB : targetA;
                        Connector assignedTargetB = swapTargets ? targetA : targetB;
                        XYZ assignedTargetAOrigin = swapTargets ? targetBOrigin : targetAOrigin;
                        XYZ assignedTargetBOrigin = swapTargets ? targetAOrigin : targetBOrigin;
                        XYZ assignedTargetVector = assignedTargetBOrigin - assignedTargetAOrigin;

                        if (!TryCalculateSignedPlanRotation(
                                sourceVector,
                                assignedTargetVector,
                                out double rotationRadians))
                        {
                            continue;
                        }

                        XYZ translatedA = sourceA.Origin + translation;
                        XYZ translatedB = sourceB.Origin + translation;
                        XYZ movedA = RotatePointAroundZ(
                            translatedA,
                            targetMidpoint,
                            rotationRadians);
                        XYZ movedB = RotatePointAroundZ(
                            translatedB,
                            targetMidpoint,
                            rotationRadians);

                        double errorA = UnitUtils.ConvertFromInternalUnits(
                            movedA.DistanceTo(assignedTargetAOrigin),
                            UnitTypeId.Millimeters);
                        double errorB = UnitUtils.ConvertFromInternalUnits(
                            movedB.DistanceTo(assignedTargetBOrigin),
                            UnitTypeId.Millimeters);

                        double transformedCenterDistanceMm = 0.0;
                        if (sourceCenter != null && equipmentCenter != null)
                        {
                            XYZ transformedSourceCenter = RotatePointAroundZ(
                                sourceCenter + translation,
                                targetMidpoint,
                                rotationRadians);
                            transformedCenterDistanceMm = UnitUtils.ConvertFromInternalUnits(
                                transformedSourceCenter.DistanceTo(equipmentCenter),
                                UnitTypeId.Millimeters);
                        }

                        double rotationDegrees = rotationRadians * (180.0 / Math.PI);

                        // Rotation is now allowed, but ONLY around Z. Therefore axis-angle mismatch
                        // is no longer an error. Pair selection is driven primarily by connector
                        // spacing and actual post-rotation endpoint error. A small center-distance
                        // term selects the correct +90/-90 (or 0/180) orientation for asymmetric
                        // assemblies without overpowering the connector geometry.
                        double score =
                            spacingDifferenceMm +
                            ((errorA + errorB) * 0.5) +
                            (transformedCenterDistanceMm * 0.02) +
                            (Math.Abs(rotationDegrees) * 0.02);

                        if (sourceA.IsFlange)
                        {
                            score -= 100.0;
                        }
                        if (sourceB.IsFlange)
                        {
                            score -= 100.0;
                        }

                        BuiltInPipePairSelection candidate = new BuiltInPipePairSelection
                        {
                            SourcePortA = sourceA,
                            SourcePortB = sourceB,
                            TargetConnectorA = assignedTargetA,
                            TargetConnectorB = assignedTargetB,
                            Translation = translation,
                            SourceSpacingMm = sourceSpacingMm,
                            TargetSpacingMm = targetSpacingMm,
                            SpacingDifferenceMm = spacingDifferenceMm,
                            AxisAbsDot = axisAbsDotBeforeRotation,
                            EndpointErrorAMm = errorA,
                            EndpointErrorBMm = errorB,
                            Score = score,
                            TargetOrderSwapped = swapTargets,
                            SourceMidpoint = sourceMidpoint,
                            TargetMidpoint = targetMidpoint,
                            RotationRadians = rotationRadians,
                            RotationDegrees = rotationDegrees,
                            TransformedSourceCenterDistanceMm = transformedCenterDistanceMm
                        };

                        if (best == null || candidate.Score < best.Score)
                        {
                            best = candidate;
                        }
                    }
                }
            }

            return best;
        }

        private static bool TryCalculateSignedPlanRotation(
            XYZ sourceVector,
            XYZ targetVector,
            out double rotationRadians)
        {
            rotationRadians = 0.0;
            if (sourceVector == null || targetVector == null)
            {
                return false;
            }

            double sourceLength = Math.Sqrt(
                (sourceVector.X * sourceVector.X) +
                (sourceVector.Y * sourceVector.Y));
            double targetLength = Math.Sqrt(
                (targetVector.X * targetVector.X) +
                (targetVector.Y * targetVector.Y));
            if (sourceLength <= 1.0e-9 || targetLength <= 1.0e-9)
            {
                return false;
            }

            double dot =
                (sourceVector.X * targetVector.X) +
                (sourceVector.Y * targetVector.Y);
            double crossZ =
                (sourceVector.X * targetVector.Y) -
                (sourceVector.Y * targetVector.X);

            rotationRadians = Math.Atan2(crossZ, dot);
            return !double.IsNaN(rotationRadians) && !double.IsInfinity(rotationRadians);
        }

        private static XYZ RotatePointAroundZ(
            XYZ point,
            XYZ center,
            double rotationRadians)
        {
            if (point == null)
            {
                return null;
            }

            if (center == null || Math.Abs(rotationRadians) <= 1.0e-12)
            {
                return point;
            }

            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            double cos = Math.Cos(rotationRadians);
            double sin = Math.Sin(rotationRadians);

            return new XYZ(
                center.X + (dx * cos) - (dy * sin),
                center.Y + (dx * sin) + (dy * cos),
                point.Z);
        }

        private static bool TryGetConnectorOrigin(Connector connector, out XYZ origin)
        {
            origin = null;
            if (connector == null)
            {
                return false;
            }

            try
            {
                origin = connector.Origin;
                return origin != null;
            }
            catch
            {
                origin = null;
                return false;
            }
        }

        private static BuiltInPipeAnchorSelection SelectBuiltInPipeSingleAnchor(
            XYZ sourceCenter,
            XYZ equipmentCenter,
            IEnumerable<BuiltInPipePortSnapshot> sourcePorts,
            IEnumerable<Connector> targetConnectors)
        {
            if (sourceCenter == null ||
                equipmentCenter == null ||
                sourcePorts == null ||
                targetConnectors == null)
            {
                return null;
            }

            BuiltInPipeAnchorSelection best = null;
            foreach (BuiltInPipePortSnapshot source in sourcePorts)
            {
                if (source?.Origin == null)
                {
                    continue;
                }

                foreach (Connector target in targetConnectors)
                {
                    XYZ targetOrigin = null;
                    try
                    {
                        targetOrigin = target?.Origin;
                    }
                    catch
                    {
                        targetOrigin = null;
                    }

                    if (targetOrigin == null)
                    {
                        continue;
                    }

                    XYZ translation = targetOrigin - source.Origin;
                    XYZ movedCenter = sourceCenter + translation;
                    double centerOffsetMm = UnitUtils.ConvertFromInternalUnits(
                        movedCenter.DistanceTo(equipmentCenter),
                        UnitTypeId.Millimeters);

                    double directionDot = GetConnectorDirectionDot(source.Direction, target);
                    double score = centerOffsetMm;

                    // Prefer the actual flange face visible in the user's figures.
                    if (source.IsFlange)
                    {
                        score -= 500.0;
                    }

                    // Prefer a connector pair that already faces each other without rotation.
                    // This is a preference only; the assembly is still placed even when it cannot
                    // be logically ConnectTo'd yet.
                    if (!double.IsNaN(directionDot))
                    {
                        if (directionDot <= -0.80)
                        {
                            score -= 800.0;
                        }
                        else if (directionDot > -0.50)
                        {
                            score += 800.0;
                        }
                    }

                    BuiltInPipeAnchorSelection candidate = new BuiltInPipeAnchorSelection
                    {
                        SourcePort = source,
                        TargetConnector = target,
                        Translation = translation,
                        ScoreMm = score,
                        DirectionDot = directionDot
                    };

                    if (best == null || candidate.ScoreMm < best.ScoreMm)
                    {
                        best = candidate;
                    }
                }
            }

            return best;
        }

        private static IEnumerable<Connector> GetElementPipingEndConnectors(Element element)
        {
            if (element == null)
            {
                return Enumerable.Empty<Connector>();
            }

            ConnectorSet connectors = null;
            try
            {
                MEPCurve curve = element as MEPCurve;
                if (curve != null)
                {
                    connectors = curve.ConnectorManager?.Connectors;
                }
                else
                {
                    FamilyInstance instance = element as FamilyInstance;
                    connectors = instance?.MEPModel?.ConnectorManager?.Connectors;
                }
            }
            catch
            {
                connectors = null;
            }

            if (connectors == null)
            {
                return Enumerable.Empty<Connector>();
            }

            return connectors
                .Cast<Connector>()
                .Where(x => x != null)
                .Where(x =>
                {
                    try
                    {
                        return x.Domain == Domain.DomainPiping &&
                               x.ConnectorType == ConnectorType.End;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
        }

        private static Connector FindNearestEquipmentPipingConnector(
            FamilyInstance equipment,
            XYZ referenceOrigin)
        {
            if (equipment == null || referenceOrigin == null)
            {
                return null;
            }

            Connector best = null;
            double bestDistance = double.MaxValue;
            foreach (Connector connector in GetEquipmentPipeConnectors(equipment))
            {
                if (connector == null || !(IsChwsConnector(connector) || IsChwrConnector(connector)))
                {
                    continue;
                }

                XYZ origin = null;
                try
                {
                    origin = connector.Origin;
                }
                catch
                {
                    origin = null;
                }

                if (origin == null)
                {
                    continue;
                }

                double distance = origin.DistanceTo(referenceOrigin);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = connector;
                }
            }

            return best;
        }

        private static Connector FindNearestCopiedOpenPipingConnector(
            Document doc,
            IEnumerable<ElementId> copiedIds,
            XYZ targetOrigin,
            out double distanceMm)
        {
            distanceMm = double.MaxValue;
            if (doc == null || copiedIds == null || targetOrigin == null)
            {
                return null;
            }

            Connector best = null;
            double bestDistance = double.MaxValue;
            foreach (ElementId id in copiedIds)
            {
                Element element = id != null ? doc.GetElement(id) : null;
                if (element == null)
                {
                    continue;
                }

                foreach (Connector connector in GetElementPipingEndConnectors(element))
                {
                    if (connector == null || !IsConnectorAvailableForConnection(connector))
                    {
                        continue;
                    }

                    XYZ origin = null;
                    try
                    {
                        origin = connector.Origin;
                    }
                    catch
                    {
                        origin = null;
                    }

                    if (origin == null)
                    {
                        continue;
                    }

                    double distance = origin.DistanceTo(targetOrigin);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = connector;
                    }
                }
            }

            if (best != null)
            {
                distanceMm = UnitUtils.ConvertFromInternalUnits(
                    bestDistance,
                    UnitTypeId.Millimeters);
            }

            return best;
        }

        private static bool IsConnectorAvailableForConnection(Connector connector)
        {
            if (connector == null)
            {
                return false;
            }

            try
            {
                return !connector.IsConnected;
            }
            catch
            {
                // If a legacy connector does not expose IsConnected reliably, keep it as a
                // placement candidate. ConnectTo itself is isolated in a rollback-safe transaction.
                return true;
            }
        }

        private static bool TryGetConnectorDirection(Connector connector, out XYZ direction)
        {
            direction = null;
            if (connector == null)
            {
                return false;
            }

            try
            {
                Transform coordinateSystem = connector.CoordinateSystem;
                XYZ basisZ = coordinateSystem?.BasisZ;
                if (basisZ == null || basisZ.GetLength() <= 1.0e-9)
                {
                    return false;
                }

                direction = basisZ.Normalize();
                return true;
            }
            catch
            {
                direction = null;
                return false;
            }
        }

        private static double GetConnectorDirectionDot(Connector first, Connector second)
        {
            if (!TryGetConnectorDirection(first, out XYZ firstDirection) ||
                !TryGetConnectorDirection(second, out XYZ secondDirection))
            {
                return double.NaN;
            }

            return firstDirection.DotProduct(secondDirection);
        }

        private static double GetConnectorDirectionDot(XYZ firstDirection, Connector second)
        {
            if (firstDirection == null ||
                firstDirection.GetLength() <= 1.0e-9 ||
                !TryGetConnectorDirection(second, out XYZ secondDirection))
            {
                return double.NaN;
            }

            return firstDirection.Normalize().DotProduct(secondDirection);
        }

        private static string ReadFamilyName(Element element)
        {
            FamilyInstance instance = element as FamilyInstance;
            try
            {
                return instance?.Symbol?.Family?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string DescribeBuiltInPipePort(BuiltInPipePortSnapshot port)
        {
            if (port == null)
            {
                return "null";
            }

            return "Owner=" + (port.OwnerName ?? string.Empty) +
                   "#" + (port.OwnerId != null
                       ? port.OwnerId.IntegerValue.ToString(CultureInfo.InvariantCulture)
                       : "-1") +
                   ", Origin=" + FormatPointMm(port.Origin) +
                   ", Direction=" + FormatVector(port.Direction) +
                   ", Classification=" + (port.Classification ?? string.Empty) +
                   ", InsulationType=" + (port.InsulationType ?? string.Empty) +
                   ", Role=" + port.Role.ToString() +
                   ", RoleEvidence=" + (port.RoleEvidence ?? string.Empty) +
                   ", Open=" + port.IsOpen.ToString() +
                   ", Vertical=" + port.IsVertical.ToString() +
                   ", Flange=" + port.IsFlange.ToString();
        }

        private static string DescribeConnector(Connector connector)
        {
            if (connector == null)
            {
                return "null";
            }

            XYZ origin = null;
            try
            {
                origin = connector.Origin;
            }
            catch
            {
            }

            XYZ direction = null;
            TryGetConnectorDirection(connector, out direction);

            string ownerName = string.Empty;
            int ownerId = -1;
            try
            {
                ownerName = connector.Owner?.Name ?? string.Empty;
                ownerId = connector.Owner?.Id?.IntegerValue ?? -1;
            }
            catch
            {
            }

            return "Owner=" + ownerName +
                   "#" + ownerId.ToString(CultureInfo.InvariantCulture) +
                   ", Origin=" + FormatPointMm(origin) +
                   ", Direction=" + FormatVector(direction) +
                   ", Classification=" + ReadConnectorClassificationText(connector);
        }

        private static string FormatVector(XYZ vector)
        {
            if (vector == null)
            {
                return "null";
            }

            return "(" +
                   FormatNumber(vector.X) + ", " +
                   FormatNumber(vector.Y) + ", " +
                   FormatNumber(vector.Z) + ")";
        }

        private static string FindBuiltInPipeAssemblyPath()
        {
            const string templateFileName = "内置管道.rvt";
            foreach (string root in GetAddinSearchRoots())
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                try
                {
                    string candidate = Path.Combine(root, "RevitLinkInstance", templateFileName);
                    if (File.Exists(candidate))
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[BuiltInPipeAssembly.CenterOnly] Template resolved. Path=" + candidate);
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return string.Empty;
        }

        private static Document FindOpenDocumentByPath(Document hostDoc, string fullPath)
        {
            if (hostDoc == null || string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            string targetPath;
            try
            {
                targetPath = Path.GetFullPath(fullPath);
            }
            catch
            {
                targetPath = fullPath;
            }

            try
            {
                foreach (Document openDoc in hostDoc.Application.Documents)
                {
                    if (openDoc == null || !openDoc.IsValidObject || string.IsNullOrWhiteSpace(openDoc.PathName))
                    {
                        continue;
                    }

                    string openPath;
                    try
                    {
                        openPath = Path.GetFullPath(openDoc.PathName);
                    }
                    catch
                    {
                        openPath = openDoc.PathName;
                    }

                    if (string.Equals(openPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return openDoc;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static List<ElementId> CollectBuiltInPipeAssemblyElementIds(Document sourceDoc)
        {
            List<ElementId> ids = new List<ElementId>();
            if (sourceDoc == null)
            {
                return ids;
            }

            foreach (Element element in new FilteredElementCollector(sourceDoc).WhereElementIsNotElementType())
            {
                if (element == null || !element.IsValidObject || element.ViewSpecific || !IsBuiltInPipeAssemblyModelElement(element))
                {
                    continue;
                }

                ids.Add(element.Id);

                // Pipe insulation is often a dependent element. Keep it with the copied assembly
                // without relying on a version-specific BuiltInCategory enum name.
                try
                {
                    ICollection<ElementId> dependentIds = element.GetDependentElements(null);
                    if (dependentIds == null)
                    {
                        continue;
                    }

                    foreach (ElementId dependentId in dependentIds)
                    {
                        Element dependent = sourceDoc.GetElement(dependentId);
                        string categoryName = dependent?.Category?.Name ?? string.Empty;
                        if (dependent != null &&
                            !dependent.ViewSpecific &&
                            categoryName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            categoryName.IndexOf("Insulation", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            ids.Add(dependentId);
                        }
                    }
                }
                catch
                {
                }
            }

            return ids
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct(new ElementIdIntegerComparer())
                .ToList();
        }

        private static bool IsBuiltInPipeAssemblyModelElement(Element element)
        {
            if (element?.Category == null)
            {
                return false;
            }

            int categoryId = element.Category.Id.IntegerValue;
            if (categoryId == (int)BuiltInCategory.OST_PipeCurves ||
                categoryId == (int)BuiltInCategory.OST_PipeFitting ||
                categoryId == (int)BuiltInCategory.OST_PipeAccessory ||
                categoryId == (int)BuiltInCategory.OST_MechanicalEquipment ||
                categoryId == (int)BuiltInCategory.OST_GenericModel)
            {
                return true;
            }

            string categoryName = element.Category.Name ?? string.Empty;
            return categoryName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   categoryName.IndexOf("Insulation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static BoundingBoxXYZ GetCombinedBoundingBox(Document doc, IEnumerable<ElementId> elementIds)
        {
            if (doc == null || elementIds == null)
            {
                return null;
            }

            XYZ min = null;
            XYZ max = null;
            foreach (ElementId id in elementIds)
            {
                Element element = id != null ? doc.GetElement(id) : null;
                BoundingBoxXYZ box = element?.get_BoundingBox(null);
                if (box?.Min == null || box.Max == null)
                {
                    continue;
                }

                min = min == null
                    ? box.Min
                    : new XYZ(
                        Math.Min(min.X, box.Min.X),
                        Math.Min(min.Y, box.Min.Y),
                        Math.Min(min.Z, box.Min.Z));

                max = max == null
                    ? box.Max
                    : new XYZ(
                        Math.Max(max.X, box.Max.X),
                        Math.Max(max.Y, box.Max.Y),
                        Math.Max(max.Z, box.Max.Z));
            }

            if (min == null || max == null)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = min,
                Max = max
            };
        }

        private static bool TryGetBoundingBoxCenter(BoundingBoxXYZ box, out XYZ center)
        {
            center = null;
            if (box?.Min == null || box.Max == null)
            {
                return false;
            }

            center = new XYZ(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);
            return true;
        }

        private sealed class UseDestinationDuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        private sealed class ElementIdIntegerComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return x.IntegerValue == y.IntegerValue;
            }

            public int GetHashCode(ElementId obj)
            {
                return obj == null ? 0 : obj.IntegerValue.GetHashCode();
            }
        }

        private static string FindLocalPipeAccessoryFamilyPath()
        {
            IEnumerable<string> roots = GetAddinSearchRoots();
            List<string> candidates = new List<string>();

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                string folder = Path.Combine(root, "PipeAccessoryFamilyType");
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(folder, "*.rfa", SearchOption.TopDirectoryOnly));
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[PipeWork] Failed to scan local PipeAccessoryFamilyType folder. Folder=" + folder + ", Error=" + ex.Message);
                }
            }

            string selected = candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(GetValveFamilyFileScore)
                .ThenBy(x => Path.GetFileName(x) ?? string.Empty)
                .FirstOrDefault();

            DiagnosticRecorder.AppendDebug(
                "[PipeWork] Local Pipe Accessory family search. Candidates=" + candidates.Count.ToString(CultureInfo.InvariantCulture) +
                ", Selected=" + (selected ?? string.Empty));

            return selected;
        }

        private static IEnumerable<string> GetAddinSearchRoots()
        {
            List<string> roots = new List<string>();
            AddDirectoryCandidate(roots, GetAssemblyDirectory());
            AddDirectoryCandidate(roots, AppDomain.CurrentDomain.BaseDirectory);
            AddDirectoryCandidate(roots, Directory.GetCurrentDirectory());

            List<string> snapshot = roots.ToList();
            foreach (string root in snapshot)
            {
                try
                {
                    DirectoryInfo dir = new DirectoryInfo(root);
                    for (int i = 0; i < 4 && dir != null; i++)
                    {
                        AddDirectoryCandidate(roots, dir.FullName);
                        dir = dir.Parent;
                    }
                }
                catch
                {
                }
            }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string GetAssemblyDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    return Path.GetDirectoryName(location);
                }
            }
            catch
            {
            }

            try
            {
                string location = typeof(RoomPipeSystemService).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    return Path.GetDirectoryName(location);
                }
            }
            catch
            {
            }

            return null;
        }

        private static void AddDirectoryCandidate(List<string> roots, string path)
        {
            if (roots == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath) && !roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(fullPath);
                }
            }
            catch
            {
            }
        }

        private static int GetValveFamilyFileScore(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            int score = 0;
            if (ContainsIgnoreCase(name, "globe")) score += 100;
            if (ContainsIgnoreCase(name, "valve")) score += 80;
            if (ContainsIgnoreCase(name, "2-18")) score += 40;
            if (ContainsIgnoreCase(name, "0.375-2")) score += 25;
            if (ContainsIgnoreCase(name, "flanged")) score += 15;
            if (ContainsIgnoreCase(name, "thread")) score += 10;
            if (ContainsIgnoreCase(name, "ball")) score += 5;
            return score;
        }

        private static bool ContainsIgnoreCase(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(token) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CreatePipeValveBranchMarker(
            Document doc,
            PipingSystemType systemType,
            PipeType pipeType,
            ElementId levelId,
            IList<XYZ> routePoints,
            PipeConnectorRole role,
            PipeWorkOptions options,
            IList<ElementId> createdElementIds)
        {
            if (routePoints == null || routePoints.Count < 4)
            {
                return;
            }

            XYZ routeDirection = FlattenAndNormalize(routePoints[3] - routePoints[2]);
            if (routeDirection == null)
            {
                routeDirection = XYZ.BasisX;
            }

            XYZ sideDirection = FlattenAndNormalize(routeDirection.CrossProduct(XYZ.BasisZ)) ?? XYZ.BasisY;
            double branchLength = UnitUtils.ConvertToInternalUnits(Math.Max(options.ValveBranchLengthMm, 250.0), UnitTypeId.Millimeters);
            double branchDiameter = UnitUtils.ConvertToInternalUnits(Math.Max(options.ValveBranchDiameterMm, 50.0), UnitTypeId.Millimeters);

            XYZ center = GetMidPoint(routePoints[2], routePoints[3]);
            XYZ branchStart = center - sideDirection * branchLength * 0.5;
            XYZ branchEnd = center + sideDirection * branchLength * 0.5;

            Pipe branch = Pipe.Create(doc, systemType.Id, pipeType.Id, levelId, branchStart, branchEnd);
            if (branch != null)
            {
                SetPipeDiameter(branch, branchDiameter);
                createdElementIds.Add(branch.Id);
                DiagnosticRecorder.AppendDebug("[PipeWork] Demo branch valve marker created. Role=" + role.ToString());
            }
        }

        private static XYZ GetMidPoint(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static void RotateElementToRoute(Document doc, ElementId elementId, XYZ origin, XYZ direction)
        {
            XYZ flat = FlattenAndNormalize(direction);
            if (doc == null || elementId == null || elementId == ElementId.InvalidElementId || origin == null || flat == null)
            {
                return;
            }

            try
            {
                double angle = Math.Atan2(flat.Y, flat.X);
                Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, elementId, axis, angle);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PipeWork] Failed to rotate demo valve marker. " + ex.Message);
            }
        }

        private static void CreateDemoPipeVisualSolids(
            Document doc,
            IList<XYZ> routePoints,
            double diameterFeet,
            PipeConnectorRole role,
            IList<ElementId> createdElementIds,
            string label)
        {
            if (doc == null || routePoints == null || routePoints.Count < 2 || createdElementIds == null)
            {
                return;
            }

            // Force the visual sleeve to at least 150mm diameter.
            // This is separate from the real Pipe element, because some Revit MEP views display pipes as centerlines.
            double minimumRadius = UnitUtils.ConvertToInternalUnits(75.0, UnitTypeId.Millimeters);
            double radius = Math.Max(diameterFeet * 0.5, minimumRadius);

            for (int i = 0; i < routePoints.Count - 1; i++)
            {
                XYZ start = routePoints[i];
                XYZ end = routePoints[i + 1];
                if (start == null || end == null || start.DistanceTo(end) < UnitUtils.ConvertToInternalUnits(80.0, UnitTypeId.Millimeters))
                {
                    continue;
                }

                try
                {
                    Solid solid = CreateCylinderLikeSolid(start, end, radius);
                    if (solid == null || solid.Volume <= 0.0)
                    {
                        continue;
                    }

                    DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    if (shape == null)
                    {
                        continue;
                    }

                    shape.Name = "DEMO " + (label ?? role.ToString()) + " Pipe Visual";
                    shape.ApplicationId = "CadToRevit";
                    shape.ApplicationDataId = "PipeWorkDemoVisual_" + role.ToString() + "_" + i.ToString(CultureInfo.InvariantCulture);
                    shape.SetShape(new List<GeometryObject> { solid });
                    createdElementIds.Add(shape.Id);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[PipeWork] Failed to create demo pipe visual solid. Segment=" + i.ToString(CultureInfo.InvariantCulture) + ", Error=" + ex.Message);
                }
            }
        }

        private static Solid CreateCylinderLikeSolid(XYZ start, XYZ end, double radius)
        {
            if (start == null || end == null || radius <= 0.0)
            {
                return null;
            }

            XYZ direction = end - start;
            if (direction.GetLength() < 1e-9)
            {
                return null;
            }

            XYZ zAxis = direction.Normalize();
            XYZ xAxis = zAxis.CrossProduct(XYZ.BasisZ);
            if (xAxis.GetLength() < 1e-9)
            {
                xAxis = zAxis.CrossProduct(XYZ.BasisX);
            }

            if (xAxis.GetLength() < 1e-9)
            {
                xAxis = XYZ.BasisY;
            }

            xAxis = xAxis.Normalize();
            XYZ yAxis = zAxis.CrossProduct(xAxis).Normalize();

            XYZ c = start;
            XYZ p1 = c + xAxis * radius;
            XYZ p2 = c + yAxis * radius;
            XYZ p3 = c - xAxis * radius;
            XYZ p4 = c - yAxis * radius;

            CurveLoop profile = new CurveLoop();
            profile.Append(Arc.Create(p1, p2, c + (xAxis + yAxis).Normalize() * radius));
            XYZ negX = xAxis.Negate();
            XYZ negY = yAxis.Negate();

            profile.Append(Arc.Create(p2, p3, c + (negX + yAxis).Normalize() * radius));
            profile.Append(Arc.Create(p3, p4, c + (negX + negY).Normalize() * radius));
            profile.Append(Arc.Create(p4, p1, c + (xAxis - yAxis).Normalize() * radius));

            CurveLoop path = new CurveLoop();
            path.Append(Line.CreateBound(start, end));

            return GeometryCreationUtilities.CreateSweptGeometry(path, 0, 0.0, new List<CurveLoop> { profile });
        }

        private static Connector FindNearestConnector(Pipe pipe, XYZ referencePoint)
        {
            ConnectorSet connectorSet = pipe?.ConnectorManager?.Connectors;
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

        private static void ApplyDemoPipeColorOverrides(Document doc, PipeConnectorRole role, params ElementId[] elementIds)
        {
            if (doc == null || doc.ActiveView == null || elementIds == null || elementIds.Length == 0)
            {
                return;
            }

            Color roleColor = ResolveDemoPipeColor(role);
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
                    DiagnosticRecorder.AppendDebug("[PipeWork] Failed to apply pipe color override. ElementId=" + id.IntegerValue.ToString(CultureInfo.InvariantCulture) + ", Error=" + ex.Message);
                }
            }
        }

        private static Color ResolveDemoPipeColor(PipeConnectorRole role)
        {
            if (role == PipeConnectorRole.Chws)
            {
                return new Color(0x00, 0x00, 0xFF);
            }

            if (role == PipeConnectorRole.Chwr)
            {
                return new Color(0x00, 0xFF, 0x00);
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

        private static string GetRoleText(PipeConnectorRole role)
        {
            if (role == PipeConnectorRole.Chws)
            {
                return "CHWS / Hydronic Supply";
            }

            if (role == PipeConnectorRole.Chwr)
            {
                return "CHWR / Hydronic Return";
            }

            return "Pipe";
        }

        private static string FormatPointMm(XYZ point)
        {
            if (point == null)
            {
                return "null";
            }

            return "(" +
                   FormatNumber(UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters)) + ", " +
                   FormatNumber(UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters)) + ", " +
                   FormatNumber(UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters)) + ")mm";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
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
    }
}
