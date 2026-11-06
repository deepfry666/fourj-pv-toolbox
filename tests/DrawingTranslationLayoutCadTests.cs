using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using StructuralToolbox.DrawingTranslation.Commands;
using StructuralToolbox.DrawingTranslation.Core;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

internal static class DrawingTranslationLayoutCadTests
{
    internal static void Run(Database database)
    {
        CheckWrapping(database);
        CheckRotations(database);
        CheckBatchReservations(database);
        CheckAttributes(database);
        CheckTable(database);
        CheckBlockNeighbour(database);
        CheckDrawingBoundary(database);
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
            "\nSTPV_TRANSLATION_LAYOUT_CAD_PASS scenarios=7");
    }

    internal static void CheckReopen(Database database)
    {
        int free = 0, rotations = 0, attributes = 0, blocked = 0, formatted = 0;
        MText upper = null, lower = null;
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            foreach (ObjectId id in Model(database, tx))
            {
                Entity entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
                MText text = entity as MText;
                if (text != null)
                {
                    if (text.Location.X == 20000 && text.TextHeight == 10 &&
                        text.Width == 240 && text.ActualHeight > 10) free++;
                    if (text.Contents == "Photovoltaic module maintenance access") rotations++;
                    if (text.Location.X == 10000 && text.Contents == "设备基础") blocked++;
                    if (text.Contents == "Equipment base waterproofing") upper = text;
                    if (text.Contents == "PV support foundation") lower = text;
                }
                BlockReference block = entity as BlockReference;
                if (block == null) continue;
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = (AttributeReference)tx.GetObject(attributeId, OpenMode.ForRead);
                    if (attribute.Tag == "LAYOUT" && attribute.TextString == "PV" &&
                        !attribute.IsMTextAttribute) attributes++;
                    if (attribute.Tag == "MULTILINE" && attribute.IsMTextAttribute)
                        using (MText content = attribute.MTextAttribute)
                            if (content.Contents.Contains("Equipment foundation") &&
                                content.ActualWidth <= 240.01 && content.ActualHeight > 10) attributes++;
                    if (attribute.Tag == "FORMATTED" && attribute.IsMTextAttribute)
                        using (MText content = attribute.MTextAttribute)
                            if (content.Contents == "{\\C2;Equipment}\\PFoundation" &&
                                content.Width == 240 && attribute.Height == 10) formatted++;
                }
            }
            Require(free == 1 && rotations == 7 && attributes == 2 && formatted == 1 && blocked == 1 &&
                upper != null && lower != null && lower.TextHeight >= 7.5 && lower.TextHeight < 10 &&
                upper.GeometricExtents.MinPoint.Y > lower.GeometricExtents.MaxPoint.Y,
                "fitted, wrapped or retained text changed after save/reopen");
            tx.Commit();
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nSTPV_TRANSLATION_LAYOUT_REOPEN_PASS");
    }

    internal static void UndoSeed(Database database)
    {
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            DBText text = new DBText(); text.SetDatabaseDefaults(database);
            text.Position = new Point3d(1000, 1000, 0); text.Height = 10;
            text.TextString = "光伏组件支架基础安装说明";
            Model(database, tx).AppendEntity(text); tx.AddNewlyCreatedDBObject(text, true); tx.Commit();
        }
        AddMText(database, 2000, 1000, 240, AttachmentPoint.TopLeft, "设备基础");
        AddFormattedAttribute(database, 3000, "UNDO_FORMATTED", "{\\C1;设备}\\P基础");
    }

    internal static void UndoApply(Database database)
    {
        List<ObjectId> ids = new List<ObjectId>();
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            foreach (ObjectId id in Model(database, tx))
            {
                BlockReference block = tx.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (block == null) ids.Add(id);
                else foreach (ObjectId attribute in block.AttributeCollection) ids.Add(attribute);
            }
            tx.Commit();
        }
        int skipped;
        Require(Write(database, ids.ToArray(), new[] {
            "STPV undo translated support note", "STPV undo translated equipment note",
            "{\\C1;Equipment}\\PFoundation" },
            out skipped) == 3 && skipped == 0, "UNDO fixture did not translate all three text kinds");
    }

    internal static void UndoCheck(Database database)
    {
        int found = 0, count = 0;
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            foreach (ObjectId id in Model(database, tx))
            {
                count++;
                DBText text = tx.GetObject(id, OpenMode.ForRead) as DBText;
                MText mtext = tx.GetObject(id, OpenMode.ForRead) as MText;
                if (text != null && text.TextString == "光伏组件支架基础安装说明") found++;
                if (mtext != null && mtext.Contents == "设备基础" && mtext.TextHeight == 10) found++;
                BlockReference block = tx.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (block != null)
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        AttributeReference attribute = (AttributeReference)tx.GetObject(attributeId, OpenMode.ForRead);
                        using (MText contents = attribute.MTextAttribute)
                            if (attribute.Tag == "UNDO_FORMATTED" && attribute.Height == 10 &&
                                contents.Contents == "{\\C1;设备}\\P基础") found++;
                    }
            }
            Require(count == 3 && found == 3, "one UNDO did not restore text, attribute formatting, types and height");
            tx.Commit();
        }
    }

    private static void CheckWrapping(Database database)
    {
        ObjectId id = AddMText(database, 20000, 1000, 240,
            AttachmentPoint.TopLeft, "设备基础");
        int skipped;
        Require(Write(database, new[] { id }, new[] {
            "Equipment foundation with waterproofing and maintenance access" },
            out skipped) == 1 && skipped == 0, "free-space translation was not written");
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            MText text = (MText)tx.GetObject(id, OpenMode.ForRead);
            Require(text.TextHeight == 10 && text.Width == 240 &&
                text.ActualHeight > 10 && text.ActualWidth <= 240.01,
                "free-space translation must wrap without reducing its font");
            tx.Commit();
        }
        ObjectId word = AddMText(database, 21000, 1000, 80,
            AttachmentPoint.TopLeft, "电气设备");
        Require(Write(database, new[] { word }, new[] { new String('W', 300) },
            out skipped) == 0 && skipped == 1,
            "unbreakable overflow was not reported separately");
        Require(ReadMText(database, word) == "电气设备", "overflow modified the source");
        ObjectId inline = AddMText(database, 22000, 1000, 80,
            AttachmentPoint.TopLeft, "说明");
        Require(Write(database, new[] { inline }, new[] { "{\\H200;Oversized}" },
            out skipped) == 0 && skipped == 1 && ReadMText(database, inline) == "说明",
            "absolute inline height escaped measurement");
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_WRAP_PASS");
    }

    private static void CheckRotations(Database database)
    {
        for (int index = 0; index < 7; index++)
        {
            ObjectId id;
            Point3d anchor = new Point3d(30000 + index * 2000, 1000, 0);
            double angle = index < 5 ? index * Math.PI / 4 : Math.PI / 6;
            Vector3d normal = index == 6 ? new Vector3d(0, 1, 1).GetNormal() : Vector3d.ZAxis;
            Vector3d right = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0).
                TransformBy(Matrix3d.PlaneToWorld(normal));
            Matrix3d toLocal = Matrix3d.AlignCoordinateSystem(Point3d.Origin,
                right, normal.CrossProduct(right), normal, Point3d.Origin,
                Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
            double width;
            ObjectId style, layer;
            using (Transaction tx = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord model = Model(database, tx);
                DBText text = new DBText();
                text.SetDatabaseDefaults(database);
                text.Normal = normal;
                text.Rotation = angle;
                text.Position = anchor;
                text.Height = 10;
                text.TextString = "光伏组件检修通道及基础安装说明";
                if (index == 5)
                {
                    text.HorizontalMode = TextHorizontalMode.TextCenter;
                    text.AlignmentPoint = anchor;
                }
                id = model.AppendEntity(text);
                tx.AddNewlyCreatedDBObject(text, true);
                text.AdjustAlignment(database);
                using (Entity local = text.GetTransformedCopy(toLocal))
                    width = local.GeometricExtents.MaxPoint.X - local.GeometricExtents.MinPoint.X;
                style = text.TextStyleId; layer = text.LayerId;
                tx.Commit();
            }
            int skipped;
            Require(Write(database, new[] { id }, new[] {
                "Photovoltaic module maintenance access" }, out skipped) == 1 && skipped == 0,
                "rotated DBText did not translate: " + index);
            using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
            {
                MText translated = null;
                foreach (ObjectId candidate in Model(database, tx))
                {
                    MText text = tx.GetObject(candidate, OpenMode.ForRead) as MText;
                    if (text != null && text.Contents == "Photovoltaic module maintenance access" &&
                        text.Location.DistanceTo(anchor) < 300) translated = text;
                }
                Require(translated != null && Math.Abs(translated.Width - width) < 0.01 &&
                    translated.Direction.IsEqualTo(right, new Tolerance(0.00001, 0.00001)) &&
                    translated.Normal.IsEqualTo(normal, new Tolerance(0.00001, 0.00001)) &&
                    translated.TextStyleId == style && translated.LayerId == layer,
                    "rotation, plane, original width, style or layer changed: " + index);
                if (index == 5) Require(translated.Attachment == AttachmentPoint.TopCenter,
                    "centred source lost its horizontal attachment");
                tx.Commit();
            }
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_ROTATION_PASS angles=7");
    }

    private static void CheckBatchReservations(Database database)
    {
        ObjectId upper = AddMText(database, 50000, 1000, 200,
            AttachmentPoint.TopLeft, "设备基础");
        ObjectId lower = AddMText(database, 50000, 960, 200,
            AttachmentPoint.BottomLeft, "支架");
        int skipped;
        int written = Write(database, new[] { upper, lower }, new[] {
            "Equipment base waterproofing",
            "PV support foundation" }, out skipped);
        Require(written == 2 && skipped == 0, "batch should fit both translations");
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            MText a = (MText)tx.GetObject(upper, OpenMode.ForRead);
            MText b = (MText)tx.GetObject(lower, OpenMode.ForRead);
            Require(a.GeometricExtents.MinPoint.Y > b.GeometricExtents.MaxPoint.Y,
                "later translation grew into an earlier translation in the same batch");
            Require(a.TextHeight >= 7.5 && b.TextHeight >= 7.5,
                "batch reduced text below its readability limit");
            Require(a.ActualHeight > 10 && b.TextHeight < 10,
                "batch must wrap the upper note and fit the lower note around its new bounds");
            tx.Commit();
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_BATCH_PASS");
    }

    private static void CheckAttributes(Database database)
    {
        ObjectId attributeId;
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            BlockTable blocks = (BlockTable)tx.GetObject(database.BlockTableId, OpenMode.ForWrite);
            BlockTableRecord definition = new BlockTableRecord();
            definition.Name = "STPV_LAYOUT_ATTRIBUTE";
            ObjectId definitionId = blocks.Add(definition);
            tx.AddNewlyCreatedDBObject(definition, true);
            AttributeDefinition field = new AttributeDefinition();
            field.SetDatabaseDefaults(database);
            field.Tag = "LAYOUT"; field.TextString = "设备基础"; field.Height = 10;
            definition.AppendEntity(field); tx.AddNewlyCreatedDBObject(field, true);
            BlockReference block = new BlockReference(new Point3d(60000, 1000, 0), definitionId);
            Model(database, tx).AppendEntity(block); tx.AddNewlyCreatedDBObject(block, true);
            AttributeReference attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(field, block.BlockTransform);
            attributeId = block.AttributeCollection.AppendAttribute(attribute);
            tx.AddNewlyCreatedDBObject(attribute, true);
            tx.Commit();
        }
        int skipped;
        Require(Write(database, new[] { attributeId }, new[] {
            "Equipment foundation with a deliberately overlong description" }, out skipped) == 0 &&
            skipped == 1, "long attribute must retain its type and source if it cannot fit");
        Require(Write(database, new[] { attributeId }, new[] { "PV" }, out skipped) == 1 && skipped == 0,
            "short attribute translation should fit");
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            AttributeReference attribute = (AttributeReference)tx.GetObject(attributeId, OpenMode.ForRead);
            Require(attribute.TextString == "PV" && attribute.Tag == "LAYOUT" &&
                attribute.WidthFactor == 1 && attribute.Height >= 7.5,
                "attribute was flattened, squashed or lost its tag");
            tx.Commit();
        }
        CheckMultilineAttribute(database);
        CheckFormattedAttribute(database);
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_ATTRIBUTE_PASS single-and-multiline");
    }

    private static void CheckFormattedAttribute(Database database)
    {
        const string original = "{\\C1;设备}\\P基础";
        ObjectId id = AddFormattedAttribute(database, 64000, "FORMATTED", original);
        int skipped;
        IList<TranslationTarget> targets = TranslationCommands.ReadTargets(database, new[] { id }, out skipped);
        Require(targets.Count == 1 && skipped == 0 && targets[0].Item.RawContent == original,
            "multiline attribute source discarded paragraphs or inline formatting");
        string translated, error;
        Require(targets[0].Item.Protection.TryRestore(
            targets[0].Item.TranslatableContent.Replace("设备", "Equipment").Replace("基础", "Foundation"),
            out translated, out error) && translated == "{\\C1;Equipment}\\PFoundation",
            "multiline attribute formatting was not protected/restored");
        int stale, layout;
        Require(TranslationCommands.ReplaceEdits(database,
            new[] { new TranslationEdit(targets[0], translated) }, out stale, out layout) == 1 &&
            stale == 0 && layout == 0, "formatted attribute translation was not written");
        targets = TranslationCommands.ReadTargets(database, new[] { id }, out skipped);
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            AttributeReference attribute = (AttributeReference)tx.GetObject(id, OpenMode.ForWrite);
            using (MText text = attribute.MTextAttribute)
            {
                Require(text.Contents == translated && text.ActualHeight > 10,
                    "formatted attribute lost its colour or paragraph on write");
                text.Contents = translated.Replace("\\C1;", "\\C2;");
                attribute.MTextAttribute = text;
            }
            tx.Commit();
        }
        Require(TranslationCommands.ReplaceEdits(database,
            new[] { new TranslationEdit(targets[0], "Should not overwrite changed formatting") },
            out stale, out layout) == 0 && stale == 1 && layout == 0,
            "format-only source edit was not detected as stale");
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            AttributeReference attribute = (AttributeReference)tx.GetObject(id, OpenMode.ForRead);
            using (MText text = attribute.MTextAttribute)
                Require(text.Contents == "{\\C2;Equipment}\\PFoundation",
                    "stale attribute did not retain the user's changed formatting");
            tx.Commit();
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nATTRIBUTE_FORMAT_AND_STALE_PASS");
    }

    private static ObjectId AddFormattedAttribute(Database database, double x, string tag, string contents)
    {
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            BlockTable blocks = (BlockTable)tx.GetObject(database.BlockTableId, OpenMode.ForWrite);
            BlockTableRecord definition = new BlockTableRecord();
            definition.Name = "STPV_" + tag + "_" + Guid.NewGuid().ToString("N");
            ObjectId definitionId = blocks.Add(definition); tx.AddNewlyCreatedDBObject(definition, true);
            AttributeDefinition field = new AttributeDefinition(); field.SetDatabaseDefaults(database);
            field.Tag = tag; field.TextString = "设备基础"; field.Height = 10;
            field.IsMTextAttributeDefinition = true;
            using (MText text = field.MTextAttributeDefinition)
            {
                text.Width = 240; text.TextHeight = 10; text.Contents = contents;
                field.MTextAttributeDefinition = text;
            }
            definition.AppendEntity(field); tx.AddNewlyCreatedDBObject(field, true);
            BlockReference block = new BlockReference(new Point3d(x, 1000, 0), definitionId);
            Model(database, tx).AppendEntity(block); tx.AddNewlyCreatedDBObject(block, true);
            AttributeReference attribute = new AttributeReference(); attribute.SetAttributeFromBlock(field, block.BlockTransform);
            ObjectId id = block.AttributeCollection.AppendAttribute(attribute);
            tx.AddNewlyCreatedDBObject(attribute, true); tx.Commit(); return id;
        }
    }

    private static void CheckMultilineAttribute(Database database)
    {
        ObjectId id;
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            BlockTable blocks = (BlockTable)tx.GetObject(database.BlockTableId, OpenMode.ForWrite);
            BlockTableRecord definition = new BlockTableRecord();
            definition.Name = "STPV_LAYOUT_MULTILINE_ATTRIBUTE";
            ObjectId definitionId = blocks.Add(definition); tx.AddNewlyCreatedDBObject(definition, true);
            AttributeDefinition field = new AttributeDefinition(); field.SetDatabaseDefaults(database);
            field.Tag = "MULTILINE"; field.TextString = "设备基础"; field.Height = 10;
            field.IsMTextAttributeDefinition = true;
            using (MText text = field.MTextAttributeDefinition)
            {
                text.TextHeight = 10; text.Width = 240; text.Contents = "设备基础";
                field.MTextAttributeDefinition = text;
            }
            definition.AppendEntity(field); tx.AddNewlyCreatedDBObject(field, true);
            BlockReference block = new BlockReference(new Point3d(62000, 1000, 0), definitionId);
            Model(database, tx).AppendEntity(block); tx.AddNewlyCreatedDBObject(block, true);
            AttributeReference attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(field, block.BlockTransform);
            id = block.AttributeCollection.AppendAttribute(attribute);
            tx.AddNewlyCreatedDBObject(attribute, true); tx.Commit();
        }
        int skipped;
        Require(Write(database, new[] { id }, new[] {
            "Equipment foundation with waterproofing and maintenance access" }, out skipped) == 1 && skipped == 0,
            "multiline attribute should wrap inside its declared width");
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            AttributeReference attribute = (AttributeReference)tx.GetObject(id, OpenMode.ForRead);
            using (MText text = attribute.MTextAttribute)
                Require(attribute.IsMTextAttribute && attribute.Tag == "MULTILINE" &&
                    text.Contents.Contains("Equipment foundation") && text.ActualHeight > 10 &&
                    text.ActualWidth <= 240.01, "multiline attribute did not retain its layout or content");
            tx.Commit();
        }
    }

    private static void CheckTable(Database database)
    {
        ObjectId id;
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            Table table = new Table(); table.SetDatabaseDefaults(database);
            table.TableStyle = database.Tablestyle;
            table.SetSize(1, 1); table.Position = new Point3d(70000, 1000, 0);
            table.SetColumnWidth(100); table.SetRowHeight(20);
            table.Cells[0, 0].TextHeight = 10;
            table.Cells[0, 0].TextString = "设备";
            table.GenerateLayout();
            id = Model(database, tx).AppendEntity(table);
            tx.AddNewlyCreatedDBObject(table, true); tx.Commit();
        }
        AddMText(database, 70000, 960, 100, AttachmentPoint.TopLeft, "表外说明必须保留");
        int skipped;
        Require(Write(database, new[] { id }, new[] {
            "Equipment foundation installation requirements and construction " +
            "instructions with detailed waterproofing and maintenance specifications" },
            out skipped) == 0 && skipped == 1,
            "expanding table must not cover an external label");
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            Table table = (Table)tx.GetObject(id, OpenMode.ForRead);
            Require(table.Cells[0, 0].TextString == "设备" && table.Rows[0].Height <= 21,
                "rejected cell translation changed the table or its row height");
            tx.Commit();
        }
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_TABLE_PASS");
    }

    private static void CheckBlockNeighbour(Database database)
    {
        ObjectId source = AddMText(database, 80000, 1000, 100,
            AttachmentPoint.TopLeft, "设备基础");
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            BlockTable blocks = (BlockTable)tx.GetObject(database.BlockTableId, OpenMode.ForWrite);
            BlockTableRecord definition = new BlockTableRecord();
            definition.Name = "STPV_LAYOUT_NEIGHBOUR";
            ObjectId id = blocks.Add(definition); tx.AddNewlyCreatedDBObject(definition, true);
            MText label = new MText(); label.SetDatabaseDefaults(database);
            label.Location = Point3d.Origin; label.TextHeight = 10;
            label.Width = 100; label.Contents = "块内相邻文字";
            definition.AppendEntity(label); tx.AddNewlyCreatedDBObject(label, true);
            BlockReference block = new BlockReference(new Point3d(80000, 980, 0), id);
            block.ScaleFactors = new Scale3d(1.2, 0.8, 1);
            Model(database, tx).AppendEntity(block); tx.AddNewlyCreatedDBObject(block, true);
            MLeader empty = new MLeader(); empty.SetDatabaseDefaults(database);
            Model(database, tx).AppendEntity(empty); tx.AddNewlyCreatedDBObject(empty, true);
            tx.Commit();
        }
        int skipped;
        Require(Write(database, new[] { source }, new[] {
            "Equipment foundation with additional construction instructions " +
            "that cannot fit above the text in an unselected block" }, out skipped) == 0 && skipped == 1 &&
            ReadMText(database, source) == "设备基础", "block text was not treated as occupied space");
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_BLOCK_PASS");
    }

    private static int Write(Database database, ObjectId[] ids, string[] values, out int layoutSkipped)
    {
        int skipped;
        IList<TranslationTarget> targets = TranslationCommands.ReadTargets(database, ids, out skipped);
        Require(skipped == 0 && targets.Count == values.Length, "layout fixture was not readable");
        List<TranslationEdit> edits = new List<TranslationEdit>();
        for (int i = 0; i < targets.Count; i++) edits.Add(new TranslationEdit(targets[i], values[i]));
        int stale;
        int written = TranslationCommands.ReplaceEdits(database, edits, out stale, out layoutSkipped);
        Require(stale == 0, "layout overflow was misreported as a changed source");
        return written;
    }

    private static void CheckDrawingBoundary(Database database)
    {
        ObjectId source = AddMText(database, 90000, 1000, 100,
            AttachmentPoint.TopLeft, "设备基础");
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            Polyline outline = new Polyline(); outline.SetDatabaseDefaults(database);
            outline.AddVertexAt(0, new Point2d(89990, 1010), 0, 0, 0);
            outline.AddVertexAt(1, new Point2d(90110, 1010), 0, 0, 0);
            outline.AddVertexAt(2, new Point2d(90110, 980), 0, 0, 0);
            outline.AddVertexAt(3, new Point2d(89990, 980), 0, 0, 0);
            outline.Closed = true;
            Model(database, tx).AppendEntity(outline); tx.AddNewlyCreatedDBObject(outline, true);
            tx.Commit();
        }
        int skipped;
        Require(Write(database, new[] { source }, new[] {
            "Equipment foundation with waterproofing and detailed construction instructions" },
            out skipped) == 0 && skipped == 1 && ReadMText(database, source) == "设备基础",
            "translation crossed the straight boundary of its drawing cell");
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nLAYOUT_BOUNDARY_PASS");
    }

    private static ObjectId AddMText(Database database, double x, double y,
        double width, AttachmentPoint attachment, string value)
    {
        using (Transaction tx = database.TransactionManager.StartTransaction())
        {
            MText text = new MText(); text.SetDatabaseDefaults(database);
            text.Attachment = attachment; text.Location = new Point3d(x, y, 0);
            text.TextHeight = 10; text.Width = width; text.Contents = value;
            ObjectId id = Model(database, tx).AppendEntity(text);
            tx.AddNewlyCreatedDBObject(text, true); tx.Commit(); return id;
        }
    }

    private static string ReadMText(Database database, ObjectId id)
    {
        using (Transaction tx = database.TransactionManager.StartOpenCloseTransaction())
        {
            string value = ((MText)tx.GetObject(id, OpenMode.ForRead)).Contents;
            tx.Commit(); return value;
        }
    }

    private static BlockTableRecord Model(Database database, Transaction tx)
    {
        return (BlockTableRecord)tx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForWrite);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
