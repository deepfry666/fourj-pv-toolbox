using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using StructuralToolbox.DrawingTranslation.Core;

namespace StructuralToolbox.DrawingTranslation.Commands
{
    // One snapshot per transaction. Accepted edits reserve their final bounds.
    internal sealed class TranslationLayoutSession
    {
        private readonly Database _database;
        private readonly Transaction _transaction;
        private readonly List<OccupiedArea> _occupied = new List<OccupiedArea>();

        internal TranslationLayoutSession(Database database,
            Transaction transaction, BlockTableRecord model)
        {
            _database = database;
            _transaction = transaction;
            foreach (ObjectId id in model)
                Collect(transaction.GetObject(id, OpenMode.ForRead, false)
                    as Entity, id, Matrix3d.Identity, 0);
        }

        internal bool TryCreateWrapped(DBText source, string value,
            out MText replacement)
        {
            replacement = null;
            if (source.IsMirroredInX || source.IsMirroredInY) return false;
            TextFrame frame = TextFrame.ForText(source);
            TextLayoutBounds original;
            if (!TryBounds(source, frame, out original)) return false;
            MText candidate = new MText();
            try
            {
                candidate.SetDatabaseDefaults(_database);
                candidate.SetPropertiesFrom(source);
                candidate.Normal = source.Normal;
                candidate.Direction = frame.Right;
                candidate.TextStyleId = source.TextStyleId;
                int column = source.HorizontalMode == TextHorizontalMode.TextRight ? 2 :
                    source.HorizontalMode == TextHorizontalMode.TextCenter ||
                    source.HorizontalMode == TextHorizontalMode.TextMid ? 1 : 0;
                int row = source.VerticalMode == TextVerticalMode.TextBottom ? 2 :
                    source.VerticalMode == TextVerticalMode.TextVerticalMid ? 1 : 0;
                AttachmentPoint[] attachments = { AttachmentPoint.TopLeft,
                    AttachmentPoint.TopCenter, AttachmentPoint.TopRight,
                    AttachmentPoint.MiddleLeft, AttachmentPoint.MiddleCenter,
                    AttachmentPoint.MiddleRight, AttachmentPoint.BottomLeft,
                    AttachmentPoint.BottomCenter, AttachmentPoint.BottomRight };
                candidate.Attachment = attachments[row * 3 + column];
                candidate.Location = frame.Point(column == 0 ? original.Left :
                    column == 1 ? (original.Left + original.Right) / 2 : original.Right,
                    row == 0 ? original.Top : row == 1 ?
                    (original.Bottom + original.Top) / 2 : original.Bottom);
                candidate.Width = original.Width;
                string contents = ToContents(value);
                if (Math.Abs(source.WidthFactor - 1) > 0.000001)
                    contents = "{\\W" + source.WidthFactor.ToString("R",
                        CultureInfo.InvariantCulture) + ";" + contents + "}";
                candidate.Contents = contents;
                double fitted;
                if (!TranslationLayout.TryFit(source.Height, original.Width,
                    Math.Max(original.Height,
                        source.Height * TranslationLayout.MaximumLineHeights),
                    delegate(double height) {
                        candidate.TextHeight = height;
                        candidate.Contents = contents;
                        return Measured(candidate, frame);
                    }, Obstacles(source.ObjectId, frame, original), out fitted)) return false;
                candidate.TextHeight = fitted;
                replacement = candidate;
                candidate = null;
                return true;
            }
            finally { if (candidate != null) candidate.Dispose(); }
        }

        internal bool TrySetMText(MText source, string value)
        {
            double height, width;
            if (!TryFitMText(source, source.ObjectId, value, out height,
                out width)) return false;
            source.UpgradeOpen();
            source.Width = width;
            source.TextHeight = height;
            source.Contents = ToContents(value);
            return true;
        }

        private bool TryFitMText(MText source, ObjectId key, string value,
            out double height, out double width)
        {
            height = source.TextHeight;
            width = source.Width;
            TextFrame frame = TextFrame.ForMText(source);
            TextLayoutBounds original;
            if (!TryBounds(source, frame, out original)) return false;
            if (width <= 0) width = original.Width;
            using (MText candidate = (MText)source.Clone())
            {
                candidate.Width = width;
                candidate.Contents = ToContents(value);
                return TranslationLayout.TryFit(source.TextHeight, width,
                    Math.Max(original.Height,
                        source.TextHeight * TranslationLayout.MaximumLineHeights),
                    delegate(double trial) {
                        candidate.TextHeight = trial;
                        candidate.Contents = ToContents(value);
                        return Measured(candidate, frame);
                    }, Obstacles(key, frame, original), out height);
            }
        }

        internal bool TrySetAttribute(AttributeReference source, string value)
        {
            if (source.IsMTextAttribute)
            {
                using (MText text = source.MTextAttribute)
                {
                    double height, width;
                    if (text == null || !TryFitMText(text, source.ObjectId,
                        value, out height, out width)) return false;
                    text.TextHeight = height;
                    text.Width = width;
                    text.Contents = ToContents(value);
                    source.UpgradeOpen();
                    source.Height = height;
                    source.MTextAttribute = text;
                    return true;
                }
            }
            TextFrame frame = TextFrame.ForText(source);
            TextLayoutBounds original;
            if (!TryBounds(source, frame, out original)) return false;
            using (AttributeReference candidate =
                (AttributeReference)source.Clone())
            {
                candidate.TextString = value;
                double fitted;
                if (!TranslationLayout.TryFit(source.Height, original.Width,
                    Math.Max(original.Height, source.Height),
                    delegate(double height) {
                        candidate.Height = height;
                        candidate.AdjustAlignment(_database);
                        return Measured(candidate, frame);
                    }, Obstacles(source.ObjectId, frame, original), out fitted)) return false;
                source.UpgradeOpen();
                source.TextString = value;
                source.Height = fitted;
                source.AdjustAlignment(_database);
                return true;
            }
        }

        internal bool TrySetCell(Table source, int row, int column, string value)
        {
            TextFrame frame = new TextFrame(source.Direction, source.Normal,
                source.Position);
            TextLayoutBounds original;
            if (!TryBounds(source, frame, out original)) return false;
            double originalHeight = source.Cells[row, column].TextHeight.GetValueOrDefault();
            double fitted;
            if (!TranslationLayout.TryFit(originalHeight, original.Width,
                original.Height + originalHeight * TranslationLayout.MaximumLineHeights,
                delegate(double height) {
                    // Layout can increase row heights: reset each trial so a
                    // rejected translation cannot leave a larger table behind.
                    using (Table candidate = (Table)source.Clone())
                    {
                        candidate.Cells[row, column].TextHeight = height;
                        candidate.Cells[row, column].Contents[0].TextString = ToContents(value);
                        candidate.GenerateLayout();
                        return Measured(candidate, frame);
                    }
                }, Obstacles(source.ObjectId, frame, original), out fitted)) return false;
            source.UpgradeOpen();
            source.Cells[row, column].TextHeight = fitted;
            source.Cells[row, column].Contents[0].TextString = ToContents(value);
            source.GenerateLayout();
            return true;
        }

        internal void Reserve(ObjectId key, Entity entity)
        {
            _occupied.RemoveAll(area => area.Key == key);
            Record(entity, key, Matrix3d.Identity);
        }

        private void Collect(Entity entity, ObjectId key, Matrix3d transform,
            int depth)
        {
            if (entity == null || !entity.Visible) return;
            Line line = entity as Line;
            if (line != null)
            {
                RecordEdge(key, line.StartPoint, line.EndPoint, transform);
                return;
            }
            Polyline outline = entity as Polyline;
            if (outline != null)
            {
                int count = outline.Closed ? outline.NumberOfVertices : outline.NumberOfVertices - 1;
                for (int index = 0; index < count; index++)
                    if (Math.Abs(outline.GetBulgeAt(index)) < 0.000001)
                        RecordEdge(key, outline.GetPoint3dAt(index),
                            outline.GetPoint3dAt((index + 1) % outline.NumberOfVertices), transform);
                return;
            }
            BlockReference block = entity as BlockReference;
            if (block != null && depth < 8)
            {
                foreach (ObjectId id in block.AttributeCollection)
                {
                    AttributeReference attribute = _transaction.GetObject(id,
                        OpenMode.ForRead, false) as AttributeReference;
                    if (attribute != null && !attribute.Invisible)
                        Record(attribute, depth == 0 ? id : key, transform);
                }
                BlockTableRecord definition = _transaction.GetObject(
                    block.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;
                if (definition != null)
                    foreach (ObjectId id in definition)
                    {
                        Entity child = _transaction.GetObject(id,
                            OpenMode.ForRead, false) as Entity;
                        AttributeDefinition attribute = child as AttributeDefinition;
                        if (attribute != null && (!attribute.Constant || attribute.Invisible))
                            continue;
                        Collect(child, key, transform * block.BlockTransform, depth + 1);
                    }
            }
            else if (entity is DBText || entity is MText || entity is Table ||
                entity is Dimension || entity is MLeader || block != null)
                Record(entity, key, transform);
        }

        private void RecordEdge(ObjectId key, Point3d start, Point3d end, Matrix3d transform)
        {
            _occupied.Add(new OccupiedArea(key,
                new[] { start.TransformBy(transform), end.TransformBy(transform) }, true));
        }

        private void Record(Entity entity, ObjectId key, Matrix3d transform)
        {
            AttributeReference attribute = entity as AttributeReference;
            if (attribute != null && attribute.IsMTextAttribute)
            {
                using (MText text = attribute.MTextAttribute)
                    if (text != null) Record(text, key, transform);
                return;
            }
            try
            {
                DBText text = entity as DBText;
                MText mtext = entity as MText;
                TextFrame frame = text != null ? TextFrame.ForText(text) :
                    mtext != null ? TextFrame.ForMText(mtext) :
                    new TextFrame(Vector3d.XAxis, Vector3d.ZAxis,
                        entity.GeometricExtents.MinPoint);
                TextLayoutBounds bounds;
                if (!TryBounds(entity, frame, out bounds)) return;
                // Transform the measured corners instead of the entity. Text
                // inside a non-uniformly scaled block can reject TransformBy.
                _occupied.Add(new OccupiedArea(key, new[] {
                    frame.Point(bounds.Left, bounds.Bottom).TransformBy(transform),
                    frame.Point(bounds.Left, bounds.Top).TransformBy(transform),
                    frame.Point(bounds.Right, bounds.Top).TransformBy(transform),
                    frame.Point(bounds.Right, bounds.Bottom).TransformBy(transform) }));
            }
            // Empty leaders/dimensions have no visible footprint to reserve.
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
        }

        private IList<TextLayoutBounds> Obstacles(ObjectId key, TextFrame frame,
            TextLayoutBounds original)
        {
            List<TextLayoutBounds> result = new List<TextLayoutBounds>();
            foreach (OccupiedArea area in _occupied)
            {
                if (area.Key == key) continue;
                double left = Double.PositiveInfinity, bottom = left;
                double right = Double.NegativeInfinity, top = right;
                double minPlane = Double.PositiveInfinity, maxPlane = Double.NegativeInfinity;
                foreach (Point3d point in area.Points)
                {
                    Point3d local = point.TransformBy(frame.ToLocal);
                    left = Math.Min(left, local.X); right = Math.Max(right, local.X);
                    bottom = Math.Min(bottom, local.Y); top = Math.Max(top, local.Y);
                    minPlane = Math.Min(minPlane, local.Z); maxPlane = Math.Max(maxPlane, local.Z);
                }
                if (frame.Plane < minPlane - 0.001 || frame.Plane > maxPlane + 0.001)
                    continue;
                TextLayoutBounds bounds = new TextLayoutBounds(left, bottom, right, top);
                // Existing drawing lines can intentionally pass behind a label.
                // Preserve that relationship; constrain growth across new edges.
                if (area.IsGeometry && original.Intersects(bounds, 0)) continue;
                result.Add(bounds);
            }
            return result;
        }

        private static TextLayoutBounds Measured(Entity entity, TextFrame frame)
        {
            TextLayoutBounds bounds;
            return TryBounds(entity, frame, out bounds) ? bounds :
                new TextLayoutBounds(Double.NaN, 0, 0, 0);
        }

        private static bool TryBounds(Entity entity, TextFrame frame,
            out TextLayoutBounds bounds)
        {
            bounds = new TextLayoutBounds(Double.NaN, 0, 0, 0);
            try
            {
                // Measure the entity after rotating it into its text plane.
                // Reprojecting its world-axis box overestimates rotated labels.
                using (Entity local = entity.GetTransformedCopy(frame.ToLocal))
                {
                    Extents3d extents = local.GeometricExtents;
                    // MText's extents can stop at its declared column width
                    // even when an unbreakable word visibly extends past it.
                    MText mtext = local as MText;
                    double overflow = mtext == null ? 0 :
                        Math.Max(0, mtext.ActualWidth -
                            (extents.MaxPoint.X - extents.MinPoint.X));
                    bounds = new TextLayoutBounds(extents.MinPoint.X,
                        extents.MinPoint.Y, extents.MaxPoint.X + overflow, extents.MaxPoint.Y);
                    return bounds.IsValid && bounds.Width > 0 && bounds.Height > 0;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        private static string ToContents(string value)
        {
            return (value ?? String.Empty).Replace("\r\n", "\n").
                Replace("\r", "\n").Replace("\n", "\\P");
        }

        private sealed class OccupiedArea
        {
            internal readonly ObjectId Key;
            internal readonly Point3d[] Points;
            internal readonly bool IsGeometry;
            internal OccupiedArea(ObjectId key, Point3d[] points, bool isGeometry = false)
            { Key = key; Points = points; IsGeometry = isGeometry; }
        }

        private sealed class TextFrame
        {
            internal readonly Vector3d Right, Up, Normal;
            internal readonly Matrix3d ToLocal;
            internal readonly double Plane;
            internal TextFrame(Vector3d right, Vector3d normal, Point3d anchor)
            {
                Normal = normal.GetNormal(); Right = right.GetNormal();
                Up = Normal.CrossProduct(Right).GetNormal();
                Plane = anchor.GetAsVector().DotProduct(Normal);
                ToLocal = Matrix3d.AlignCoordinateSystem(Point3d.Origin,
                    Right, Up, Normal, Point3d.Origin, Vector3d.XAxis,
                    Vector3d.YAxis, Vector3d.ZAxis);
            }
            internal Point3d Point(double x, double y)
            { return Point3d.Origin + Right * x + Up * y + Normal * Plane; }
            internal static TextFrame ForText(DBText text)
            {
                Vector3d right = new Vector3d(Math.Cos(text.Rotation),
                    Math.Sin(text.Rotation), 0).TransformBy(Matrix3d.PlaneToWorld(text.Normal));
                return new TextFrame(right, text.Normal, text.Position);
            }
            internal static TextFrame ForMText(MText text)
            { return new TextFrame(text.Direction, text.Normal, text.Location); }
        }
    }
}
