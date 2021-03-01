using System;
using System.Collections.Generic;
using System.Text;

using Autodesk.Revit;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.ApplicationServices;
using RevitExporter.Exporter;
using System.Windows.Forms;

namespace RevitExporter
{
    /// <summary>
    /// Demonstrate how a basic ExternalCommand can be added to the Revit user interface. 
    /// And demonstrate how to create a Revit style dialog.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    [Autodesk.Revit.Attributes.Journaling(Autodesk.Revit.Attributes.JournalingMode.NoCommandData)]
    public class Command : IExternalCommand
    {
        #region IExternalCommand Members

        public Autodesk.Revit.UI.Result Execute(ExternalCommandData commandData,
            ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View3D view = doc.ActiveView as View3D;
            int lodGltfValue = 8;
            GLTFExpoterContext contextGltf = new GLTFExpoterContext(doc, lodGltfValue);

            SaveFileDialog sdial = new SaveFileDialog();
            sdial.Filter = "gltf|*.gltf|glb|*.glb";
            if (sdial.ShowDialog() == DialogResult.OK)
            {
                using (CustomExporter exporterGltf = new CustomExporter(doc, contextGltf))
                {
                    //是否包括Geom对象                    
                    exporterGltf.IncludeGeometricObjects = false;
                    exporterGltf.ShouldStopOnError = true;
                    //导出3D模型                   
                    exporterGltf.Export(view);
                    contextGltf.Model.SaveGLB(sdial.FileName);
                    contextGltf.Model.SaveGLTF(sdial.FileName);
                }
            }
            return Autodesk.Revit.UI.Result.Succeeded;
        }

        #endregion
    }
}
