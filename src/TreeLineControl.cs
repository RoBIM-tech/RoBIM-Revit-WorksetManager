using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace RoBIM.Feature;

public sealed class TreeLineControl : FrameworkElement
{
    private static readonly Pen DottedPen = new(new SolidColorBrush(Color.FromRgb(150, 150, 150)), 1)
    {
        DashStyle = new DashStyle(new double[] { 1, 2 }, 0)
    };
    private static readonly Pen BoxPen = new(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
    private static readonly Brush BoxBrush = Brushes.White;
    private static readonly Pen PlusMinusPen = new(new SolidColorBrush(Color.FromRgb(50, 50, 50)), 1);

    static TreeLineControl()
    {
        DottedPen.Freeze();
        BoxPen.Freeze();
        PlusMinusPen.Freeze();
    }

    public static readonly DependencyProperty NodeProperty =
        DependencyProperty.Register(
            nameof(Node),
            typeof(WorksetHierarchyNode),
            typeof(TreeLineControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                OnNodeChanged));

    public WorksetHierarchyNode? Node
    {
        get => (WorksetHierarchyNode?)GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    private static void OnNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (TreeLineControl)d;
        if (e.OldValue is WorksetHierarchyNode oldNode)
        {
            oldNode.PropertyChanged -= ctrl.OnNodePropertyChanged;
        }
        if (e.NewValue is WorksetHierarchyNode newNode)
        {
            newNode.PropertyChanged += ctrl.OnNodePropertyChanged;
        }
        ctrl.InvalidateMeasure();
        ctrl.InvalidateVisual();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorksetHierarchyNode.IsExpanded) ||
            e.PropertyName == nameof(WorksetHierarchyNode.IsLastSibling))
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Node == null) return new Size(18, 22);
        return new Size(Node.TreeLineWidth, 22);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Node == null) return;

        double h = ActualHeight > 0 ? ActualHeight : 22;
        double midY = Math.Floor(h / 2.0) + 0.5; // 11.5 (픽셀 정렬)

        if (Node.NodeType == HierarchyNodeType.Category)
        {
            // Category [+/-] 사각 박스 (X=4.5 .. 13.5, 중심 X=9.0)
            if (Node.HasChildren)
            {
                DrawBox(dc, 4.5, midY - 4.5, Node.IsExpanded);
                if (Node.IsExpanded)
                {
                    dc.DrawLine(DottedPen, new Point(9.0, midY + 4.5), new Point(9.0, h));
                }
            }
        }
        else if (Node.NodeType == HierarchyNodeType.Family)
        {
            // 부모 카테고리에서 내려오는 세로 점선 (X=9.0)
            if (!Node.IsLastSibling)
            {
                dc.DrawLine(DottedPen, new Point(9.0, 0), new Point(9.0, h));
            }
            else
            {
                dc.DrawLine(DottedPen, new Point(9.0, 0), new Point(9.0, midY));
            }

            // 가로 분기선 (X=9.0 ~ X=20.5)
            dc.DrawLine(DottedPen, new Point(9.0, midY), new Point(20.5, midY));

            // Family [+/-] 사각 박스 (X=20.5 .. 29.5, 중심 X=25.0)
            if (Node.HasChildren)
            {
                DrawBox(dc, 20.5, midY - 4.5, Node.IsExpanded);
                if (Node.IsExpanded)
                {
                    dc.DrawLine(DottedPen, new Point(25.0, midY + 4.5), new Point(25.0, h));
                }
            }
        }
        else if (Node.NodeType == HierarchyNodeType.Type)
        {
            // 부모 카테고리의 세로 점선 (부모 패밀리가 카테고리의 마지막 패밀리가 아닐 때만 통과)
            if (Node.Parent != null && !Node.Parent.IsLastSibling)
            {
                dc.DrawLine(DottedPen, new Point(9.0, 0), new Point(9.0, h));
            }

            // 부모 패밀리에서 내려오는 세로 점선 (X=25.0)
            if (!Node.IsLastSibling)
            {
                dc.DrawLine(DottedPen, new Point(25.0, 0), new Point(25.0, h));
            }
            else
            {
                dc.DrawLine(DottedPen, new Point(25.0, 0), new Point(25.0, midY));
            }

            // 타입 가로 분기선 (X=25.0 ~ X=40.0)
            dc.DrawLine(DottedPen, new Point(25.0, midY), new Point(40.0, midY));
        }
    }

    private static void DrawBox(DrawingContext dc, double x, double y, bool isExpanded)
    {
        // 9x9 정밀 사각 박스
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
