using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;

namespace CadToRevit.Services.Common
{
    public static class ViewDisplayHelper
    {
        public static void EnsureFineDetailLevel(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            View view = doc.ActiveView;
            if (view == null || view.IsTemplate)
            {
                return;
            }

            try
            {
                if (view.DetailLevel == ViewDetailLevel.Fine)
                {
                    return;
                }

                if (doc.IsModifiable)
                {
                    view.DetailLevel = ViewDetailLevel.Fine;
                    return;
                }

                using (Transaction tx = new Transaction(doc, "Set Active View Fine Detail Level"))
                {
                    tx.Start();
                    if (view.DetailLevel != ViewDetailLevel.Fine)
                    {
                        view.DetailLevel = ViewDetailLevel.Fine;
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ViewDisplay] Skip setting active view detail level to Fine. " + ex.Message);
            }
        }
    }
}
