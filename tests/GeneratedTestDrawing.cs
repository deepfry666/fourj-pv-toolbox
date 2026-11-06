using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
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
        string kind = Environment.GetEnvironmentVariable("FOURJ_TEST_DRAWING_KIND") ?? "Blank";
        if (kind != "Blank" && kind != "RailsOnly")
            throw new InvalidOperationException("Unknown generated drawing kind.");
        using (Database database = new Database(true, true))
        {
            database.Insunits = UnitsValue.Millimeters;
            if (kind == "RailsOnly") CreateRailSources(database);
            database.SaveAs(path, DwgVersion.Current);
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
            "\nFOURJ_GENERATED_DRAWING=PASS KIND=" + kind + "\n");
    }

    private static void CreateRailSources(Database database)
    {
        using (Transaction transaction = database.TransactionManager.StartTransaction())
        {
            LayerTable layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
            LayerTableRecord layer = new LayerTableRecord();
            layer.Name = "STPV_GENERATED_PANELS";
            ObjectId layerId = layers.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
            BlockTableRecord model = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 6; column++)
                {
                    double x = 1000 + column * 1154;
                    double y = 1000 + row * 2298;
                    Polyline panel = new Polyline();
                    panel.SetDatabaseDefaults(database);
                    panel.LayerId = layerId;
                    panel.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
                    panel.AddVertexAt(1, new Point2d(x + 1134, y), 0, 0, 0);
                    panel.AddVertexAt(2, new Point2d(x + 1134, y + 2278), 0, 0, 0);
                    panel.AddVertexAt(3, new Point2d(x, y + 2278), 0, 0, 0);
                    panel.Closed = true;
                    model.AppendEntity(panel);
                    transaction.AddNewlyCreatedDBObject(panel, true);
                }
            RotatedDimension dimension = new RotatedDimension(0,
                new Point3d(1000, 1000, 0), new Point3d(2134, 1000, 0),
                new Point3d(1000, 500, 0), String.Empty, database.Dimstyle);
            dimension.SetDatabaseDefaults(database);
            model.AppendEntity(dimension);
            transaction.AddNewlyCreatedDBObject(dimension, true);
            transaction.Commit();
        }
    }
}
