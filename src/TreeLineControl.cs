using System;
using System.Windows;
using System.Windows.Media;

namespace RoBIM.Feature;

public sealed class TreeLineControl : FrameworkElement
{
    private static readonly Pen DottedPen = new(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1)
    {
        DashStyle = new DashStyle(new double[] { 1, 2 }, 0)
    };
    private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1);
    private static readonly Brush BoxBrush = Brushes.White;
    private static readonly Pen PlusMinusPen = new(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 1);

    static TreeLineControl()
    {
        DottedPen.Freeze();
        BoxPen.Freeze();
        PlusMinusPen.Freeze();
    }

    public static readonly DependencyProperty NodeProperty =
        DependencyProperty.Register(nameof(Node), typeof(WorksetHierarchyNode), typeof(TreeLineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public WorksetHierarchyNode? Node
    {
        get => (WorksetHierarchyNode?)GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Node == null) return new Size(16, 22);
        double width = Node.NodeType switch
        {
            HierarchyNodeType.Category => 18,
            HierarchyNodeType.Family => 34,
            _ => 46
        };
        return new Size(width, 22);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Node == null) return;

        double h = ActualHeight > 0 ? ActualHeight : 22;
        double midY = Math.Floor(h / 2); // 11

        if (Node.NodeType == HierarchyNodeType.Category)
        {
            // Category [+/-] 사각 박스 (X=4, Y=midY-4.5)
            DrawBox(dc, 4, midY - 4.5, Node.IsExpanded);

            // 펼쳐져 있고 자식이 있으면 박스 하단에서 바닥까지 세로 점선
            if (Node.IsExpanded && Node.HasChildren)
            {
                dc.DrawLine(DottedPen, new Point(8.5, midY + 4.5), new Point(8.5, h));
            }
        }
        else if (Node.NodeType == HierarchyNodeType.Family)
        {
            // 부모 카테고리의 세로 점선 (X=8.5)
            bool parentHasNext = Node.Parent != null && !Node.Parent.IsLastSibling;
            if (parentHasNext)
            {
                dc.DrawLine(DottedPen, new Point(8.5, 0), new Point(8.5, h));
            }
            else
            {
                dc.DrawLine(DottedPen, new Point(8.5, 0), new Point(8.5, midY));
            }

            // 가로 분기선 (X=8.5 ~ X=20)
            dc.DrawLine(DottedPen, new Point(8.5, midY), new Point(20, midY));

            // Family [+/-] 사각 박스 (X=20, Y=midY-4.5)
            DrawBox(dc, 20, midY - 4.5, Node.IsExpanded);

            // 펼쳐져 있고 자식(타입)이 있으면 박스 하단에서 바닥까지 세로 점선 (X=24.5)
            if (Node.IsExpanded && Node.HasChildren)
            {
                dc.DrawLine(DottedPen, new Point(24.5, midY + 4.5), new Point(24.5, h));
            }
        }
        else if (Node.NodeType == HierarchyNodeType.Type)
        {
            // 부모 카테고리의 세로 점선 (X=8.5)
            var cat = Node.Parent?.Parent;
            if (cat != null && !cat.IsLastSibling)
            {
                dc.DrawLine(DottedPen, new Point(8.5, 0), new Point(8.5, h));
            }

            // 부모 패밀리의 세로 점선 (X=24.5)
            bool isLastType = Node.IsLastSibling;
            if (!isLastType)
            {
                dc.DrawLine(DottedPen, new Point(24.5, 0), new Point(24.5, h));
            }
            else
            {
                dc.DrawLine(DottedPen, new Point(24.5, 0), new Point(24.5, midY));
            }

            // 타입 가로 분기선 (X=24.5 ~ X=38)
            dc.DrawLine(DottedPen, new Point(24.5, midY), new Point(38, midY));
        }
    }

    private static void DrawBox(DrawingContext dc, double x, double y, bool isExpanded)
    {
        // 9x9 박스 렌더링 (Revit VV/VG 완벽 일치)
        dc.DrawRectangle(BoxBrush, BoxPen, new Rect(x, y, 9, 9));
        // 가로 마이너스 선
        dc.DrawLine(PlusMinusPen, new Point(x + 2, y + 4.5), new Point(x + 7, y + 4.5));
        if (!isExpanded)
        {
            // 세로 플러스 선
            dc.DrawLine(PlusMinusPen, new Point(x + 4.5, y + 2), new Point(x + 4.5, y + 7));
        }
    }
}
