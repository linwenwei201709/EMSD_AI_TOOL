using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace CadToRevit.Services.Workflow
{
    public enum ProjectWorkflowMode
    {
        None = 0,
        DwgImportMode = 1,
        RvtModelImportMode = 2
    }

    public static class ProjectWorkflowModeStoreService
    {
        private static readonly Guid SchemaGuid = new Guid("A7C1A8B9-5E84-4F3F-A8B4-2B8E52E0721F");
        private const string SchemaName = "CadToRevitProjectWorkflowModeStore";
        private const string ModeFieldName = "WorkflowMode";

        public static ProjectWorkflowMode GetMode(Document doc)
        {
            if (doc == null || doc.IsFamilyDocument)
            {
                return ProjectWorkflowMode.None;
            }

            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null)
                {
                    return ProjectWorkflowMode.None;
                }

                Element projectInfo = doc.ProjectInformation;
                if (projectInfo == null)
                {
                    return ProjectWorkflowMode.None;
                }

                Entity entity = projectInfo.GetEntity(schema);
                if (entity == null || !entity.IsValid())
                {
                    return ProjectWorkflowMode.None;
                }

                string modeText = entity.Get<string>(ModeFieldName);
                if (Enum.TryParse(modeText, out ProjectWorkflowMode mode))
                {
                    return mode;
                }
            }
            catch
            {
                // Keep ribbon usable if the project information storage is unavailable.
            }

            return ProjectWorkflowMode.None;
        }

        public static void SetMode(Document doc, ProjectWorkflowMode mode)
        {
            if (doc == null || doc.IsFamilyDocument)
            {
                return;
            }

            try
            {
                Schema schema = EnsureSchema();
                Entity entity = new Entity(schema);
                entity.Set(ModeFieldName, mode.ToString());

                using (Transaction tx = new Transaction(doc, "Set EMSD Workflow Mode"))
                {
                    tx.Start();
                    doc.ProjectInformation.SetEntity(entity);
                    tx.Commit();
                }
            }
            catch
            {
                // Do not block Import DWG / RVT Model Import if the mode cannot be persisted.
            }
        }

        public static void ClearMode(Document doc)
        {
            SetMode(doc, ProjectWorkflowMode.None);
        }

        private static Schema EnsureSchema()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null)
            {
                return existing;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId("EMSD");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(ModeFieldName, typeof(string));
            return builder.Finish();
        }
    }
}
