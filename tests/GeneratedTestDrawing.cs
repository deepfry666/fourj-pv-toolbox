using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public static class GeneratedTestDrawing
{
    [CommandMethod("FOURJGENERATETESTDRAWING", CommandFlags.Session)]
    public static void Create()
    {
        string path = Environment.GetEnvironmentVariable("FOURJ_TEST_DRAWING_OUTPUT");
        if (String.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("No test drawing destination was supplied.");
        using (Database database = new Database(true, true))
        {
            database.Insunits = UnitsValue.Millimeters;
            database.SaveAs(path, DwgVersion.Current);
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
            "\nFOURJ_GENERATED_DRAWING=PASS\n");
    }
}
