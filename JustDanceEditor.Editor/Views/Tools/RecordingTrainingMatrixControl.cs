using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Tools;

using System;
using System.Globalization;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class RecordingTrainingCellClickedEventArgs(int rowIndex, int columnIndex) : EventArgs
{
    public int RowIndex { get; } = rowIndex;
    public int ColumnIndex { get; } = columnIndex;
}

public sealed class RecordingTrainingMatrixControl : Control
{
    private const double LabelWidth = 176;
    private const double HeaderHeight = 116;
    private const double CellWidth = 30;
    private const double CellHeight = 28;

    public static readonly StyledProperty<RecordingTrainingMatrixViewModel?> MatrixProperty =
        AvaloniaProperty.Register<RecordingTrainingMatrixControl, RecordingTrainingMatrixViewModel?>(nameof(Matrix));

    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(226, 225, 232));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.FromRgb(174, 171, 184));
    private static readonly IBrush ExcludedBrush = new SolidColorBrush(Color.FromRgb(12, 12, 14));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.FromRgb(74, 72, 79));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), 1);
    private (int Row, int Column)? _hoveredCell;

    static RecordingTrainingMatrixControl()
    {
        AffectsRender<RecordingTrainingMatrixControl>(MatrixProperty);
    }

    public event EventHandler<RecordingTrainingCellClickedEventArgs>? CellClicked;

    public RecordingTrainingMatrixViewModel? Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RecordingTrainingMatrixViewModel? matrix = Matrix;
        if (matrix == null)
            return;

        DrawText(context, "Recording", 11, TextBrush, new Point(4, HeaderHeight - 19));
        for (int columnIndex = 0; columnIndex < matrix.Columns.Count; columnIndex++)
        {
            double x = LabelWidth + (columnIndex * CellWidth);
            DrawMoveHeader(context, matrix.Columns[columnIndex].MoveId, x);
        }

        for (int rowIndex = 0; rowIndex < matrix.Rows.Count; rowIndex++)
        {
            RecordingTrainingRowViewModel row = matrix.Rows[rowIndex];
            double y = HeaderHeight + (rowIndex * CellHeight);
            DrawText(context, TrimLabel(row.DisplayName), 10, TextBrush, new Point(4, y + 7));

            for (int columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                RecordingTrainingCellViewModel cell = row.Cells[columnIndex];
                Rect cellBounds = new(
                    LabelWidth + (columnIndex * CellWidth) + 1,
                    y + 1,
                    CellWidth - 2,
                    CellHeight - 2);
                IBrush fill = cell.IsExcluded
                    ? ExcludedBrush
                    : cell.PercentageScore.HasValue
                        ? CreateOutlierBrush(cell.DifferenceFromConsensus)
                        : MissingBrush;
                context.DrawRectangle(fill, BorderPen, cellBounds, 2, 2);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TryHitCell(e.GetPosition(this), out int row, out int column))
        {
            CellClicked?.Invoke(this, new RecordingTrainingCellClickedEventArgs(row, column));
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!TryHitCell(e.GetPosition(this), out int row, out int column))
        {
            _hoveredCell = null;
            Cursor = new Cursor(StandardCursorType.Arrow);
            ToolTip.SetTip(this, null);
            return;
        }

        Cursor = new Cursor(StandardCursorType.Hand);
        if (_hoveredCell == (row, column))
            return;

        _hoveredCell = (row, column);
        RecordingTrainingMatrixViewModel matrix = Matrix!;
        RecordingTrainingColumnViewModel move = matrix.Columns[column];
        RecordingTrainingRowViewModel recording = matrix.Rows[row];
        RecordingTrainingCellViewModel cell = recording.Cells[column];
        string score = cell.PercentageScore.HasValue ? $"{cell.PercentageScore.Value:0.0}%" : "unavailable";
        string state = cell.IsExcluded ? "Excluded from MSM generation" : "Included in MSM generation";
        string difference = cell.PercentageScore.HasValue ? $"{cell.DifferenceFromConsensus:0.0} points from consensus" : cell.Issue ?? string.Empty;
        ToolTip.SetTip(
            this,
            $"{recording.DisplayName}\n{move.MoveId} at beat {move.StartBeat:0.##}\nScore: {score} ({difference})\n{state}\nClick to toggle");
    }

    private bool TryHitCell(Point point, out int row, out int column)
    {
        row = -1;
        column = -1;
        RecordingTrainingMatrixViewModel? matrix = Matrix;
        if (matrix == null || point.X < LabelWidth || point.Y < HeaderHeight)
            return false;

        column = (int)((point.X - LabelWidth) / CellWidth);
        row = (int)((point.Y - HeaderHeight) / CellHeight);
        return row >= 0 && row < matrix.Rows.Count && column >= 0 && column < matrix.Columns.Count;
    }

    private static IBrush CreateOutlierBrush(float differenceFromAverage)
    {
        double amount = Math.Clamp(differenceFromAverage / 35.0, 0, 1);
        Color color = amount < 0.5
            ? Interpolate(Color.FromRgb(55, 154, 88), Color.FromRgb(210, 173, 56), amount * 2)
            : Interpolate(Color.FromRgb(210, 173, 56), Color.FromRgb(205, 58, 62), (amount - 0.5) * 2);
        return new SolidColorBrush(color);
    }

    private static Color Interpolate(Color from, Color to, double amount)
        => Color.FromRgb(
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private static string TrimLabel(string value)
        => value.Length <= 25 ? value : value[..22] + "...";

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point point)
        => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush), point);

    private static void DrawMoveHeader(DrawingContext context, string moveId, double x)
    {
        FormattedText formatted = new(
            moveId.Length <= 24 ? moveId : moveId[..21] + "...",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            MutedTextBrush);
        Point anchor = new(x + (CellWidth / 2), HeaderHeight - 5);
        using (context.PushTransform(Avalonia.Matrix.CreateRotation(-Math.PI / 3, anchor)))
            context.DrawText(formatted, new Point(anchor.X + 2, anchor.Y - formatted.Height));
    }
}
